using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace Aumentos;

internal sealed record SpellDisplay(Int32Rect Bounds, nint Target, string Champion, ChampionSelection Selection, OpGgBuildView Source);

internal sealed class ChampionSelectAdvice(Ratings ratings, string assets, OpGgBuilds builds,
    ArenaChampionTierSource tiers, Dispatcher dispatcher, Action<SpellDisplay?> update,
    Action<ArenaChampionDisplay?> updateArena)
{
    private int paused;
    internal bool Paused { get => Volatile.Read(ref paused) != 0; set => Volatile.Write(ref paused, value ? 1 : 0); }

    internal async Task Run(CancellationToken cancellation)
    {
        using var lobby = new ChampionSelectClient(Path.Combine(assets, "riotgames.pem"));
        var catalog = ratings.Catalog;
        var draftReader = new ArenaDraftReader(catalog);
        using var draftOcr = new Detection(ratings, Path.Combine(assets, "tessdata"));
        WindowCapture? capture = null;
        ArenaDraftOffer[]? offers = null;
        SpellDisplay? previous = null;
        ArenaChampionDisplay? previousArena = null;
        long checkPatchAt = 0;
        long scanAt = 0;
        long lastOffer = 0;
        bool patchMatches = false;
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                SpellDisplay? display = null;
                ArenaChampionDisplay? arena = null;
                if (!Paused && ForegroundClient() is { } window)
                {
                    var now = Environment.TickCount64;
                    if (now >= checkPatchAt)
                    {
                        patchMatches = LeaguePatch.Installed() == catalog.Patch;
                        checkPatchAt = now + 5000;
                    }
                    var session = patchMatches ? await ReadSession(lobby, cancellation).ConfigureAwait(false) : null;
                    if (!Paused && Native.GetForegroundWindow() == window.Handle)
                    {
                        if (session is { Mode: Mode.Caos, Pick: { } selected })
                        {
                            var champion = catalog.Champions.FirstOrDefault(item =>
                                string.Equals(item.Key, selected.Champion, StringComparison.OrdinalIgnoreCase));
                            if (champion is not null)
                                display = new SpellDisplay(window.Bounds, window.Handle, champion.Name, selected,
                                    builds.Current(new GameContext(Mode.Caos, champion.Key), cancellation));
                        }
                        else if (session?.Mode == Mode.Arena)
                        {
                            var source = tiers.Current(cancellation);
                            ScanOffers(window);
                            arena = ArenaChampionDisplay.Create(window.Bounds, window.Handle, catalog, source, offers);
                        }
                    }
                }
                if (arena is null)
                    StopCapture();
                if (display is not null || previous is not null || arena is not null || previousArena is not null)
                {
                    (previous, previousArena) = await dispatcher.InvokeAsync(() =>
                    {
                        var foreground = Native.GetForegroundWindow();
                        var visible = !Paused && display?.Target == foreground ? display : null;
                        var visibleArena = !Paused && arena?.Target == foreground ? arena : null;
                        update(visible);
                        updateArena(visibleArena);
                        return (visible, visibleArena);
                    }, DispatcherPriority.Normal, cancellation);
                }
                await Task.Delay(420, cancellation).ConfigureAwait(false);
            }
        }
        finally { StopCapture(); }

        void ScanOffers((nint Handle, Int32Rect Bounds, Int32Rect Frame) window)
        {
            if (capture is not null && (capture.Handle != window.Handle ||
                capture.Width != window.Frame.Width || capture.Height != window.Frame.Height))
                StopCapture();

            var now = Environment.TickCount64;
            if (now - lastOffer > 2000)
                offers = null;
            if (now < scanAt)
                return;

            scanAt = now + 800;
            try
            {
                capture ??= new WindowCapture(window.Handle);
                var bounds = window.Bounds;
                var frame = capture.Read(new Int32Rect(bounds.X - window.Frame.X, bounds.Y - window.Frame.Y,
                    bounds.Width, bounds.Height));
                if (frame is null)
                    scanAt = now;
                else if (draftReader.Read(frame, draftOcr) is { Length: > 0 } recognized)
                {
                    if (offers is null || recognized.Length >= offers.Length ||
                        recognized.Any(candidate => !offers.Any(known => known.Champion.Key == candidate.Champion.Key)))
                    {
                        offers = recognized;
                        lastOffer = now;
                    }
                }
            }
            catch (Exception error) when (error is ExternalException or ArgumentException or InvalidOperationException or NotSupportedException)
            {
                StopCapture();
                scanAt = now + 3000;
            }
        }

        void StopCapture()
        {
            capture?.Dispose();
            capture = null;
            offers = null;
            scanAt = 0;
            lastOffer = 0;
        }
    }

    private static async Task<ChampionSelectSession?> ReadSession(ChampionSelectClient lobby, CancellationToken cancellation)
    {
        try { return await lobby.Read(cancellation).ConfigureAwait(false); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or HttpRequestException or
            JsonException or ArgumentException or InvalidOperationException or OperationCanceledException)
        {
            cancellation.ThrowIfCancellationRequested();
            return null;
        }
    }

    private static (nint Handle, Int32Rect Bounds, Int32Rect Frame)? ForegroundClient()
    {
        var handle = Native.GetForegroundWindow();
        Native.GetWindowThreadProcessId(handle, out var pid);
        if (pid == 0) return null;
        try
        {
            using var process = Process.GetProcessById((int)pid);
            if (!string.Equals(process.ProcessName, "LeagueClientUx", StringComparison.OrdinalIgnoreCase))
                return null;
            var frame = Native.Bounds(handle);
            var bounds = Native.ClientBounds(handle, frame);
            return bounds.Width >= 400 && bounds.Height >= 320 ? (handle, bounds, frame) : null;
        }
        catch (ArgumentException) { return null; }
        catch (InvalidOperationException) { return null; }
    }
}

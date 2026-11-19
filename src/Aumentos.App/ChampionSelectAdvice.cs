using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace Aumentos;

internal sealed record SpellDisplay(Int32Rect Bounds, nint Target, string Champion, ChampionSelection Selection, OpGgBuildView Source);

internal sealed class ChampionSelectAdvice(Catalog catalog, string assets, OpGgBuilds builds,
    Dispatcher dispatcher, Action<SpellDisplay?> update)
{
    private int paused;
    internal bool Paused { get => Volatile.Read(ref paused) != 0; set => Volatile.Write(ref paused, value ? 1 : 0); }

    internal async Task Run(CancellationToken cancellation)
    {
        using var lobby = new ChampionSelectClient(Path.Combine(assets, "riotgames.pem"));
        SpellDisplay? previous = null;
        long checkPatchAt = 0;
        bool patchMatches = false;
        while (!cancellation.IsCancellationRequested)
        {
            SpellDisplay? display = null;
            if (!Paused && ForegroundClient() is { } window)
            {
                var now = Environment.TickCount64;
                if (now >= checkPatchAt)
                {
                    patchMatches = LeaguePatch.Installed() == catalog.Patch;
                    checkPatchAt = now + 5000;
                }
                if (patchMatches)
                {
                    ChampionSelection? selected;
                    try { selected = await lobby.Read(cancellation).ConfigureAwait(false); }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException or HttpRequestException or
                        JsonException or ArgumentException or InvalidOperationException or OperationCanceledException)
                    {
                        selected = null;
                    }
                    if (selected is not null && !Paused && Native.GetForegroundWindow() == window.Handle)
                    {
                        var champion = catalog.Champions.FirstOrDefault(item =>
                            string.Equals(item.Key, selected.Champion, StringComparison.OrdinalIgnoreCase));
                        if (champion is not null)
                            display = new SpellDisplay(window.Bounds, window.Handle, champion.Name, selected,
                                builds.Current(new GameContext(Mode.Caos, champion.Key), cancellation));
                    }
                }
            }
            if (display is not null || previous is not null)
            {
                previous = await dispatcher.InvokeAsync(() =>
                {
                    var visible = display is { } active && !Paused && Native.GetForegroundWindow() == active.Target ? active : null;
                    update(visible);
                    return visible;
                }, DispatcherPriority.Normal, cancellation);
            }
            await Task.Delay(420, cancellation).ConfigureAwait(false);
        }
    }

    private static (nint Handle, Int32Rect Bounds)? ForegroundClient()
    {
        var handle = Native.GetForegroundWindow();
        Native.GetWindowThreadProcessId(handle, out var pid);
        if (pid == 0) return null;
        try
        {
            using var process = Process.GetProcessById((int)pid);
            if (!string.Equals(process.ProcessName, "LeagueClientUx", StringComparison.OrdinalIgnoreCase))
                return null;
            var bounds = Native.ClientBounds(handle, Native.Bounds(handle));
            return bounds.Width >= 400 && bounds.Height >= 320 ? (handle, bounds) : null;
        }
        catch (ArgumentException) { return null; }
        catch (InvalidOperationException) { return null; }
    }
}

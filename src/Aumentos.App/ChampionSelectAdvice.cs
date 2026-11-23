using System;
using System.Collections.Generic;
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

internal sealed record SpellDisplay(Int32Rect Bounds, nint Target, string Champion, ChampionSelection Selection,
    OpGgBuildView Source, string? Tier = null, bool TierIsPrevious = false);

internal sealed class ChampionSelectAdvice(Ratings ratings, string assets, OpGgBuilds builds,
    ArenaChampionTierSource tiers, MayhemChampionTiers mayhemTiers, MayhemBenchPortraits mayhemBench,
    Dispatcher dispatcher, Action<SpellDisplay?> update,
    Action<ArenaChampionDisplay?> updateArena, Action<MayhemChampionDisplay?> updateMayhem,
    Action<MayhemTeamDisplay?> updateMayhemTeam, ArenaTeamSource? teams = null)
{
    private int paused;
    private ArenaVoteDraft? voteDraft;
    internal bool Paused { get => Volatile.Read(ref paused) != 0; set => Volatile.Write(ref paused, value ? 1 : 0); }
    internal ArenaVoteDraft? VoteDraft => Volatile.Read(ref voteDraft);

    internal async Task Run(CancellationToken cancellation)
    {
        using var lobby = new ChampionSelectClient(Path.Combine(assets, "riotgames.pem"));
        var catalog = ratings.Catalog;
        var mayhemReader = new MayhemDraftReader(catalog);
        var mayhemComposition = new MayhemCompositionAdvice(ratings);
        using var draftOcr = new Detection(ratings, Path.Combine(assets, "tessdata"));
        WindowCapture? capture = null;
        (nint Handle, Int32Rect Bounds, Int32Rect Frame)? scanWindow = null;
        MayhemDraftChoice[]? choices = null;
        MayhemBenchChoice[] bench = [];
        Task<MayhemDraftChoice[]?>? draftRead = null;
        int captureGeneration = 0;
        int? draftGeneration = null;
        SpellDisplay? previous = null;
        ArenaChampionDisplay? previousArena = null;
        MayhemChampionDisplay? previousMayhem = null;
        MayhemTeamDisplay? previousMayhemTeam = null;
        long checkPatchAt = 0;
        long scanAt = 0;
        long captureRetryAt = 0;
        long draftFrameAt = 0;
        long lastFrame = 0;
        bool patchMatches = false;
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                SpellDisplay? display = null;
                ArenaChampionDisplay? arena = null;
                MayhemChampionDisplay? mayhem = null;
                MayhemTeamDisplay? mayhemTeam = null;
                bool scanning = false;
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
                        mayhemTiers.Current(cancellation);
                        mayhemBench.Prefetch(cancellation);
                    }
                    var session = patchMatches ? await ReadSession(lobby, cancellation).ConfigureAwait(false) : null;
                    if (session is not null && !Paused && Native.GetForegroundWindow() == window.Handle)
                    {
                        if (session.Mode == Mode.Caos)
                        {
                            Volatile.Write(ref voteDraft, null);
                            scanning = true;
                            if (scanWindow != window)
                            {
                                StopCapture();
                                scanWindow = window;
                            }
                            var source = mayhemTiers.Current(cancellation);
                            var selected = session.Pick;
                            ScanMayhem(window, readOffer: selected is null);
                            var squad = session.MayhemTeam;
                            var selectedKey = selected?.Champion;
                            var allies = squad?.Teammates ?? [];
                            var (available, emptyMessage) = MayhemOptions(session, choices, bench);
                            var grade = mayhemComposition.Evaluate(selectedKey, allies);
                            var suggestions = mayhemComposition.Recommend(source.Data, selectedKey, allies, available);
                            mayhemTeam = MayhemTeamDisplay.Create(window.Bounds, window.Handle, catalog, source, selectedKey,
                                available, emptyMessage, suggestions, grade);
                            mayhem = MayhemChampionDisplay.Create(window.Bounds, window.Handle, source.Data, selected, choices, bench);
                            if (selected is not null)
                            {
                                var champion = catalog.Champions.FirstOrDefault(item =>
                                    string.Equals(item.Key, selected.Champion, StringComparison.OrdinalIgnoreCase));
                                if (champion is not null)
                                    display = new SpellDisplay(window.Bounds, window.Handle, champion.Name, selected,
                                        builds.Current(new GameContext(Mode.Caos, champion.Key), cancellation),
                                        source.Data?.Letter(champion.Key),
                                        source.Data is { } tierData && tierData.SourcePatch != catalog.Patch);
                            }
                        }
                        else if (session.Mode == Mode.Arena)
                        {
                            if (session.ArenaTeam is { } team)
                                Volatile.Write(ref voteDraft, session.ArenaPicksClosed && session.ArenaChampion is { } key &&
                                    team.Size is 2 or 3 && team.Teammates.Length == team.Size - 1
                                    ? new ArenaVoteDraft(key, team, Environment.TickCount64) : null);
                            var source = tiers.Current(cancellation);
                            var composition = ArenaCompositionAdvice.Recommend(ratings, source.Data, session.ArenaTeam);
                            var observed = teams?.Find(session.ArenaChampion, session.ArenaTeam, cancellation);
                            var grade = ArenaCompositionAdvice.Evaluate(ratings, session.ArenaChampion, session.ArenaTeam, observed);
                            arena = ArenaChampionDisplay.Create(window.Bounds, window.Handle, catalog, source,
                                session.ArenaChampion, composition, grade, session.ArenaTeam);
                        }
                    }
                }
                if (!scanning && scanWindow is not null)
                {
                    StopCapture();
                    scanWindow = null;
                }
                if (display is not null || previous is not null || arena is not null || previousArena is not null ||
                    mayhem is not null || previousMayhem is not null || mayhemTeam is not null || previousMayhemTeam is not null)
                {
                    (previous, previousArena, previousMayhem, previousMayhemTeam) = await dispatcher.InvokeAsync(() =>
                    {
                        var foreground = Native.GetForegroundWindow();
                        var visible = !Paused && display?.Target == foreground ? display : null;
                        var visibleArena = !Paused && arena?.Target == foreground ? arena : null;
                        var visibleMayhem = !Paused && mayhem?.Target == foreground ? mayhem : null;
                        var visibleTeam = !Paused && mayhemTeam?.Target == foreground ? mayhemTeam : null;
                        update(visible);
                        updateArena(visibleArena);
                        updateMayhem(visibleMayhem);
                        updateMayhemTeam(visibleTeam);
                        return (visible, visibleArena, visibleMayhem, visibleTeam);
                    }, DispatcherPriority.Normal, cancellation);
                }
                await Task.Delay(420, cancellation).ConfigureAwait(false);
            }
        }
        finally
        {
            StopCapture();
            if (draftRead is not null)
                await draftRead.ConfigureAwait(false);
        }

        void ScanMayhem((nint Handle, Int32Rect Bounds, Int32Rect Frame) window, bool readOffer)
        {
            var now = Environment.TickCount64;
            if (!readOffer)
            {
                // A pending OCR result can refer to a frame before the client confirmed the pick.
                choices = null;
                draftGeneration = null;
                scanAt = 0;
            }
            if (now - lastFrame > 1500)
            {
                choices = null;
                bench = [];
            }
            if (draftRead is { IsCompleted: true })
            {
                var read = draftRead.GetAwaiter().GetResult();
                if (draftGeneration == captureGeneration && now - draftFrameAt <= 1500)
                    choices = read;
                draftRead = null;
            }
            if (ReadFrame(window, now) is { } frame)
            {
                // The portrait row can change as allies choose or swap. Title OCR runs independently.
                bench = mayhemBench.Read(frame);
                lastFrame = now;
                if (readOffer && draftRead is null && now >= scanAt)
                {
                    scanAt = now + 600;
                    draftGeneration = captureGeneration;
                    draftFrameAt = now;
                    draftRead = Task.Run(() => mayhemReader.Read(frame, draftOcr));
                }
            }
        }

        System.Windows.Media.Imaging.BitmapSource? ReadFrame((nint Handle, Int32Rect Bounds, Int32Rect Frame) window, long now)
        {
            if (now < captureRetryAt)
                return null;
            try
            {
                capture ??= new WindowCapture(window.Handle);
                var bounds = window.Bounds;
                var frame = capture.Read(new Int32Rect(bounds.X - window.Frame.X, bounds.Y - window.Frame.Y,
                    bounds.Width, bounds.Height));
                return frame;
            }
            catch (Exception error) when (error is ExternalException or ArgumentException or InvalidOperationException or NotSupportedException)
            {
                StopCapture();
                scanWindow = window;
                captureRetryAt = now + 3000;
                return null;
            }
        }

        void StopCapture()
        {
            capture?.Dispose();
            capture = null;
            choices = null;
            bench = [];
            captureGeneration++;
            scanAt = 0;
            captureRetryAt = 0;
            lastFrame = 0;
        }
    }

    internal static (string[] Keys, string EmptyMessage) MayhemOptions(ChampionSelectSession session,
        MayhemDraftChoice[]? cards, MayhemBenchChoice[] portraits)
    {
        var team = session.MayhemTeam;
        var selected = session.Pick?.Champion;
        var (visible, emptyMessage) = (selected, team) switch
        {
            (null, _) => ((cards ?? []).Select(card => card.Champion.Key), "Esperando tus cartas…"),
            (_, { BenchKnown: true } draft) => ((IEnumerable<string>)draft.Bench, "El banco está vacío."),
            _ => (portraits.Select(portrait => portrait.Champion.Key), "Esperando identificar el banco…")
        };
        var excluded = new HashSet<string>(team?.Unavailable ?? [], StringComparer.OrdinalIgnoreCase);
        excluded.UnionWith(team?.Teammates ?? []);
        if (selected is not null)
            excluded.Add(selected);

        return (visible.Where(key => !excluded.Contains(key)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), emptyMessage);
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

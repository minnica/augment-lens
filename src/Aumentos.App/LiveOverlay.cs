using System;
using System.Collections.Generic;
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

internal record OverlayUpdate(string Status, Int32Rect? Bounds = null, Calibration? Layout = null,
    CardComparison?[]? Labels = null, nint Target = 0, PrismaticDisplay? Prismatics = null, Mode? Mode = null,
    int? ArenaCardTop = null, bool ArenaUpgradeHeading = false, bool ArenaItemOffer = false);

internal sealed class LiveOverlay
{
    private const int FramePollInterval = 40;
    private const int CardScanInterval = 110;
    private readonly Ratings ratings;
    private readonly LayoutStore layouts;
    private readonly CardHistory history;
    private readonly OpGgRatings opgg;
    private readonly AramMayhemRatings aramMayhem;
    private readonly GgezArenaRatings ggez;
    private readonly GgezArenaGlobalRatings ggezGlobal;
    private readonly AramHexRatings aramHex;
    private readonly OpGgBuilds builds;
    private readonly string assets;
    private readonly Dispatcher dispatcher;
    private readonly Action<OverlayUpdate> update;
    private readonly Action<BuildDisplay?> updateBuild;
    private readonly BuildVisibility buildVisibility = new();
    private int paused;

    public LiveOverlay(Ratings ratings, LayoutStore layouts, CardHistory history, OpGgRatings opgg,
        AramMayhemRatings aramMayhem, GgezArenaRatings ggez, GgezArenaGlobalRatings ggezGlobal, AramHexRatings aramHex,
        OpGgBuilds builds, string assets,
        Dispatcher dispatcher, Action<OverlayUpdate> update, Action<BuildDisplay?> updateBuild)
    {
        this.ratings = ratings;
        this.layouts = layouts;
        this.history = history;
        this.opgg = opgg;
        this.aramMayhem = aramMayhem;
        this.ggez = ggez;
        this.ggezGlobal = ggezGlobal;
        this.aramHex = aramHex;
        this.builds = builds;
        this.assets = assets;
        this.dispatcher = dispatcher;
        this.update = update;
        this.updateBuild = updateBuild;
    }

    public bool Paused { get => Volatile.Read(ref paused) != 0; set => Volatile.Write(ref paused, value ? 1 : 0); }
    public bool ToggleBuild() => buildVisibility.Toggle();

    public async Task Run(CancellationToken cancellation)
    {
        using var live = new LiveGame(Path.Combine(assets, "riotgames.pem"));
        using var detector = new Detection(ratings, Path.Combine(assets, "tessdata"));
        var shopDetector = new ShopDetection();
        var mayhemItems = MayhemItemCatalog.Load(ratings.Catalog.Patch, assets);
        var mayhemBuilds = new MayhemBuilds(ratings, mayhemItems);
        var mayhemAdvice = new MayhemAugmentAdvice(ratings, mayhemItems);
        var arenaBuilds = new ArenaBuilds(ratings);
        WindowCapture? capture = null;
        Mode? activeMode = null;
        GameContext? game = null;
        LiveMatch? match = null;
        LiveMatch? confirmedMatch = null;
        long lastMatchRead = 0;
        (LiveMatch? Match, OpGgBuildSnapshot? Source) planInput = default;
        MayhemBuildPlan? plan = null;
        EnemyDamageMix? enemyDamage = null;
        EnemyDamageMix? lobbyDamage = null;
        bool hideStartingItems = false;
        long nextPoll = 0;
        Task<LiveMatchState?>? pendingMatch = null;
        (nint Handle, uint ProcessId) pendingWindow = default;
        long lastFrame = 0;
        long lastOffer = 0;
        long nextCardScan = 0;
        string idle = "Esperando la partida…";
        Calibration? previous = null;
        bool? arenaUpgradeHeading = null;
        GameWindow? lastWindow = null;
        BuildDisplay? lastBuild = null;
        bool cardsVisible = false;
        PrismaticDisplay? prismaticDisplay = null;
        Int32Rect? shop = null;
        Int32Rect? knownShop = null;
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                var window = Paused ? null : GameWindow.Foreground(lastWindow);
                if (window is not null)
                {
                    if (window.Handle != lastWindow?.Handle || window.ProcessId != lastWindow?.ProcessId)
                    {
                        StopCapture();
                        knownShop = null;
                        buildVisibility.ObserveShop(open: false);
                        history.ResetMatch();
                        arenaBuilds.ResetSwap();
                        activeMode = null;
                        game = null;
                        match = null;
                        confirmedMatch = null;
                        nextPoll = 0;
                    }
                    else if (lastWindow is { } prior &&
                        (window.Bounds.Width != prior.Bounds.Width || window.Bounds.Height != prior.Bounds.Height))
                        knownShop = null;
                    lastWindow = window;
                }
                OverlayUpdate? result;
                BuildDisplay? build = null;
                if (window is null || window.Bounds.Width < 1 || window.Bounds.Height < 1)
                {
                    StopCapture();
                    activeMode = null;
                    game = null;
                    match = null;
                    nextPoll = 0;
                    result = new OverlayUpdate(Paused ? "Pausado" : "Esperando la ventana de League…");
                }
                else
                {
                    var now = Environment.TickCount64;
                    if (pendingMatch is { IsCompleted: true } completed)
                    {
                        pendingMatch = null;
                        bool sameWindow = pendingWindow == (window.Handle, window.ProcessId);
                        try
                        {
                            var state = await completed.ConfigureAwait(false);
                            var current = state?.Match;
                            if (sameWindow && (current is not null || state?.Mode != activeMode || now - lastMatchRead >= 2500))
                            {
                                bool newMatch = current is not null && confirmedMatch is not null && current.IsNewSession(confirmedMatch);
                                if (game != current?.Game || state?.Mode != activeMode || newMatch)
                                    StopCapture();
                                if (newMatch)
                                {
                                    history.ResetMatch();
                                    arenaBuilds.ResetSwap();
                                }
                                if (current is not null)
                                {
                                    arenaBuilds.ObserveInventory(current.Game, current.Arena);
                                    confirmedMatch = current.GameTime.HasValue || newMatch
                                        ? current : current with { GameTime = confirmedMatch?.GameTime };
                                    lastMatchRead = Environment.TickCount64;
                                }
                                activeMode = state?.Mode;
                                game = current?.Game;
                                match = current;
                            }
                        }
                        catch (Exception error) when (error is HttpRequestException or JsonException or KeyNotFoundException
                            or InvalidOperationException or TaskCanceledException)
                        {
                            if (sameWindow && game is not null && Environment.TickCount64 - lastMatchRead >= 2500)
                            {
                                StopCapture();
                                game = null;
                                match = null;
                            }
                        }
                    }
                    if (pendingMatch is null && now >= nextPoll)
                    {
                        pendingWindow = (window.Handle, window.ProcessId);
                        pendingMatch = live.ReadMatch(cancellation);
                        nextPoll = now + 1000;
                    }
                    var champion = game is not null ? ChampionName(game) : null;
                    result = activeMode is { } mode
                        ? ReadOverlay(window, mode, champion is null ? null : game, champion, match)
                        : new OverlayUpdate("Esperando ARAM Caos o Arena…");
                    if (result is not null)
                    {
                        cardsVisible = result.Labels is not null;
                        prismaticDisplay = result.Prismatics;
                    }
                    if (activeMode is { } buildMode && window.Patch == ratings.Catalog.Patch &&
                        !Paused && Native.GetForegroundWindow() == window.Handle)
                    {
                        bool showShopBuild = !cardsVisible && buildVisibility.ShouldShow(shop.HasValue);
                        if (game is { } currentGame && champion is not null)
                        {
                            var suggestion = builds.Current(currentGame, cancellation);
                            if (prismaticDisplay is not null)
                            {
                                build = new BuildDisplay(window.Bounds, window.Handle, champion,
                                    currentGame.Mode, null, Prismatics: prismaticDisplay);
                            }
                            else if (showShopBuild)
                            {
                                if (planInput != (match, suggestion.Data))
                                {
                                    planInput = (match, suggestion.Data);
                                    var updated = currentGame.Mode == Mode.Arena
                                        ? arenaBuilds.Recommend(currentGame, match?.Arena, suggestion.Data)
                                        : mayhemBuilds.Recommend(currentGame, match?.Mayhem, suggestion.Data);
                                    if (currentGame.Mode == Mode.Arena)
                                        arenaBuilds.RememberSuggestedSwap(currentGame, updated);
                                    if (updated is null || plan is null || updated.Swap != plan.Swap ||
                                        !updated.Steps.SequenceEqual(plan.Steps))
                                        plan = updated;
                                    enemyDamage = currentGame.Mode == Mode.Arena
                                        ? arenaBuilds.EstimateEnemyDamage(match?.Arena)
                                        : mayhemBuilds.EstimateEnemyDamage(match?.Mayhem);
                                    lobbyDamage = arenaBuilds.EstimateLobbyDamage(match?.Arena);
                                    hideStartingItems = mayhemBuilds.HasStartingPurchase(match?.Mayhem, suggestion.Data);
                                }
                                build = new BuildDisplay(window.Bounds, window.Handle, champion,
                                    currentGame.Mode, suggestion, shop, plan, ClientPatch: ratings.Catalog.Patch,
                                    ArenaRivalsDetected: match?.Arena?.Opponents.Length > 0, EnemyDamage: enemyDamage,
                                    HideStartingItems: hideStartingItems, LobbyDamage: lobbyDamage);
                            }
                        }
                        else if (showShopBuild)
                            build = new BuildDisplay(window.Bounds, window.Handle, null, buildMode, null, shop);
                    }
                }

                history.WriteIfDue(Environment.TickCount64);
                if (result is not null || lastBuild != build)
                    await dispatcher.InvokeAsync(() =>
                    {
                        if (result is not null)
                            update(result);
                        if (lastBuild != build)
                            updateBuild(build);
                    }, DispatcherPriority.Normal, cancellation);
                lastBuild = build;
                await Task.Delay(window is null ? 180 : FramePollInterval, cancellation).ConfigureAwait(false);
            }
        }
        finally
        {
            capture?.Dispose();
            if (pendingMatch is not null)
            {
                try { await pendingMatch.ConfigureAwait(false); }
                catch (Exception error) when (error is HttpRequestException or JsonException or KeyNotFoundException
                    or InvalidOperationException or TaskCanceledException) { }
            }
        }

        void StopCapture()
        {
            capture?.Dispose();
            capture = null;
            previous = null;
            arenaUpgradeHeading = null;
            detector.Clear();
            lastFrame = 0;
            lastOffer = 0;
            nextCardScan = 0;
            cardsVisible = false;
            prismaticDisplay = null;
            shop = null;
        }

        OverlayUpdate? ReadOverlay(GameWindow window, Mode mode, GameContext? current, string? name, LiveMatch? currentMatch)
        {
            if (!string.Equals(window.Patch, ratings.Catalog.Patch, StringComparison.Ordinal))
            {
                StopCapture();
                return new OverlayUpdate($"Datos para {ratings.Catalog.Patch}. Cliente: {window.Patch ?? "parche desconocido"}.");
            }
            if (capture is not null && (window.Handle != capture.Handle ||
                capture.Width != window.Frame.Width || capture.Height != window.Frame.Height))
                StopCapture();

            var label = mode == Mode.Caos ? "ARAM Caos" : "Arena";
            try
            {
                capture ??= new WindowCapture(window.Handle);
                var scan = new Int32Rect(0, 0, window.Bounds.Width, window.Bounds.Height);
                var edge = knownShop is { } remembered ? ShopDetection.ProbeRegion(remembered, scan.Width, scan.Height) : null;
                bool scanCards = Environment.TickCount64 >= nextCardScan;
                if (!scanCards && edge is null)
                    return null;
                // Consultar solo el borde entre cuadros completos evita copiar toda la ventana para seguir la tienda.
                var region = scanCards ? scan : edge.GetValueOrDefault();
                var captureRegion = new Int32Rect(window.Bounds.X - window.Frame.X + region.X,
                    window.Bounds.Y - window.Frame.Y + region.Y, region.Width, region.Height);
                var frame = scanCards ? capture.Read(captureRegion) : capture.ReadProbe(captureRegion);
                if (frame is null)
                {
                    if (Environment.TickCount64 - lastFrame <= 600)
                        return null;
                    shop = null;
                    return new OverlayUpdate($"{label} · Esperando imagen de League…");
                }
                lastFrame = Environment.TickCount64;

                bool wasOpen = shop.HasValue;
                bool edgeVisible = edge is { } border && shopDetector.MatchesProbe(frame,
                    scanCards ? border : new Int32Rect(0, 0, region.Width, region.Height));
                shop = edgeVisible ? knownShop : null;
                if (!edgeVisible && scanCards && (!wasOpen || edge is null))
                {
                    shop = shopDetector.Read(frame, detector, previous: null);
                    if (shop is { } located)
                        knownShop = ShopDetection.ProbeRegion(located, scan.Width, scan.Height) is { } candidateEdge &&
                            shopDetector.MatchesProbe(frame, candidateEdge) ? located : null;
                }
                buildVisibility.ObserveShop(shop.HasValue);
                if (!scanCards)
                    return null;
                nextCardScan = lastFrame + CardScanInterval;
                if (current is null || name is null)
                    return new OverlayUpdate($"{label} · No se reconoció tu campeón.");
                if (!cardsVisible && wasOpen != shop.HasValue)
                    return null;

                var source = opgg.Current(current, cancellation);
                var mayhem = aramMayhem.Current(current.Mode, cancellation);
                var arena = ggez.Current(current, cancellation);
                var global = current.Mode == Mode.Arena ? ggezGlobal.Current(cancellation) : null;
                var hex = current.Mode == Mode.Caos ? aramHex.Current(cancellation) : null;
                var sources = string.Join(" · ", new[] { source.Status, mayhem?.Status, arena?.Status, global?.Status, hex?.Status }
                    .OfType<string>());
                idle = $"{label} · {name} · Esperando cartas… · {sources}";
                var candidates = layouts.Find(current.Mode, scan.Width, scan.Height);
                if (candidates.Length == 0)
                    return new OverlayUpdate($"{label} · Calibra un panel de {window.Bounds.Width} × {window.Bounds.Height}.");
                string status = idle;
                var read = detector.ReadPanel(frame, scan, candidates, current.Mode, previous);
                if (read is { Layout: var candidate, Result: var result })
                {
                    int? arenaCardTop = current.Mode == Mode.Arena ? ArenaCardEdge.Read(frame, candidate) : null;
                    var shards = ratings.Shards.ForOffer(current, result);
                    var prismatics = shards is null ? ratings.Prismatics.ForOffer(current, result) : null;
                    if ((shards ?? prismatics?.Badges ?? ratings.Legendaries.ForOffer(current, result)) is { } choices)
                    {
                        if (Native.GetForegroundWindow() != window.Handle || Paused)
                            return new OverlayUpdate("Esperando la ventana de League…");
                        previous = candidate;
                        arenaUpgradeHeading = null;
                        lastOffer = Environment.TickCount64;
                        history.ObserveClosed(window.Handle, lastOffer);
                        var offers = shards is not null ? "Fragmentos" : prismatics is not null ? "Prismáticos" : "Legendarios";
                        var tiers = string.Join(" | ", choices.Select(badge => badge?.LocalText ?? "?"));
                        return new OverlayUpdate($"{label} · {name} · {offers} · Tier local · {tiers}",
                            window.Bounds, candidate, choices, window.Handle,
                            prismatics is null ? null : new(prismatics.Top, candidate, ratings.Catalog.Patch), current.Mode,
                            ArenaCardTop: arenaCardTop, ArenaItemOffer: shards is null);
                    }
                    if (result.CanShow(candidate == previous))
                    {
                        if (candidate != previous)
                            arenaUpgradeHeading = null;
                        previous = candidate;
                        var badges = CardComparison.ForOffer(ratings, current, result.Cards, result.Items,
                            source.Data, source.Loading, mayhem?.Data, arena?.Data,
                            upgradeLevels: result.UpgradeLevels,
                            arenaItems: currentMatch?.Arena is { InventoryKnown: true } arenaMatch ? arenaMatch.Items : null,
                            ggezGlobal: global?.Data,
                            aramHex: hex?.Data);
                        if (arenaUpgradeHeading is null && result.UpgradeLevels?.Any(level => level.HasValue) == true)
                            arenaUpgradeHeading = detector.HasArenaUpgradeHeading(frame);
                        if (Native.GetForegroundWindow() != window.Handle || Paused)
                            return new OverlayUpdate("Esperando la ventana de League…");
                        lastOffer = Environment.TickCount64;
                        history.Observe(window.Handle, current.Mode, name, result.Cards, badges, lastOffer);
                        mayhemAdvice.Apply(current, currentMatch?.Mayhem, result.Cards, badges);
                        var displayed = string.Join(" | ", badges.Select(badge => badge is null ? "—" :
                            badge == CardComparison.Unread ? "?" : $"{badge.ExternalText} / {badge.VisibleTierText}" +
                            (badge.RecommendedUpgrade ? " · Mejora recomendada" :
                                badge.RecommendedAugment ? " · Recomendado" : "")));
                        return new OverlayUpdate($"{label} · {name} · {sources} · OP.GG / local · {displayed}",
                            window.Bounds, candidate, badges, window.Handle, Mode: current.Mode,
                            ArenaCardTop: arenaCardTop, ArenaUpgradeHeading: arenaUpgradeHeading == true);
                    }
                    status = result.Items.Any(item => item)
                        ? $"{label} · Panel de objetos"
                        : $"{label} · Panel abierto · {result.Cards.Count(id => id.HasValue)} aumentos reconocidos";
                }
                if (previous is not null && Environment.TickCount64 - lastOffer < 300 &&
                    (current.Mode == Mode.Arena ? detector.PanelVisible(frame, scan, previous) : read is null))
                    return null;
                previous = null;
                arenaUpgradeHeading = null;
                detector.Clear();
                history.ObserveClosed(window.Handle, Environment.TickCount64);
                return new OverlayUpdate(status);
            }
            catch (Exception error) when (error is COMException or InvalidOperationException or
                ArgumentException or SharpGen.Runtime.SharpGenException)
            {
                StopCapture();
                return new OverlayUpdate($"Captura no disponible · {error.Message}");
            }
        }
    }

    private string? ChampionName(GameContext game) => ratings.Catalog.Champions.FirstOrDefault(item =>
        string.Equals(item.Key, game.Champion, StringComparison.OrdinalIgnoreCase))?.Name;
}

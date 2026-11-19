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
    CardComparison?[]? Labels = null, nint Target = 0, PrismaticDisplay? Prismatics = null);

internal sealed class LiveOverlay
{
    private readonly Ratings ratings;
    private readonly LayoutStore layouts;
    private readonly CardHistory history;
    private readonly OpGgRatings opgg;
    private readonly AramMayhemRatings aramMayhem;
    private readonly GgezArenaRatings ggez;
    private readonly OpGgBuilds builds;
    private readonly string assets;
    private readonly Dispatcher dispatcher;
    private readonly Action<OverlayUpdate> update;
    private readonly Action<BuildDisplay?> updateBuild;
    private readonly BuildVisibility buildVisibility = new();
    private int paused;

    public LiveOverlay(Ratings ratings, LayoutStore layouts, CardHistory history, OpGgRatings opgg,
        AramMayhemRatings aramMayhem, GgezArenaRatings ggez, OpGgBuilds builds, string assets,
        Dispatcher dispatcher, Action<OverlayUpdate> update, Action<BuildDisplay?> updateBuild)
    {
        this.ratings = ratings;
        this.layouts = layouts;
        this.history = history;
        this.opgg = opgg;
        this.aramMayhem = aramMayhem;
        this.ggez = ggez;
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
        var mayhemBuilds = MayhemBuilds.Load(ratings, assets);
        var arenaBuilds = new ArenaBuilds(ratings);
        WindowCapture? capture = null;
        GameContext? game = null;
        LiveMatch? match = null;
        LiveMatch? confirmedMatch = null;
        long lastMatchRead = 0;
        (LiveMatch? Match, OpGgBuildSnapshot? Source) planInput = default;
        MayhemBuildPlan? plan = null;
        EnemyDamageMix? enemyDamage = null;
        bool hideStartingItems = false;
        long nextPoll = 0;
        long lastFrame = 0;
        long lastOffer = 0;
        long nextShopScan = 0;
        string idle = "Esperando la partida…";
        Calibration? previous = null;
        GameWindow? lastWindow = null;
        BuildDisplay? lastBuild = null;
        bool cardsVisible = false;
        PrismaticDisplay? prismaticDisplay = null;
        Int32Rect? shop = null;
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
                        buildVisibility.ObserveShop(open: false);
                        history.ResetMatch();
                        game = null;
                        match = null;
                        confirmedMatch = null;
                        nextPoll = 0;
                    }
                    lastWindow = window;
                }
                OverlayUpdate? result;
                BuildDisplay? build = null;
                if (window is null || window.Bounds.Width < 1 || window.Bounds.Height < 1)
                {
                    StopCapture();
                    game = null;
                    match = null;
                    nextPoll = 0;
                    result = new OverlayUpdate(Paused ? "Pausado" : "Esperando la ventana de League…");
                }
                else
                {
                    var now = Environment.TickCount64;
                    if (now >= nextPoll)
                    {
                        try
                        {
                            var current = await live.ReadMatch(cancellation).ConfigureAwait(false);
                            bool newMatch = current is not null && confirmedMatch is not null && current.IsNewSession(confirmedMatch);
                            if (game != current?.Game || newMatch)
                                StopCapture();
                            if (newMatch)
                                history.ResetMatch();
                            if (current is not null)
                                confirmedMatch = current.GameTime.HasValue || newMatch
                                    ? current : current with { GameTime = confirmedMatch?.GameTime };
                            game = current?.Game;
                            match = current;
                            lastMatchRead = Environment.TickCount64;
                        }
                        catch (Exception error) when (error is HttpRequestException or JsonException or KeyNotFoundException
                            or InvalidOperationException or TaskCanceledException)
                        {
                            if (Environment.TickCount64 - lastMatchRead >= 2500)
                            {
                                StopCapture();
                                game = null;
                                match = null;
                            }
                        }
                        nextPoll = Environment.TickCount64 + 1000;
                    }
                    result = game is null
                        ? new OverlayUpdate("Esperando ARAM Caos o Arena…")
                        : ReadOverlay(window, game, match?.Arena is { InventoryKnown: true } arenaMatch ? arenaMatch.Items : null);
                    if (result is not null)
                    {
                        cardsVisible = result.Labels is not null;
                        prismaticDisplay = result.Prismatics;
                    }
                    if (game is { } currentGame && window.Patch == ratings.Catalog.Patch)
                    {
                        var suggestion = builds.Current(currentGame, cancellation);
                        bool canShow = !Paused && Native.GetForegroundWindow() == window.Handle;
                        if (canShow && prismaticDisplay is not null)
                        {
                            build = new BuildDisplay(window.Bounds, window.Handle, ChampionName(currentGame),
                                currentGame.Mode, null, Prismatics: prismaticDisplay);
                        }
                        else if (canShow && !cardsVisible && buildVisibility.ShouldShow(shop.HasValue))
                        {
                            if (planInput != (match, suggestion.Data))
                            {
                                planInput = (match, suggestion.Data);
                                var updated = currentGame.Mode == Mode.Arena
                                    ? arenaBuilds.Recommend(currentGame, match?.Arena, suggestion.Data)
                                    : mayhemBuilds.Recommend(currentGame, match?.Mayhem, suggestion.Data);
                                if (updated is null || plan is null || updated.Swap != plan.Swap ||
                                    !updated.Steps.SequenceEqual(plan.Steps))
                                    plan = updated;
                                enemyDamage = currentGame.Mode == Mode.Arena
                                    ? arenaBuilds.EstimateEnemyDamage(match?.Arena)
                                    : mayhemBuilds.EstimateEnemyDamage(match?.Mayhem);
                                hideStartingItems = mayhemBuilds.HasStartingPurchase(match?.Mayhem, suggestion.Data);
                            }
                            build = new BuildDisplay(window.Bounds, window.Handle, ChampionName(currentGame),
                                currentGame.Mode, suggestion, shop, plan, ClientPatch: ratings.Catalog.Patch,
                                ArenaRivalsDetected: match?.Arena?.Opponents.Length > 0, EnemyDamage: enemyDamage,
                                HideStartingItems: hideStartingItems);
                        }
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
                await Task.Delay(capture is null ? 240 : 115, cancellation).ConfigureAwait(false);
            }
        }
        finally { capture?.Dispose(); }

        void StopCapture()
        {
            capture?.Dispose();
            capture = null;
            previous = null;
            detector.Clear();
            lastFrame = 0;
            lastOffer = 0;
            nextShopScan = 0;
            cardsVisible = false;
            prismaticDisplay = null;
            shop = null;
        }

        OverlayUpdate? ReadOverlay(GameWindow window, GameContext current, IReadOnlyList<int>? arenaItems)
        {
            if (!string.Equals(window.Patch, ratings.Catalog.Patch, StringComparison.Ordinal))
            {
                StopCapture();
                return new OverlayUpdate($"Datos para {ratings.Catalog.Patch}. Cliente: {window.Patch ?? "parche desconocido"}.");
            }
            if (capture is not null && (window.Handle != capture.Handle ||
                capture.Width != window.Frame.Width || capture.Height != window.Frame.Height))
                StopCapture();

            var label = current.Mode == Mode.Caos ? "ARAM Caos" : "Arena";
            var name = ChampionName(current);
            var source = opgg.Current(current, cancellation);
            var mayhem = aramMayhem.Current(current.Mode, cancellation);
            var arena = ggez.Current(current, cancellation);
            var extraStatus = mayhem?.Status ?? arena?.Status;
            var sources = extraStatus is null ? source.Status : $"{source.Status} · {extraStatus}";
            idle = $"{label} · {name} · Esperando cartas… · {sources}";
            var candidates = layouts.Find(current.Mode, window.Bounds.Width, window.Bounds.Height);

            try
            {
                capture ??= new WindowCapture(window.Handle);
                var scan = new Int32Rect(0, 0, window.Bounds.Width, window.Bounds.Height);
                var frame = capture.Read(new Int32Rect(window.Bounds.X - window.Frame.X,
                    window.Bounds.Y - window.Frame.Y, scan.Width, scan.Height));
                if (frame is null)
                {
                    if (Environment.TickCount64 - lastFrame <= 600)
                        return null;
                    shop = null;
                    nextShopScan = 0;
                    return new OverlayUpdate($"{label} · Esperando imagen de League…");
                }
                var now = Environment.TickCount64;
                lastFrame = now;
                if (now >= nextShopScan)
                {
                    shop = shopDetector.Read(frame, detector, shop);
                    buildVisibility.ObserveShop(shop.HasValue);
                    nextShopScan = Environment.TickCount64 + (shop is null ? 400 : 180);
                }
                if (candidates.Length == 0)
                    return new OverlayUpdate($"{label} · Calibra un panel de {window.Bounds.Width} × {window.Bounds.Height}.");
                string status = idle;
                var read = detector.ReadPanel(frame, scan, candidates, current.Mode, previous);
                if (read is { Layout: var candidate, Result: var result })
                {
                    var shards = ratings.Shards.ForOffer(current, result);
                    var prismatics = shards is null ? ratings.Prismatics.ForOffer(current, result) : null;
                    if ((shards ?? prismatics?.Badges ?? ratings.Legendaries.ForOffer(current, result)) is { } choices)
                    {
                        if (Native.GetForegroundWindow() != window.Handle || Paused)
                            return new OverlayUpdate("Esperando la ventana de League…");
                        previous = candidate;
                        lastOffer = Environment.TickCount64;
                        history.ObserveClosed(window.Handle, lastOffer);
                        var offers = shards is not null ? "Fragmentos" : prismatics is not null ? "Prismáticos" : "Legendarios";
                        var tiers = string.Join(" | ", choices.Select(badge => badge?.LocalText ?? "?"));
                        return new OverlayUpdate($"{label} · {name} · {offers} · Tier local · {tiers}",
                            window.Bounds, candidate, choices, window.Handle,
                            prismatics is null ? null : new(prismatics.Top, candidate, ratings.Catalog.Patch));
                    }
                    if (result.CanShow(candidate == previous))
                    {
                        previous = candidate;
                        var badges = CardComparison.ForOffer(ratings, current, result.Cards, result.Items,
                            source.Data, source.Loading, mayhem?.Data, arena?.Data,
                            upgradeLevels: result.UpgradeLevels, arenaItems: arenaItems);
                        if (Native.GetForegroundWindow() != window.Handle || Paused)
                            return new OverlayUpdate("Esperando la ventana de League…");
                        lastOffer = Environment.TickCount64;
                        history.Observe(window.Handle, current.Mode, name, result.Cards, badges, lastOffer);
                        var displayed = string.Join(" | ", badges.Select(badge => badge is null ? "—" :
                            badge == CardComparison.Unread ? "?" : $"{badge.ExternalText} / {badge.LocalText}" +
                            (badge.RecommendedUpgrade ? " · Mejora recomendada" :
                                badge.RecommendedAugment ? " · Recomendado" : "")));
                        return new OverlayUpdate($"{label} · {name} · {sources} · OP.GG / local · {displayed}",
                            window.Bounds, candidate, badges, window.Handle);
                    }
                    status = result.Items.Any(item => item)
                        ? $"{label} · Panel de objetos"
                        : $"{label} · Panel abierto · {result.Cards.Count(id => id.HasValue)} aumentos reconocidos";
                }
                else if (current.Mode != Mode.Arena && previous is not null && Environment.TickCount64 - lastOffer < 300)
                    return null;
                previous = null;
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

    private string ChampionName(GameContext game) => ratings.Catalog.Champions.FirstOrDefault(item =>
        string.Equals(item.Key, game.Champion, StringComparison.OrdinalIgnoreCase))?.Name ?? game.Champion;
}

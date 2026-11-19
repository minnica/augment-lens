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
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
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
        var mayhemBuilds = MayhemBuilds.Load(ratings.Catalog, assets);
        var arenaBuilds = new ArenaBuilds(ratings);
        WindowCapture? capture = null;
        GameContext? game = null;
        LiveMatch? match = null;
        (LiveMatch? Match, OpGgBuildSnapshot? Source) planInput = default;
        MayhemBuildPlan? plan = null;
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
                    if (window.Handle != lastWindow?.Handle)
                        buildVisibility.ObserveShop(open: false);
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
                        LiveMatch? current;
                        try { current = await live.ReadMatch(cancellation).ConfigureAwait(false); }
                        catch (Exception error) when (error is HttpRequestException or JsonException or KeyNotFoundException
                            or InvalidOperationException or TaskCanceledException)
                        {
                            current = null;
                        }
                        if (game != current?.Game)
                            StopCapture();
                        game = current?.Game;
                        match = current;
                        nextPoll = Environment.TickCount64 + 1000;
                    }
                    result = game is null
                        ? new OverlayUpdate("Esperando ARAM Caos o Arena…")
                        : ReadOverlay(window, game);
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
                                if (updated is null || plan is null || !updated.Steps.SequenceEqual(plan.Steps))
                                    plan = updated;
                            }
                            build = new BuildDisplay(window.Bounds, window.Handle, ChampionName(currentGame),
                                currentGame.Mode, suggestion, shop, plan, ClientPatch: ratings.Catalog.Patch,
                                ArenaRivalsDetected: match?.Arena?.Opponents.Length > 0);
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

        OverlayUpdate? ReadOverlay(GameWindow window, GameContext current)
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
                            source.Data, source.Loading, mayhem?.Data, arena?.Data, result.UpgradeLevels);
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

internal sealed class Overlay
{
    private readonly BadgeWindow[] badges = Enumerable.Range(1, 4).Select(slot => new BadgeWindow(slot)).ToArray();
    internal IReadOnlyList<Window> Windows => badges;
    internal bool IsVisible => badges.Any(badge => badge.NativeVisible);
    internal void Hide() { foreach (var badge in badges) badge.HideBadge(); }
    internal void Close() { foreach (var badge in badges) badge.Close(); }

    internal void Render(OverlayUpdate update)
    {
        if (update.Bounds is not { } bounds || update.Layout is not { } layout || update.Labels is not { } labels)
        {
            Hide();
            return;
        }

        double scale = update.Target != 0 ? Math.Max(96u, Native.GetDpiForWindow(update.Target)) / 96.0 : 1;
        for (int index = 0; index < badges.Length; index++)
        {
            var badge = badges[index];
            var label = index < labels.Length ? labels[index] : null;
            if (label is null)
            {
                badge.HideBadge();
                continue;
            }
            var title = layout.Titles[index];
            double gap = label.ArenaChoice is null ? 0 : 8 * scale;
            badge.Display(label, bounds.X + (title.X + title.Width / 2) * bounds.Width,
                bounds.Y + title.Y * bounds.Height - gap, scale);
        }
    }

    internal static Brush Paint(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    private sealed class BadgeWindow : Window
    {
        private readonly TextBlock source = new() { FontWeight = FontWeights.Bold, FontSize = 16 };
        private readonly TextBlock local = new() { FontSize = 13, Foreground = Brushes.WhiteSmoke };
        private readonly TextBlock localReason = new()
        {
            FontSize = 12, Foreground = Brushes.WhiteSmoke, MaxWidth = 228, TextWrapping = TextWrapping.Wrap
        };
        private readonly TextBlock popularity = new() { FontSize = 12, Foreground = Brushes.WhiteSmoke };
        private readonly TextBlock popularityDetails = new() { FontSize = 11 };
        private readonly Border badge;
        private CardComparison? displayed;
        private nint handle;
        private Int32Rect placement;
        internal bool NativeVisible => handle != 0 && Native.IsWindowVisible(handle);

        internal BadgeWindow(int slot)
        {
            Title = $"Aumentos LoL · Carta {slot}";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            ShowActivated = false;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
            Background = Brushes.Transparent;
            IsHitTestVisible = false;
            var panel = new StackPanel();
            panel.Children.Add(source);
            panel.Children.Add(localReason);
            panel.Children.Add(local);
            panel.Children.Add(popularity);
            panel.Children.Add(popularityDetails);
            badge = new Border
            {
                Padding = new Thickness(10, 3, 10, 3), CornerRadius = new CornerRadius(6),
                Background = Paint("#F0172436"), BorderThickness = new Thickness(1), Child = panel
            };
            Content = badge;
            SourceInitialized += (_, _) =>
            {
                handle = new WindowInteropHelper(this).Handle;
                const int exStyle = -20;
                Native.SetWindowLongW(handle, exStyle, Native.GetWindowLongW(handle, exStyle) | 0x080000A0);
            };
        }

        internal void HideBadge()
        {
            if (IsVisible)
                Hide();
        }

        internal void Display(CardComparison value, double center, double titleY, double dpiScale)
        {
            if (displayed != value)
            {
                displayed = value;
                bool unread = value == CardComparison.Unread;
                string? tier = value.External?.Tier;
                if (value.ArenaChoice is { } choice)
                {
                    source.Text = $"Tier local   {value.LocalText}{choice.TopLabel}{(choice.Recommended ? " · Recomendado" : "")}";
                    local.Text = choice.Reason;
                    tier = choice.Tier.ToString();
                }
                else if (value.IsArenaAugment && value.Local != CardComparison.Missing)
                {
                    source.Text = value.Local == "Variable" ? "Valoración variable" : $"Tier local   {value.LocalText}" +
                        (value.RecommendedUpgrade ? " · Mejora recomendada" :
                            value.RecommendedAugment ? " · Recomendado" : "");
                    local.Text = value.External is not null || value.Loading ? $"OP.GG   {value.ExternalText}" : "";
                    tier = value.Local;
                }
                else
                {
                    source.Text = unread ? "?" : $"OP.GG   {value.ExternalText}";
                    local.Text = unread ? "" : $"Estimación local   {value.Local}";
                }
                localReason.Text = value.LocalReason ?? "";
                localReason.Visibility = localReason.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
                local.Visibility = local.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
                popularity.Text = value.Mayhem?.Text ?? value.Arena?.Text ?? "";
                popularity.Visibility = popularity.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
                popularityDetails.Text = value.Mayhem is { IsPrevious: true } old ?
                    $"Parche anterior {old.Patch}" : value.Arena?.Details ?? "";
                popularityDetails.Visibility = popularityDetails.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
                popularityDetails.Foreground = value.Mayhem?.IsPrevious == true || value.Arena?.IsPrevious == true
                    ? Paint("#FFD76D") : Brushes.WhiteSmoke;
                badge.BorderBrush = source.Foreground = tier switch
                {
                    "S" => Paint("#FFD76D"), "A" => Paint("#86EAC5"), "B" => Paint("#B6CAFF"), _ => Paint("#CFD7E3")
                };
            }
            badge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Width = badge.DesiredSize.Width;
            Height = badge.DesiredSize.Height;
            dpiScale = Math.Max(dpiScale, VisualTreeHelper.GetDpi(this).DpiScaleX);
            int width = (int)Math.Ceiling(Width * dpiScale);
            int height = (int)Math.Ceiling(Height * dpiScale);
            var target = new Int32Rect((int)Math.Round(center - width / 2.0), (int)Math.Round(titleY) - height - 4, width, height);
            if (IsVisible && NativeVisible && target == placement)
                return;
            if (!IsVisible)
                Show();
            const uint showWithoutActivation = 0x0040 | 0x0010;
            Native.SetWindowPos(handle, -1, target.X, target.Y, target.Width, target.Height, showWithoutActivation);
            placement = target;
        }
    }
}

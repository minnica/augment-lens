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
    CardComparison?[]? Labels = null, nint Target = 0);

internal sealed class LiveOverlay
{
    private readonly Ratings ratings;
    private readonly LayoutStore layouts;
    private readonly CardHistory history;
    private readonly OpGgRatings opgg;
    private readonly AramMayhemRatings aramMayhem;
    private readonly GgezArenaRatings ggez;
    private readonly string assets;
    private readonly Dispatcher dispatcher;
    private readonly Action<OverlayUpdate> update;
    private int paused;

    public LiveOverlay(Ratings ratings, LayoutStore layouts, CardHistory history, OpGgRatings opgg,
        AramMayhemRatings aramMayhem, GgezArenaRatings ggez, string assets,
        Dispatcher dispatcher, Action<OverlayUpdate> update)
    {
        this.ratings = ratings;
        this.layouts = layouts;
        this.history = history;
        this.opgg = opgg;
        this.aramMayhem = aramMayhem;
        this.ggez = ggez;
        this.assets = assets;
        this.dispatcher = dispatcher;
        this.update = update;
    }

    public bool Paused { get => Volatile.Read(ref paused) != 0; set => Volatile.Write(ref paused, value ? 1 : 0); }

    public async Task Run(CancellationToken cancellation)
    {
        using var live = new LiveGame(Path.Combine(assets, "riotgames.pem"));
        using var detector = new Detection(ratings, Path.Combine(assets, "tessdata"));
        WindowCapture? capture = null;
        GameContext? game = null;
        long nextPoll = 0;
        long lastFrame = 0;
        long lastOffer = 0;
        string idle = "Esperando la partida…";
        Calibration? previous = null;
        GameWindow? lastWindow = null;
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                var window = Paused ? null : GameWindow.Foreground(lastWindow);
                if (window is not null)
                    lastWindow = window;
                OverlayUpdate? result;
                if (window is null || window.Bounds.Width < 1 || window.Bounds.Height < 1)
                {
                    StopCapture();
                    game = null;
                    nextPoll = 0;
                    result = new OverlayUpdate(Paused ? "Pausado" : "Esperando la ventana de League…");
                }
                else
                {
                    var now = Environment.TickCount64;
                    if (now >= nextPoll)
                    {
                        GameContext? current;
                        try { current = await live.Read(cancellation).ConfigureAwait(false); }
                        catch (Exception error) when (error is HttpRequestException or JsonException or KeyNotFoundException
                            or InvalidOperationException or TaskCanceledException)
                        {
                            current = null;
                        }
                        if (game != current)
                            StopCapture();
                        game = current;
                        nextPoll = Environment.TickCount64 + 1000;
                    }
                    result = game is null
                        ? new OverlayUpdate("Esperando ARAM Caos o Arena…")
                        : ReadOverlay(window, game);
                }

                history.WriteIfDue(Environment.TickCount64);
                if (result is not null)
                    await dispatcher.InvokeAsync(() => update(result), DispatcherPriority.Normal, cancellation);
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
            var name = ratings.Catalog.Champions.FirstOrDefault(item =>
                string.Equals(item.Key, current.Champion, StringComparison.OrdinalIgnoreCase))?.Name ?? current.Champion;
            var source = opgg.Current(current, cancellation);
            var mayhem = aramMayhem.Current(current.Mode, cancellation);
            var arena = ggez.Current(current, cancellation);
            var extraStatus = mayhem?.Status ?? arena?.Status;
            var sources = extraStatus is null ? source.Status : $"{source.Status} · {extraStatus}";
            idle = $"{label} · {name} · Esperando cartas… · {sources}";
            var candidates = layouts.Find(current.Mode, window.Bounds.Width, window.Bounds.Height);
            if (candidates.Length == 0)
            {
                StopCapture();
                return new OverlayUpdate($"{label} · Calibra un panel de {window.Bounds.Width} × {window.Bounds.Height}.");
            }

            try
            {
                capture ??= new WindowCapture(window.Handle);
                var scan = new Int32Rect(0, 0, window.Bounds.Width, window.Bounds.Height);
                var frame = capture.Read(new Int32Rect(window.Bounds.X - window.Frame.X,
                    window.Bounds.Y - window.Frame.Y, scan.Width, scan.Height));
                if (frame is null)
                    return Environment.TickCount64 - lastFrame > 600 ? new OverlayUpdate($"{label} · Esperando imagen de League…") : null;
                lastFrame = Environment.TickCount64;
                string status = idle;
                var read = detector.ReadPanel(frame, scan, candidates, current.Mode, previous);
                if (read is { Layout: var candidate, Result: var result })
                {
                    if (result.CanShow(candidate == previous))
                    {
                        previous = candidate;
                        var badges = CardComparison.ForOffer(ratings, current, result.Cards, result.Items,
                            source.Data, source.Loading, mayhem?.Data, arena?.Data);
                        if (Native.GetForegroundWindow() != window.Handle || Paused)
                            return new OverlayUpdate("Esperando la ventana de League…");
                        lastOffer = Environment.TickCount64;
                        history.Observe(window.Handle, current.Mode, name, result.Cards, badges, lastOffer);
                        var displayed = string.Join(" | ", badges.Select(badge => badge is null ? "—" :
                            badge == CardComparison.Unread ? "?" : $"{badge.ExternalText} / {badge.Local}"));
                        return new OverlayUpdate($"{label} · {name} · {sources} · OP.GG / local · {displayed}",
                            window.Bounds, candidate, badges, window.Handle);
                    }
                    status = result.Items.Any(item => item)
                        ? $"{label} · Panel de objetos"
                        : $"{label} · Panel abierto · {result.Cards.Count(id => id.HasValue)} aumentos reconocidos";
                }
                else if (previous is not null && Environment.TickCount64 - lastOffer < 300)
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
            badge.Display(label, bounds.X + (title.X + title.Width / 2) * bounds.Width,
                bounds.Y + title.Y * bounds.Height, scale);
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
                source.Text = unread ? "?" : $"OP.GG   {value.ExternalText}";
                local.Text = $"Estimación local   {value.Local}";
                local.Visibility = unread ? Visibility.Collapsed : Visibility.Visible;
                popularity.Text = value.Mayhem?.Text ?? value.Arena?.Text ?? "";
                popularity.Visibility = popularity.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
                popularityDetails.Text = value.Mayhem is { IsPrevious: true } old ?
                    $"Parche anterior {old.Patch}" : value.Arena?.Details ?? "";
                popularityDetails.Visibility = popularityDetails.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
                popularityDetails.Foreground = value.Mayhem?.IsPrevious == true || value.Arena?.IsPrevious == true
                    ? Paint("#FFD76D") : Brushes.WhiteSmoke;
                badge.BorderBrush = source.Foreground = value.External?.Tier switch
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

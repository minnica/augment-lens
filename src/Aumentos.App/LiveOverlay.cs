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

internal record OverlayUpdate(string Status, Int32Rect? Bounds = null, Calibration? Layout = null, string?[]? Labels = null);

internal sealed class LiveOverlay
{
    private readonly Ratings ratings;
    private readonly LayoutStore layouts;
    private readonly string assets;
    private readonly Dispatcher dispatcher;
    private readonly Action<OverlayUpdate> update;
    private int paused;

    public LiveOverlay(Ratings ratings, LayoutStore layouts, string assets, Dispatcher dispatcher, Action<OverlayUpdate> update)
    {
        this.ratings = ratings;
        this.layouts = layouts;
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
            idle = $"{label} · {name} · Esperando cartas…";
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
                        var badges = result.Cards.Select((id, slot) => result.Items[slot] ? null : id is null ? "?" :
                            ratings.Rate(current.Mode, current.Champion, id.Value)?.ToString() ?? "Sin tier").ToArray();
                        if (Native.GetForegroundWindow() != window.Handle || Paused)
                            return new OverlayUpdate("Esperando la ventana de League…");
                        lastOffer = Environment.TickCount64;
                        return new OverlayUpdate($"{label} · {name} · Tiers {string.Join(" / ", badges)}", window.Bounds, candidate, badges);
                    }
                    status = result.Items.Any(item => item)
                        ? $"{label} · Panel de objetos"
                        : $"{label} · Panel abierto · {result.Cards.Count(id => id.HasValue)} aumentos reconocidos";
                }
                else if (previous is not null && Environment.TickCount64 - lastOffer < 300)
                    return null;
                previous = null;
                detector.Clear();
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

internal sealed class Overlay : Window
{
    private readonly Canvas surface = new();
    private readonly Border[] badges;
    private nint handle;

    public Overlay()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        ShowActivated = false;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        Background = Brushes.Transparent;
        IsHitTestVisible = false;
        Content = surface;
        badges = Enumerable.Range(0, 4).Select(_ =>
        {
            var badge = new Border
            {
                Padding = new Thickness(10, 3, 10, 3), CornerRadius = new CornerRadius(8),
                Background = Paint("#F0172436"), BorderThickness = new Thickness(1),
                Child = new TextBlock { FontWeight = FontWeights.Bold, FontSize = 18 }
            };
            surface.Children.Add(badge);
            return badge;
        }).ToArray();
        SourceInitialized += (_, _) =>
        {
            handle = new WindowInteropHelper(this).Handle;
            const int exStyle = -20;
            Native.SetWindowLongW(handle, exStyle, Native.GetWindowLongW(handle, exStyle) | 0x08000020 | 0x80);
        };
    }

    internal void Render(OverlayUpdate update)
    {
        if (update.Bounds is not { } bounds || update.Layout is not { } layout || update.Labels is not { } labels)
        {
            if (IsVisible) Hide();
            return;
        }

        handle = new WindowInteropHelper(this).EnsureHandle();
        Native.SetWindowPos(handle, -1, bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x0010);
        var dpi = VisualTreeHelper.GetDpi(this);
        for (int index = 0; index < badges.Length; index++)
        {
            var badge = badges[index];
            string? label = index < labels.Length ? labels[index] : null;
            badge.Visibility = label is not null ? Visibility.Visible : Visibility.Collapsed;
            if (label is null)
                continue;
            var text = (TextBlock)badge.Child;
            text.Text = label;
            badge.BorderBrush = text.Foreground = label switch
            {
                "S" => Paint("#FFD76D"), "A" => Paint("#86EAC5"), "B" => Paint("#B6CAFF"), _ => Paint("#CFD7E3")
            };
            badge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var title = layout.Titles[index];
            Canvas.SetLeft(badge, (title.X + title.Width / 2) * bounds.Width / dpi.DpiScaleX - badge.DesiredSize.Width / 2);
            Canvas.SetTop(badge, Math.Max(0, title.Y * bounds.Height / dpi.DpiScaleY - badge.DesiredSize.Height - 3));
        }
        if (!IsVisible)
        {
            Show();
        }
        const uint showWithoutActivation = 0x0040 | 0x0010;
        Native.SetWindowPos(handle, -1, bounds.X, bounds.Y, bounds.Width, bounds.Height, showWithoutActivation);
    }

    internal static Brush Paint(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}

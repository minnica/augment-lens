using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace Aumentos;

internal sealed record MayhemChampionDisplay(Int32Rect Bounds, nint Target,
    MayhemChampionTierView Source, MayhemDraftChoice[] Choices, MayhemBenchChoice[]? Bench = null)
{
    internal int Top => Bench is not null ? 16 : Choices.Select(choice => choice.Bottom).DefaultIfEmpty(752).Max() + 1;
}

internal sealed class MayhemChampionOverlay : Window
{
    private const int LeftEdge = 510;
    private const int WidthInClient = 980;
    private const int HeightInClient = 124;
    private readonly Canvas badges = new() { Width = WidthInClient, Height = HeightInClient };
    private MayhemChampionDisplay? displayed;
    private Int32Rect placement;
    private nint handle;

    internal MayhemChampionOverlay()
    {
        Title = "Aumentos LoL · Campeones de ARAM Caos";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        ShowActivated = false;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        Background = Brushes.Transparent;
        Focusable = false;
        IsHitTestVisible = false;
        Content = new Viewbox { Child = badges, Stretch = Stretch.Fill };
        SourceInitialized += (_, _) =>
        {
            handle = new WindowInteropHelper(this).Handle;
            const int extendedStyle = -20;
            Native.SetWindowLongW(handle, extendedStyle, Native.GetWindowLongW(handle, extendedStyle) | 0x080000A0);
        };
    }

    internal void Render(MayhemChampionDisplay? view)
    {
        if (view is null)
        {
            if (IsVisible) Hide();
            displayed = null;
            return;
        }

        if (displayed is null || displayed.Source != view.Source || displayed.Bounds.Width != view.Bounds.Width ||
            !displayed.Choices.SequenceEqual(view.Choices) || (displayed.Bench is null) != (view.Bench is null) ||
            !(displayed.Bench ?? []).SequenceEqual(view.Bench ?? []))
            Draw(view);
        displayed = view;

        var bounds = view.Bounds;
        int height = view.Bench is null ? HeightInClient : 82;
        var relative = MayhemDraftReader.Scaled(LeftEdge, view.Top, WidthInClient, height, bounds.Width, bounds.Height);
        var position = new Int32Rect(bounds.X + relative.X, bounds.Y + relative.Y, relative.Width, relative.Height);
        if (IsVisible && Native.IsWindowVisible(handle) && position == placement)
            return;

        var dpi = Math.Max(96u, Native.GetDpiForWindow(view.Target)) / 96.0;
        Width = position.Width / dpi;
        Height = position.Height / dpi;
        if (!IsVisible) Show();
        if (Native.SetWindowPos(handle, -1, position.X, position.Y, position.Width, position.Height, 0x0040 | 0x0010))
            placement = position;
    }

    private void Draw(MayhemChampionDisplay view)
    {
        badges.Children.Clear();
        badges.Height = view.Bench is null ? HeightInClient : 82;
        double scale = Math.Clamp((double)MayhemDraftReader.ReferenceWidth / view.Bounds.Width, 1, 1.35);
        if (view.Bench is { } bench && view.Source.Data is { } benchData)
        {
            foreach (var choice in bench)
            {
                if (benchData.Letter(choice.Champion.Key) is not { } letter)
                    continue;
                var badge = MayhemTierBadge.Create(letter, choice.Champion.Name, 31 * scale, 29 * scale, 21 * scale);
                var right = MayhemBenchPortraits.PortraitLeft(choice.Slot) + MayhemBenchPortraits.PortraitSize - 4;
                Canvas.SetLeft(badge, right - LeftEdge - badge.Width);
                Canvas.SetTop(badge, MayhemBenchPortraits.PortraitSize - 3 - badge.Height);
                badges.Children.Add(badge);
            }
            return;
        }
        if (view.Source.Data is { } data)
        {
            foreach (var choice in view.Choices)
            {
                var badge = MayhemTierBadge.Create(data.Letter(choice.Champion.Key), choice.Champion.Name,
                    58 * scale, 46 * scale, 32 * scale);
                Canvas.SetLeft(badge, choice.Center - LeftEdge - badge.Width / 2);
                Canvas.SetTop(badge, 7);
                badges.Children.Add(badge);
            }
        }

        var caption = new Border
        {
            Background = OverlayPanelStyle.Surface, BorderBrush = OverlayPanelStyle.Frame, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(12 * scale, 3 * scale, 12 * scale, 4 * scale),
            Child = new TextBlock { Text = view.Source.Status, FontSize = 19 * scale, Foreground = OverlayPanelStyle.Muted }
        };
        caption.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(caption, (WidthInClient - caption.DesiredSize.Width) / 2);
        Canvas.SetTop(caption, view.Source.Data is null ? 14 : 14 + 46 * scale);
        badges.Children.Add(caption);
    }
}

internal static class MayhemTierBadge
{
    internal static Border Create(string? tier, string champion, double width, double height, double size)
    {
        var badge = new Border
        {
            Width = width, Height = height, BorderThickness = new Thickness(1.5),
            BorderBrush = OverlayPanelStyle.Frame, CornerRadius = new CornerRadius(2), Background = OverlayPanelStyle.Surface,
            Child = new TextBlock
            {
                Text = tier ?? "—", Foreground = OverlayPanelStyle.Tier(tier), FontSize = size, FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            }
        };
        AutomationProperties.SetName(badge, $"{champion} · {(tier is null ? "Sin tier" : $"Tier {tier}")} de OP.GG");
        return badge;
    }
}

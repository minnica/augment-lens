using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Aumentos;

internal sealed class BuildItemCard : Window
{
    private const double CardWidth = 226;
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-MX");
    private static readonly Brush CardSurface = Overlay.Paint("#101D25");
    private nint handle;

    internal BuildItemCard()
    {
        Title = "Detalle del objeto";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        ShowActivated = false;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        Focusable = false;
        IsHitTestVisible = false;
        Background = Brushes.Transparent;
        UseLayoutRounding = true;
        SourceInitialized += (_, _) =>
        {
            handle = new WindowInteropHelper(this).Handle;
            const int extendedStyle = -20, clickThroughToolWindow = 0x080000A0;
            Native.SetWindowLongW(handle, extendedStyle, Native.GetWindowLongW(handle, extendedStyle) | clickThroughToolWindow);
        };
    }

    internal void Present(OpGgBuildItem item, Border icon, ShopItemDetails? details, Rect row, Int32Rect panel, Int32Rect bounds, double scale)
    {
        var card = Create(item, icon, details);
        card.Measure(new Size(CardWidth, double.PositiveInfinity));
        scale = Math.Min(scale, Math.Min(bounds.Width / card.DesiredSize.Width, bounds.Height / card.DesiredSize.Height));
        int width = Math.Max(1, (int)Math.Floor(card.DesiredSize.Width * scale));
        int height = Math.Max(1, (int)Math.Floor(card.DesiredSize.Height * scale));
        int gap = Math.Max(4, (int)Math.Round(8 * scale));
        int leftSpace = panel.X - bounds.X, rightSpace = bounds.X + bounds.Width - panel.X - panel.Width;
        int x = rightSpace >= width + gap || rightSpace >= leftSpace ? panel.X + panel.Width + gap : panel.X - width - gap;
        int y = (int)Math.Round(row.Top);
        if (Math.Max(leftSpace, rightSpace) < width + gap)
        {
            x = (int)Math.Round(row.Right) - width;
            y = row.Bottom + height + gap <= bounds.Y + bounds.Height
                ? (int)Math.Ceiling(row.Bottom) + gap : (int)Math.Floor(row.Top) - height - gap;
        }
        x = Math.Clamp(x, bounds.X, bounds.X + bounds.Width - width);
        y = Math.Clamp(y, bounds.Y, bounds.Y + bounds.Height - height);

        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        double dpi = Math.Max(1, Native.GetDpiForWindow(hwnd) / 96.0);
        Width = width / dpi;
        Height = height / dpi;
        Content = new Viewbox { Child = card };
        if (!IsVisible)
            Show();
        Native.SetWindowPos(handle, -1, x, y, width, height, 0x0040 | 0x0010);
    }

    private static Border Create(OpGgBuildItem item, Border icon, ShopItemDetails? details)
    {
        var stack = new StackPanel();
        var name = Text(item.Name, OverlayPanelStyle.TitleSize, OverlayPanelStyle.BrightGold);
        name.FontFamily = OverlayPanelStyle.Heading;
        name.FontWeight = FontWeights.Bold;
        name.TextAlignment = TextAlignment.Center;
        name.Margin = new Thickness(0, 0, 0, 11);
        stack.Children.Add(name);

        var facts = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        icon.VerticalAlignment = VerticalAlignment.Center;
        facts.Children.Add(icon);
        if (details?.Cost is not null || details?.Category is not null)
        {
            var price = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, MaxWidth = 108 };
            if (details?.Cost is { } cost)
            {
                var label = Text($"{cost.ToString("N0", Spanish)} de oro", OverlayPanelStyle.BodySize, OverlayPanelStyle.BrightGold);
                label.FontWeight = FontWeights.Bold;
                price.Children.Add(label);
                price.Children.Add(Text("Costo total", OverlayPanelStyle.DetailSize, OverlayPanelStyle.Muted));
            }
            if (details?.Category is { } category)
            {
                price.Children.Add(new Border
                {
                    Child = Text(category, OverlayPanelStyle.DetailSize, OverlayPanelStyle.Ink),
                    BorderBrush = OverlayPanelStyle.Divider, BorderThickness = new Thickness(1),
                    Margin = new Thickness(0, 5, 0, 0), Padding = new Thickness(5, 2, 5, 2)
                });
            }
            facts.Children.Add(price);
        }
        stack.Children.Add(facts);

        if (!string.IsNullOrEmpty(details?.Stats) || !string.IsNullOrEmpty(details?.Effect))
            stack.Children.Add(new Rectangle
                { Height = 1, Fill = OverlayPanelStyle.Divider, Margin = new Thickness(0, 11, 0, 8) });
        if (!string.IsNullOrEmpty(details?.Stats))
            stack.Children.Add(Text(details.Stats, 12, OverlayPanelStyle.Accent));
        if (!string.IsNullOrEmpty(details?.Effect))
        {
            var effect = Text(details.Effect, 12, OverlayPanelStyle.Ink);
            effect.Margin = new Thickness(0, details.Stats is null ? 0 : 7, 0, 0);
            effect.LineHeight = 15;
            effect.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            effect.MaxHeight = 120;
            effect.TextTrimming = TextTrimming.WordEllipsis;
            stack.Children.Add(effect);
        }

        var frame = new Border { Width = CardWidth, Child = stack, Padding = new Thickness(13) };
        OverlayPanelStyle.ApplyFrame(frame);
        frame.Background = CardSurface;
        return frame;
    }

    private static TextBlock Text(string value, double size, Brush ink) =>
        new() { Text = value, FontSize = size, Foreground = ink, TextWrapping = TextWrapping.Wrap };
}

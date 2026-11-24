using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace Aumentos;

internal sealed record ArenaVoteLabel(Int32Rect Row, string? Tier, string Reason, bool Recommended = false);
internal sealed record ArenaVoteDisplay(Int32Rect Bounds, nint Target, ArenaVoteLabel[] Labels);

internal sealed class ArenaVoteOverlay : Window
{
    private static readonly Brush S = Overlay.Paint("#EDC675");
    private static readonly Brush A = Overlay.Paint("#9ED5BF");
    private static readonly Brush B = Overlay.Paint("#B7CCE3");
    private readonly Canvas layer = new() { ClipToBounds = true };
    private ArenaVoteDisplay? displayed;
    private double displayedDpi;
    private nint handle;

    internal ArenaVoteOverlay()
    {
        Title = "Aumentos LoL · Votación de Arena";
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
        TextOptions.SetTextFormattingMode(layer, TextFormattingMode.Display);
        Content = layer;

        SourceInitialized += (_, _) =>
        {
            handle = new WindowInteropHelper(this).Handle;
            const int extendedStyle = -20;
            const int noActivateToolWindow = 0x080000A0;
            Native.SetWindowLongW(handle, extendedStyle, Native.GetWindowLongW(handle, extendedStyle) | noActivateToolWindow);
        };
    }

    internal void Render(ArenaVoteDisplay? view)
    {
        if (view is null || view.Labels.Length == 0 || view.Bounds.Width <= 0 || view.Bounds.Height <= 0)
        {
            Clear();
            return;
        }

        if (handle == 0)
            _ = new WindowInteropHelper(this).EnsureHandle();
        // Vote rows are in screen pixels. WPF lays out the badges at the overlay's DPI.
        double dpi = VisualTreeHelper.GetDpi(layer).DpiScaleX;
        var bounds = view.Bounds;
        if (IsVisible && Native.IsWindowVisible(handle) && displayedDpi == dpi && displayed is { } previous &&
            previous.Bounds == bounds && previous.Target == view.Target && previous.Labels.SequenceEqual(view.Labels))
            return;

        var rows = view.Labels.Where(label =>
            label.Row.X >= 0 && label.Row.Y >= 0 && label.Row.Width > 0 && label.Row.Height / dpi >= 18 &&
            label.Row.Width <= bounds.Width - label.Row.X && label.Row.Height <= bounds.Height - label.Row.Y).ToArray();
        if (rows.Length == 0)
        {
            Clear();
            return;
        }

        int gap = (int)Math.Ceiling(6 * dpi);
        int left = rows.Max(label => label.Row.X + label.Row.Width) + gap;
        int width = Math.Min((int)Math.Round(225 * dpi), (int)Math.Floor(bounds.Width * .15));
        width = Math.Min(width, bounds.Width - left - gap);
        if (width / dpi < 38)
        {
            Clear();
            return;
        }

        var positions = rows.Select(label =>
        {
            var row = label.Row;
            bool compact = row.Height / dpi < 44 || width / dpi < 156;
            int height = Math.Min(row.Height, (int)Math.Floor((compact ? 32 : 44) * dpi));
            // The option title occupies the lower part of the game's row.
            int center = row.Y + row.Height * 2 / 3;
            int top = Math.Clamp(center - height / 2, row.Y, row.Y + row.Height - height);
            return (Label: label, Top: top, Height: height, Compact: compact);
        }).ToArray();

        int firstTop = positions.Min(position => position.Top);
        int lastBottom = positions.Max(position => position.Top + position.Height);
        int contentHeight = lastBottom - firstTop;
        layer.Children.Clear();
        foreach (var position in positions)
        {
            var badge = Badge(position.Label, width / dpi, position.Height / dpi, position.Compact);
            Canvas.SetTop(badge, (position.Top - firstTop) / dpi);
            layer.Children.Add(badge);
        }

        Width = width / dpi;
        Height = contentHeight / dpi;
        if (!IsVisible)
            Show();
        const uint showWithoutActivation = 0x0040 | 0x0010;
        if (Native.SetWindowPos(handle, -1, bounds.X + left, bounds.Y + firstTop, width, contentHeight, showWithoutActivation))
        {
            displayed = view;
            displayedDpi = dpi;
        }
    }

    private void Clear()
    {
        displayed = null;
        if (IsVisible)
            Hide();
    }

    private static Border Badge(ArenaVoteLabel label, double width, double height, bool compact)
    {
        bool recommended = label.Recommended && label.Tier is not null;
        var tier = new TextBlock
        {
            Text = label.Tier ?? "—",
            FontSize = compact ? 17 : 20,
            FontWeight = FontWeights.SemiBold,
            Foreground = label.Tier switch { "S" or "S+" => S, "A" => A, "B" => B, null => OverlayPanelStyle.Muted, _ => OverlayPanelStyle.Ink },
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        UIElement content = tier;
        if (width >= 104 && height >= 28 && (recommended || !string.IsNullOrWhiteSpace(label.Reason)))
        {
            tier.Width = compact ? 30 : 38;
            var row = new DockPanel();
            DockPanel.SetDock(tier, Dock.Left);
            row.Children.Add(tier);
            var detail = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 5, 0) };
            if (recommended)
                detail.Children.Add(Detail("Recomendado", OverlayPanelStyle.BrightGold, OverlayPanelStyle.DetailSize, lines: 1, semibold: true));
            if (!string.IsNullOrWhiteSpace(label.Reason))
                detail.Children.Add(Detail(label.Reason, OverlayPanelStyle.Ink, compact ? OverlayPanelStyle.DetailSize : 12, lines: compact && recommended ? 1 : 2));
            row.Children.Add(detail);
            content = row;
        }

        var badge = new Border
        {
            Width = width,
            Height = height,
            Child = content
        };
        OverlayPanelStyle.ApplyFrame(badge);
        if (recommended)
        {
            badge.Background = OverlayPanelStyle.SelectedSurface;
            badge.BorderBrush = OverlayPanelStyle.Gold;
        }
        return badge;
    }

    private static TextBlock Detail(string text, Brush foreground, double size, int lines, bool semibold = false) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = semibold ? FontWeights.SemiBold : FontWeights.Normal,
        Foreground = foreground,
        LineHeight = 13,
        LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
        MaxHeight = lines * 13 + 2,
        TextWrapping = lines == 1 ? TextWrapping.NoWrap : TextWrapping.Wrap,
        TextTrimming = TextTrimming.CharacterEllipsis
    };
}

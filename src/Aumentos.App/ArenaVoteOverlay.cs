using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace Aumentos;

internal sealed record ArenaVoteLabel(Int32Rect Row, string? Tier);
internal sealed record ArenaVoteDisplay(Int32Rect Bounds, nint Target, ArenaVoteLabel[] Labels);

internal sealed class ArenaVoteOverlay : Window
{
    private const double BadgeHeight = 44;
    private readonly Canvas layer = new() { ClipToBounds = true };
    private ArenaVoteDisplay? displayed;
    private double displayedDpi;
    private int displayedTierSize;
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

    internal void Render(ArenaVoteDisplay? view, int tierSizePercent = BuildSizePreference.Default)
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
        if (IsVisible && Native.IsWindowVisible(handle) && displayedDpi == dpi && displayedTierSize == tierSizePercent &&
            displayed is { } previous &&
            previous.Bounds == bounds && previous.Target == view.Target && previous.Labels.SequenceEqual(view.Labels))
            return;

        var rows = view.Labels.Where(label =>
            label.Tier is not null && label.Row.X >= 0 && label.Row.Y >= 0 && label.Row.Width > 0 && label.Row.Height / dpi >= 18 &&
            label.Row.Width <= bounds.Width - label.Row.X && label.Row.Height <= bounds.Height - label.Row.Y).ToArray();
        if (rows.Length == 0)
        {
            Clear();
            return;
        }

        int preferredHeight = (int)Math.Round(BadgeHeight * dpi * tierSizePercent / 100);
        var positions = rows.Select(label =>
        {
            var row = label.Row;
            int height = Math.Min(row.Height, preferredHeight);
            int width = Math.Min(bounds.Width, (int)Math.Round(height * TierHexagon.ArtworkAspect));
            int left = Math.Clamp(row.X + row.Width - width / 2, 0, bounds.Width - width);
            // The name tab sits above the bordered title. Center the tier on the title's right edge.
            int titleTop = row.Y + row.Height / 3;
            int top = Math.Clamp(titleTop + (row.Y + row.Height - titleTop - height) / 2, 0, bounds.Height - height);
            return (Label: label, Left: left, Top: top, Width: width, Height: height);
        }).ToArray();

        int firstLeft = positions.Min(position => position.Left);
        int contentWidth = positions.Max(position => position.Left + position.Width) - firstLeft;
        int firstTop = positions.Min(position => position.Top);
        int contentHeight = positions.Max(position => position.Top + position.Height) - firstTop;
        layer.Children.Clear();
        foreach (var position in positions)
        {
            var badge = TierHexagon.Create(position.Label.Tier, position.Width / dpi, position.Height / dpi);
            Canvas.SetLeft(badge, (position.Left - firstLeft) / dpi);
            Canvas.SetTop(badge, (position.Top - firstTop) / dpi);
            layer.Children.Add(badge);
        }

        Width = contentWidth / dpi;
        Height = contentHeight / dpi;
        if (!IsVisible)
            Show();
        const uint showWithoutActivation = 0x0040 | 0x0010;
        if (Native.SetWindowPos(handle, -1, bounds.X + firstLeft, bounds.Y + firstTop, contentWidth, contentHeight, showWithoutActivation))
        {
            displayed = view;
            displayedDpi = dpi;
            displayedTierSize = tierSizePercent;
        }
    }

    private void Clear()
    {
        displayed = null;
        if (IsVisible)
            Hide();
    }
}

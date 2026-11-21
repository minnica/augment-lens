using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace Aumentos;

internal sealed class SpellOverlay : Window
{
    private readonly StackPanel rows = new();
    private readonly Border panel;
    private SpellDisplay? displayed;
    private nint handle;
    private Int32Rect placement;

    internal SpellOverlay()
    {
        Title = "Aumentos LoL · Hechizos de OP.GG";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        ShowActivated = false;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        Background = Brushes.Transparent;
        Focusable = false;
        IsHitTestVisible = false;
        panel = new Border
        {
            Padding = new Thickness(10, 8, 10, 9), CornerRadius = new CornerRadius(3),
            Background = OverlayPanelStyle.Surface, BorderBrush = OverlayPanelStyle.Frame, BorderThickness = new Thickness(2, 3, 2, 2),
            Child = rows
        };
        TextOptions.SetTextFormattingMode(panel, TextFormattingMode.Display);
        Content = new Viewbox { Child = panel, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly };
        SourceInitialized += (_, _) =>
        {
            handle = new WindowInteropHelper(this).Handle;
            const int extendedStyle = -20;
            Native.SetWindowLongW(handle, extendedStyle, Native.GetWindowLongW(handle, extendedStyle) | 0x080000A0);
        };
    }

    internal void Render(SpellDisplay? view)
    {
        if (view is null)
        {
            if (IsVisible) Hide();
            displayed = null;
            return;
        }
        if (displayed is null || displayed.Champion != view.Champion || displayed.Selection != view.Selection ||
            displayed.Source != view.Source || displayed.Tier != view.Tier || displayed.TierCaption != view.TierCaption)
            RenderRows(view);
        displayed = view;

        var bounds = view.Bounds;
        var dpi = Math.Max(96u, Native.GetDpiForWindow(view.Target)) / 96.0;
        var margin = Math.Min((int)Math.Round(14 * dpi), Math.Min(bounds.Width, bounds.Height) / 4);
        int availableWidth = MayhemSidePanel.AvailableWidth(bounds.Width, margin);
        panel.Width = MayhemSidePanel.PreferredWidth(availableWidth / dpi);
        panel.Measure(new Size(panel.Width, double.PositiveInfinity));
        var canvas = panel.DesiredSize;
        var scale = Math.Min(dpi, Math.Min(availableWidth / canvas.Width, (bounds.Height - 2 * margin) / canvas.Height));
        int width = Math.Clamp((int)Math.Floor(canvas.Width * scale), 1, availableWidth);
        int height = Math.Clamp((int)Math.Ceiling(canvas.Height * scale), 1, bounds.Height - 2 * margin);
        int top = Math.Clamp((int)(bounds.Height * 0.75), margin, bounds.Height - margin - height);
        var position = new Int32Rect(bounds.X + bounds.Width - margin - width, bounds.Y + top, width, height);
        if (IsVisible && Native.IsWindowVisible(handle) && position == placement)
            return;
        Width = width / dpi;
        Height = height / dpi;
        if (!IsVisible) Show();
        if (Native.SetWindowPos(handle, -1, position.X, position.Y, position.Width, position.Height, 0x0040 | 0x0010))
            placement = position;
    }

    private void RenderRows(SpellDisplay view)
    {
        rows.Children.Clear();
        if (view.Tier is { } tier && view.TierCaption is { } caption)
        {
            var header = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var badge = MayhemTierBadge.Create(tier, view.Champion, 32, 32, 22);
            badge.Margin = new Thickness(0, 0, 9, 0);
            DockPanel.SetDock(badge, Dock.Left);
            header.Children.Add(badge);
            var champion = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            champion.Children.Add(Line(view.Champion, 15, OverlayPanelStyle.Ink, bold: true));
            champion.Children.Add(Line(caption, 11, OverlayPanelStyle.Muted));
            header.Children.Add(champion);
            rows.Children.Add(header);
            rows.Children.Add(new Border { Height = 1, Background = OverlayPanelStyle.Divider });
            AddHeading("Hechizos");
        }
        else
            AddHeading($"{view.Champion} · Hechizos de OP.GG");
        if (view.Source.Data is not { } data)
        {
            AddLine(view.Source.Status, 12, OverlayPanelStyle.Ink);
            return;
        }
        if (data.Spells is null)
            AddLine("Hechizos sin datos guardados", 12, OverlayPanelStyle.Ink);
        else if (data.Spells is not { Length: > 0 } pairs)
            AddLine("Sin combinación publicada", 12, OverlayPanelStyle.Ink);
        else
        {
            var main = pairs[0];
            AddLine($"{main.First} + {main.Second}", 16, OverlayPanelStyle.Ink, bold: true);
            if (Matches(main, view.Selection))
                AddLine("Ya tienes estos hechizos", 11, OverlayPanelStyle.Tier("A"));
            if (pairs.Length > 1)
                AddLine($"Alternativa · {pairs[1].First} + {pairs[1].Second}", 12, OverlayPanelStyle.Muted);
        }
        var patch = $"{(data.IsPrevious ? "Parche anterior" : "Parche")} {LeaguePatch.Public(data.SourcePatch)}";
        if (view.TierCaption != $"OP.GG · {patch}")
            AddLine(view.Tier is null ? patch : $"Hechizos · {patch}", 11,
                data.IsPrevious ? OverlayPanelStyle.Gold : OverlayPanelStyle.Muted);
    }

    internal static bool Matches(OpGgSpellPair pair, ChampionSelection selection)
    {
        if (selection.FirstSpell is not { } first || selection.SecondSpell is not { } second) return false;
        var a = Ratings.Normalize(pair.First);
        var b = Ratings.Normalize(pair.Second);
        var one = Ratings.Normalize(first);
        var two = Ratings.Normalize(second);
        return (a == one && b == two) || (a == two && b == one);
    }

    private void AddLine(string text, double fontSize, Brush color, bool bold = false) =>
        rows.Children.Add(Line(text, fontSize, color, bold));

    private void AddHeading(string text)
    {
        var heading = Line(text, 15, OverlayPanelStyle.Gold, bold: true);
        heading.FontFamily = OverlayPanelStyle.Heading;
        heading.Margin = new Thickness(0, 4, 0, 0);
        rows.Children.Add(heading);
    }

    private static TextBlock Line(string text, double fontSize, Brush color, bool bold = false) => new()
    {
        Text = text, FontSize = fontSize, Foreground = color, TextWrapping = TextWrapping.Wrap,
        FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, Margin = new Thickness(0, 2, 0, 0)
    };
}

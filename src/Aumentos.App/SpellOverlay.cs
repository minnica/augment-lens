using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace Aumentos;

internal sealed class SpellOverlay : Window
{
    private const double Inset = 12;
    private readonly StackPanel rows = new();
    private readonly Border panel;
    private readonly ChampionSelectPortraits? portraits;
    private SpellDisplay? displayed;
    private nint handle;
    private Int32Rect placement;

    internal SpellOverlay(ChampionSelectPortraits? portraits = null)
    {
        this.portraits = portraits;
        Title = "Aumentos LoL · Hechizos";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        ShowActivated = false;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        Background = Brushes.Transparent;
        Focusable = false;
        IsHitTestVisible = false;
        panel = new Border { Child = rows };
        OverlayPanelStyle.ApplyFrame(panel);
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
        if (displayed is null || displayed.Champion != view.Champion || displayed.Selection.Champion != view.Selection.Champion ||
            displayed.Source != view.Source || displayed.Tier != view.Tier || displayed.TierIsPrevious != view.TierIsPrevious)
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
        var data = view.Source.Data;
        var pairs = data?.Spells;
        bool previousTier = view.Tier is not null && view.TierIsPrevious;
        bool previousSpells = data?.IsPrevious == true && pairs is { Length: > 0 };
        rows.Children.Add(Header(view, previousTier && !previousSpells));

        var spells = new StackPanel { Margin = new Thickness(Inset, 8, Inset, 10) };
        rows.Children.Add(spells);
        if (pairs is not { Length: > 0 })
        {
            string status = data is null ? view.Source.Status :
                pairs is null ? "Hechizos sin datos guardados" : "Sin combinación publicada";
            spells.Children.Add(Line(status, OverlayPanelStyle.DetailSize, OverlayPanelStyle.Muted));
            return;
        }

        spells.Children.Add(SpellPair("Principal", pairs[0], primary: true));
        if (pairs.Length > 1)
            spells.Children.Add(SpellPair("Alternativa", pairs[1], primary: false));
        if (previousSpells)
        {
            var patch = Note(previousTier ? "Datos del parche anterior" : "Hechizos del parche anterior");
            patch.Margin = new Thickness(0, 7, 0, 0);
            spells.Children.Add(patch);
        }
    }

    private Border Header(SpellDisplay view, bool previousTier)
    {
        var header = new DockPanel();
        if (view.Tier is { } tier)
        {
            var badge = MayhemTierStyle.ChampionBadge(tier, view.Champion, 36, 40);
            badge.Margin = new Thickness(6, 0, 0, 0);
            badge.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(badge, Dock.Right);
            header.Children.Add(badge);
        }
        var portrait = ChampionSelectStyle.Portrait(portraits, view.Selection.Champion, 30);
        portrait.Margin = new Thickness(0, 0, 7, 0);
        DockPanel.SetDock(portrait, Dock.Left);
        header.Children.Add(portrait);
        var identity = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        identity.Children.Add(Heading(view.Champion, OverlayPanelStyle.TitleSize, OverlayPanelStyle.Ink));
        if (previousTier)
            identity.Children.Add(Note("Tier del parche anterior"));
        header.Children.Add(identity);
        return new Border
        {
            Background = OverlayPanelStyle.HeaderSurface, BorderBrush = OverlayPanelStyle.Divider,
            BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(Inset, 8, Inset, 8), Child = header
        };
    }

    private static StackPanel SpellPair(string heading, OpGgSpellPair pair, bool primary)
    {
        var group = new StackPanel { Margin = new Thickness(0, primary ? 0 : 8, 0, 0) };
        group.Children.Add(Heading(heading, OverlayPanelStyle.SectionSize, OverlayPanelStyle.Gold));
        group.Children.Add(Line($"{pair.First} + {pair.Second}", OverlayPanelStyle.BodySize, OverlayPanelStyle.Ink, bold: primary));
        return group;
    }

    private static TextBlock Heading(string text, double size, Brush color)
    {
        var heading = Line(text, size, color);
        heading.FontFamily = OverlayPanelStyle.Heading;
        heading.FontWeight = FontWeights.Bold;
        return heading;
    }

    private static TextBlock Note(string text)
    {
        var note = Line(text, OverlayPanelStyle.DetailSize, OverlayPanelStyle.Gold);
        note.Margin = new Thickness(0, 2, 0, 0);
        return note;
    }

    private static TextBlock Line(string text, double fontSize, Brush color, bool bold = false) => new()
    {
        Text = text, FontSize = fontSize, Foreground = color, TextWrapping = TextWrapping.Wrap,
        FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal
    };
}

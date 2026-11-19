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
            Width = 290, Padding = new Thickness(13, 9, 13, 10), CornerRadius = new CornerRadius(7),
            Background = Overlay.Paint("#F2172436"), BorderBrush = Overlay.Paint("#667C9F"), BorderThickness = new Thickness(1),
            Child = rows
        };
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
            displayed.Source != view.Source)
            RenderRows(view);
        displayed = view;

        var bounds = view.Bounds;
        var dpi = Math.Max(96u, Native.GetDpiForWindow(view.Target)) / 96.0;
        var margin = Math.Min((int)Math.Round(14 * dpi), Math.Min(bounds.Width, bounds.Height) / 4);
        panel.Measure(new Size(panel.Width, double.PositiveInfinity));
        var canvas = panel.DesiredSize;
        var scale = Math.Min(dpi, Math.Min((bounds.Width - 2 * margin) / canvas.Width, (bounds.Height - 2 * margin) / canvas.Height));
        int width = Math.Clamp((int)Math.Ceiling(canvas.Width * scale), 1, bounds.Width - 2 * margin);
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
        AddLine($"{view.Champion} · Hechizos de OP.GG", 15, Brushes.WhiteSmoke, bold: true);
        if (view.Source.Data is not { } data)
        {
            AddLine(view.Source.Status, 12, Brushes.WhiteSmoke);
            return;
        }
        if (data.Spells is null)
            AddLine("Hechizos sin datos guardados", 12, Brushes.WhiteSmoke);
        else if (data.Spells is not { Length: > 0 } pairs)
            AddLine("Sin combinación publicada", 12, Brushes.WhiteSmoke);
        else
        {
            var main = pairs[0];
            AddLine($"{main.First} + {main.Second}", 16, Overlay.Paint("#AAD1FF"), bold: true);
            if (Matches(main, view.Selection))
                AddLine("Ya tienes estos hechizos", 11, Overlay.Paint("#86EAC5"));
            if (pairs.Length > 1)
                AddLine($"Alternativa · {pairs[1].First} + {pairs[1].Second}", 12, Brushes.WhiteSmoke);
        }
        AddLine($"{(data.IsPrevious ? "Parche anterior" : "Parche")} {LeaguePatch.Public(data.SourcePatch)}",
            11, data.IsPrevious ? Overlay.Paint("#FFD76D") : Brushes.LightGray);
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
        rows.Children.Add(new TextBlock
        {
            Text = text, FontSize = fontSize, Foreground = color, TextWrapping = TextWrapping.Wrap,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, Margin = new Thickness(0, 2, 0, 0)
        });
}

using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Aumentos;

internal static class TierHexagon
{
    private static readonly Geometry Outline = Geometry.Parse("M 12,1 L 23,7.5 23,19.5 12,26 1,19.5 1,7.5 Z");
    private static readonly (Brush Ink, Brush Plate) Gold = Palette("#F0CE82", "#231B0F");
    private static readonly (Brush Ink, Brush Plate) Green = Palette("#99E6A6", "#0D2019");
    private static readonly (Brush Ink, Brush Plate) Blue = Palette("#92CBF2", "#0B1C28");
    private static readonly (Brush Ink, Brush Plate) Parchment = Palette("#DAD1BF", "#151B23");
    private static readonly (Brush Ink, Brush Plate) Coral = Palette("#F4A096", "#261820");
    private static readonly (Brush Ink, Brush Plate) Muted = Palette("#AFA69B", "#131B23");

    internal static Border Create(string? tier, double width, double height, double fontSize, int? position = null,
        bool centerGlyph = false)
    {
        var (ink, plate) = Colors(tier);
        var badge = new Border { Width = width, Height = height };
        if (tier is not null)
        {
            var pen = new Pen(ink, 1) { LineJoin = PenLineJoin.Round };
            var drawing = new GeometryDrawing(plate, pen, Outline);
            var brush = new DrawingBrush(drawing) { Stretch = Stretch.Fill };
            brush.Freeze();
            badge.Background = brush;
        }
        var letter = new TextBlock
        {
            Text = tier ?? "—", Foreground = ink, FontFamily = OverlayPanelStyle.Body, FontSize = fontSize,
            FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        if (position is { } rank)
        {
            letter.Inlines.Add(new Run($"#{rank}") { FontSize = fontSize * .58, BaselineAlignment = BaselineAlignment.Superscript });
        }
        else if (centerGlyph)
        {
            CenterGlyph(letter, ink);
        }
        badge.Child = letter;
        return badge;
    }

    private static void CenterGlyph(TextBlock letter, Brush ink)
    {
        // Center the visible letter instead of the line box, which includes room for descenders.
        TextOptions.SetTextFormattingMode(letter, TextFormattingMode.Ideal);
        var typeface = new Typeface(letter.FontFamily, letter.FontStyle, letter.FontWeight, letter.FontStretch);
        var text = new FormattedText(letter.Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            typeface, letter.FontSize, ink, VisualTreeHelper.GetDpi(letter).PixelsPerDip);
        var bounds = text.BuildGeometry(new Point()).Bounds;
        if (!bounds.IsEmpty)
            letter.RenderTransform = new TranslateTransform((text.Width - bounds.Width) / 2 - bounds.X,
                (text.Height - bounds.Height) / 2 - bounds.Y);
    }

    private static (Brush Ink, Brush Plate) Colors(string? tier) => tier switch
    {
        "S+" or "S" => Gold,
        "A" => Green,
        "B" => Blue,
        "C" => Parchment,
        "D" or "E" => Coral,
        _ => Muted
    };

    private static (Brush Ink, Brush Plate) Palette(string ink, string plate) => (Overlay.Paint(ink), Overlay.Paint(plate));
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Aumentos;

internal static class OverlayPanelStyle
{
    public const double TitleSize = 16;
    public const double SectionSize = 14;
    public const double BodySize = 13;
    public const double DetailSize = 11;

    // Draw translucency into the brush so the type, frame and icons remain crisp.
    public static readonly Thickness FrameThickness = new(1);
    public static readonly Brush Surface = VerticalGradient("#EC0B2432", "#F004111D");
    public static readonly Brush Frame = VerticalGradient("#8A7650", "#5C482B");
    public static readonly Brush Divider = Overlay.Paint("#625237");
    public static readonly Brush HeaderSurface = Overlay.Paint("#5C102A37");
    public static readonly Brush Inset = Overlay.Paint("#C805131D");
    public static readonly Brush HighlightSurface = Overlay.Paint("#B910333D");
    public static readonly Brush SelectedSurface = Overlay.Paint("#DE292517");
    public static readonly Brush Ink = Overlay.Paint("#F0E6D2");
    public static readonly Brush Muted = Overlay.Paint("#B9AF9A");
    public static readonly Brush Gold = Overlay.Paint("#CDB17B");
    public static readonly Brush BrightGold = Overlay.Paint("#E7CF9D");
    public static readonly Brush Accent = Overlay.Paint("#76D5D2");
    public static readonly FontFamily Heading = new("Beaufort for LOL, Cambria");
    public static readonly FontFamily Body = new("Spiegel, Segoe UI");

    internal static void ApplyFrame(Border panel)
    {
        panel.Background = Surface;
        panel.BorderBrush = Frame;
        panel.BorderThickness = FrameThickness;
        panel.SnapsToDevicePixels = true;
        TextElement.SetFontFamily(panel, Body);
        TextElement.SetFontSize(panel, BodySize);
        TextElement.SetForeground(panel, Ink);
        TextOptions.SetTextFormattingMode(panel, TextFormattingMode.Display);
    }

    private static Brush VerticalGradient(string top, string bottom)
    {
        var brush = new LinearGradientBrush((Color)ColorConverter.ConvertFromString(top),
            (Color)ColorConverter.ConvertFromString(bottom), 90);
        brush.Freeze();
        return brush;
    }
}

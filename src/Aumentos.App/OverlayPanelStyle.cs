using System.Windows.Media;

namespace Aumentos;

internal static class OverlayPanelStyle
{
    internal static readonly Brush Surface = VerticalGradient("#FC102A35", "#FC081923");
    internal static readonly Brush Frame = VerticalGradient("#BDA477", "#8A6A43");
    internal static readonly Brush Divider = Overlay.Paint("#3C514D");
    internal static readonly Brush Ink = Overlay.Paint("#EEE6D3");
    internal static readonly Brush Muted = Overlay.Paint("#B1C1D8");
    internal static readonly Brush Gold = Overlay.Paint("#F6D28C");
    internal static readonly FontFamily Heading = new("Georgia");

    internal static Brush VerticalGradient(string top, string bottom)
    {
        var brush = new LinearGradientBrush((Color)ColorConverter.ConvertFromString(top),
            (Color)ColorConverter.ConvertFromString(bottom), 90);
        brush.Freeze();
        return brush;
    }
}

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Aumentos;

internal static class MayhemTierStyle
{
    internal const double PanelTitleSize = 20;
    internal const double PanelSectionSize = 16;
    internal const double PanelBodySize = 14;
    internal const double PanelDetailSize = 12;

    // Sampled from the native es-MX summoner spell picker.
    internal static readonly Brush Surface = Overlay.Paint("#010A13");
    internal static readonly Brush HeaderSurface = Overlay.Paint("#0C1922");
    internal static readonly Brush Divider = Overlay.Paint("#463714");
    internal static readonly Brush Frame = OverlayPanelStyle.VerticalGradient("#463714", "#785A28");
    internal static readonly Brush AccentFrame = OverlayPanelStyle.VerticalGradient("#F0E6D2", "#C89B3C");
    internal static readonly Brush Ink = Overlay.Paint("#EFE5D1");
    internal static readonly Brush Muted = Overlay.Paint("#9F9A8B");
    internal static readonly Brush Gold = Overlay.Paint("#E4C482");
    internal static readonly Brush SPlusSurface = Overlay.Paint("#201B10");
    private static readonly Brush Parchment = Overlay.Paint("#C8BFA9");

    internal static readonly FontFamily Heading = new("Beaufort for LOL, Cambria");
    internal static readonly FontFamily Body = new("Spiegel, Segoe UI");

    internal static Brush Tier(string? grade) => grade switch
    {
        "S+" or "S" => Gold,
        "A" => Ink,
        "B" => Parchment,
        _ => Muted
    };

    internal static Border Panel(UIElement child, Thickness padding)
    {
        var panel = new Border
        {
            Background = Surface, BorderBrush = Frame, BorderThickness = new Thickness(2),
            Padding = padding, Child = child, SnapsToDevicePixels = true
        };
        TextElement.SetFontFamily(panel, Body);
        TextOptions.SetTextFormattingMode(panel, TextFormattingMode.Display);
        return panel;
    }

    internal static Border Badge(string? tier, double width, double height, double fontSize)
    {
        var letter = new TextBlock
        {
            Text = tier ?? "—", Foreground = Tier(tier), FontFamily = Heading, FontSize = fontSize,
            FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        return new Border
        {
            Width = width, Height = height, Background = Surface,
            BorderBrush = tier is "S" or "S+" ? AccentFrame : Frame, BorderThickness = new Thickness(1.5),
            Child = letter, SnapsToDevicePixels = true
        };
    }

    internal static Border ChampionBadge(string? tier, string champion, double width, double height, double fontSize,
        string source = "OP.GG")
    {
        var badge = Badge(tier, width, height, fontSize);
        AutomationProperties.SetName(badge, $"{champion} · {(tier is null ? "Sin tier" : $"Tier {tier}")} de {source}");
        return badge;
    }
}

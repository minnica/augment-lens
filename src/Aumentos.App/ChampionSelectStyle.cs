using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Aumentos;

internal static class ChampionSelectStyle
{
    internal static Border Portrait(ChampionSelectPortraits? portraits, string champion, double size)
    {
        var image = new Image();
        var frame = new Border
        {
            Width = size, Height = size, BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center, Child = image
        };
        if (portraits is not null)
            _ = LoadPortrait(portraits, champion, image, frame);
        return frame;
    }

    private static async Task LoadPortrait(ChampionSelectPortraits portraits, string champion, Image image, Border frame)
    {
        if (await portraits.Read(champion) is { } source)
        {
            image.Source = source;
            frame.BorderBrush = MayhemTierStyle.Divider;
        }
    }
}

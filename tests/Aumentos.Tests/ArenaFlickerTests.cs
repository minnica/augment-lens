using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Aumentos;

internal static class ArenaFlickerTests
{
    internal static void Run(Ratings ratings, string assets, string? screenshot)
    {
        byte[] title = [22, 22, 105, 22, 22, 22];
        byte[] brighter = [34, 34, 117, 34, 34, 34];
        byte[] changed = [34, 34, 34, 34, 117, 34];
        Check(Detection.Difference(title, brighter) >= 2.2 && Detection.Difference(title, brighter, true) < 2.2 &&
            Detection.Difference(title, changed, true) >= 2.2, "la iluminación no invalida una carta cuyo texto sigue igual");
        Check(Detection.RecoverArenaCraftingTitle("Debes sobrevivir varias rondas Obtienes una ranura de Aumento") ==
                "Obtienes una ranura de Aumento" &&
            Detection.RecoverArenaCraftingTitle("1enes un Yunque de Estadísticas Prismático") ==
                "Obtienes un Yunque de Estadísticas Prismático" && Detection.RecoverArenaCraftingTitle("Estadísticas") is null,
            "las frases distintivas de la ronda de creación permiten recuperar el título completo");

        if (screenshot is null)
            return;

        var image = Diagnostics.OpenBitmap(screenshot);
        var bounds = new Int32Rect(0, 0, image.PixelWidth, image.PixelHeight);
        var layouts = new LayoutStore(Path.Combine(assets, "layouts.json"), "").Find(Mode.Arena, bounds.Width, bounds.Height);
        using var detector = new Detection(ratings, Path.Combine(assets, "tessdata"));
        var initial = detector.ReadPanel(image, bounds, layouts, Mode.Arena, null);
        Check(initial is { } read && read.Result.Cards.SequenceEqual([341, 340, 342]),
            "la captura de Ivern reconoce las tres recompensas en orden");
        var layout = initial!.Value.Layout;
        var source = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var brightened = new WriteableBitmap(source);
        foreach (var region in layout.Titles)
        {
            var rect = region.Pixels(bounds.Width, bounds.Height);
            int stride = rect.Width * 4;
            var pixels = new byte[stride * rect.Height];
            source.CopyPixels(rect, pixels, stride, 0);
            for (int pixel = 0; pixel < pixels.Length; pixel += 4)
                for (int channel = 0; channel < 3; channel++)
                    pixels[pixel + channel] = (byte)Math.Min(255, pixels[pixel + channel] + 10);
            brightened.WritePixels(rect, pixels, stride, 0);
        }
        var lit = detector.ReadPanel(brightened, bounds, layouts, Mode.Arena, layout);
        Check(lit is { } stable && stable.Result.Cards.SequenceEqual([341, 340, 342]),
            "la animación conserva los tiers de las tres cartas visibles");

        var first = layout.Titles[0].Pixels(bounds.Width, bounds.Height);
        var removed = new Int32Rect(first.X, first.Y - first.Height, first.Width, first.Height * 2);
        int removedStride = removed.Width * 4;
        brightened.WritePixels(removed, new byte[removedStride * removed.Height], removedStride, 0);
        var reroll = detector.ReadPanel(brightened, bounds, layouts, Mode.Arena, layout);
        Check(reroll is { } changedSlot && changedSlot.Result.Cards.SequenceEqual([null, 340, 342]),
            "una carta que desaparece durante el reroll pierde el tier anterior");

        int closedStride = bounds.Width * 4;
        var closed = BitmapSource.Create(bounds.Width, bounds.Height, 96, 96, PixelFormats.Bgr32, null,
            new byte[closedStride * bounds.Height], closedStride);
        Check(detector.ReadPanel(closed, bounds, layouts, Mode.Arena, layout) is null,
            "el panel cerrado oculta los tiers y limpia la selección");
    }

    private static void Check(bool result, string message)
    {
        if (!result)
            throw new InvalidOperationException(message);
    }
}

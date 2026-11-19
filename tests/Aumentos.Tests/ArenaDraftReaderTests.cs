using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Aumentos;

internal static class ArenaDraftReaderTests
{
    internal static void Run(Ratings ratings, string assets, string screenshot)
    {
        var image = Diagnostics.OpenBitmap(screenshot);
        using var detector = new Detection(ratings, Path.Combine(assets, "tessdata"));
        var reader = new ArenaDraftReader(ratings.Catalog);
        string[] expected = ["Aurora", "Camille", "Draven", "Karthus"];
        var original = Read(image);
        var scaled = Read(new TransformedBitmap(image, new ScaleTransform(0.64, 0.64)));
        Check(original.Select(offer => (int)Math.Round(offer.Tile.X * 0.64)).SequenceEqual(scaled.Select(offer => offer.Tile.X)),
            "las posiciones de la fila se ajustan a la ventana");

        var withoutAnchor = new WriteableBitmap(new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0));
        int nameWidth = image.PixelWidth * 8 / 100;
        int nameHeight = image.PixelHeight * 3 / 100;
        var anchor = new Int32Rect(image.PixelWidth * 26 / 100, image.PixelHeight * 278 / 1000, nameWidth, nameHeight);
        withoutAnchor.WritePixels(anchor, new byte[nameWidth * nameHeight * 4], nameWidth * 4, 0);
        Check(reader.Read(withoutAnchor, detector) is null, "rechaza la fila sin la etiqueta Valentía");
        Console.WriteLine("Fila de Valentía verificada con la captura original, reescalado y ancla obligatoria.");

        ArenaDraftOffer[] Read(BitmapSource frame)
        {
            var offers = reader.Read(frame, detector);
            Check(offers?.Select(offer => offer.Champion.Key).SequenceEqual(expected) == true,
                "reconoce Aurora, Camille, Draven y Karthus en orden sin incluir Valentía");
            return offers!;
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}

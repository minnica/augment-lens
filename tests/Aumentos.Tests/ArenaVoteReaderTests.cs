using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Aumentos;

internal static class ArenaVoteReaderTests
{
    internal static ArenaVoteOffer Verify(Ratings ratings, string assets, string shot, int expectedRound = 2)
    {
        (string Guest, string Title)[] options = expectedRound == 8
            ? [("Garen", "Perseverancia"), ("JarvanIV", "Pesada es la Corona"), ("Annie", "Muro de Fuego")]
            : [("Yone", "Demonio interior"), ("Evelynn", "Solo Algo Casual"), ("TahmKench", "Negociación Oscura")];
        int expectedHealth = expectedRound == 8 ? 70 : 100;
        var capture = BitmapFrame.Create(new Uri(Path.GetFullPath(shot)), BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        using var ocr = new Detection(ratings, Path.Combine(assets, "tessdata"));
        var reader = new ArenaVoteReader(ocr);
        var original = VerifyFrame(capture);
        Check(ReferenceEquals(reader.Read(capture), original), "una captura estable reutiliza la lectura");
        foreach (var scale in new[] { .75, .5 })
            VerifyFrame(new TransformedBitmap(capture, new ScaleTransform(scale, scale)));

        var withoutScores = new CroppedBitmap(capture, new Int32Rect(0, 0,
            capture.PixelWidth - capture.PixelHeight * 14 / 100, capture.PixelHeight));
        Check(reader.Read(withoutScores) is { TeamHealth: null }, "las opciones se leen cuando la vida del equipo no es visible");
        int left = capture.PixelWidth / 4;
        var withoutBallot = new CroppedBitmap(capture, new Int32Rect(left, 0, capture.PixelWidth - left, capture.PixelHeight));
        Check(reader.Read(withoutBallot) is null, "desaparece la lectura al desaparecer la votación");
        Check(!ReferenceEquals(reader.Read(capture), original), "la siguiente votación se vuelve a leer");
        Console.WriteLine($"Invitados de Arena de la ronda {expectedRound} verificados con OCR de la captura y escalas menores.");
        return original;

        ArenaVoteOffer VerifyFrame(BitmapSource frame)
        {
            var offer = reader.Read(frame) ?? throw new InvalidOperationException("No se reconoció la votación de Arena.");
            Check(offer.Round == expectedRound, $"la captura corresponde a la ronda {expectedRound}");
            Check(offer.TeamHealth == expectedHealth, $"la placa dorada propia muestra {expectedHealth} de vida");
            Check(offer.Options.Select(option => (option.Guest, option.Title)).SequenceEqual(options),
                "identifica las tres opciones en el orden de la votación");
            for (int i = 0; i < offer.Options.Length; i++)
            {
                var bounds = offer.Options[i].Bounds;
                Check(bounds.X >= 0 && bounds.Width > 0 && bounds.X + bounds.Width < frame.PixelWidth / 4 &&
                    Math.Abs(bounds.Y / (double)frame.PixelHeight - (.302 + i * .0765)) < .01 &&
                    Math.Abs(bounds.Height / (double)frame.PixelHeight - .056) < .01,
                    "cada valoración puede alinearse con su opción nativa");
            }
            return offer;
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException($"Invitados de Arena: {message}.");
    }
}

using System.IO;
using System.Windows;
using Aumentos;

internal static class ArenaShardTests
{
    internal static void Run(Ratings ratings, string assets, string? screenshot)
    {
        var rell = new GameContext(Mode.Arena, "Rell");
        string[] titles = ["Frasmento de noder", "Fragmento de aceleración de habilidad", "Fraomento de Vida"];
        var offer = new DetectionResult(true, new int?[3], titles, new bool[3], titles.Select(ArenaShardRatings.Recognize).ToArray());
        CheckRell(ratings, rell, offer);
        Check(ratings.Shards.ForOffer(rell with { Mode = Mode.Caos }, offer) is null &&
            ratings.Shards.ForOffer(rell, offer with { Items = [false, false, true] }) is null &&
            ArenaShardRatings.Recognize("Fragmento de aspecto") is null,
            "los fragmentos se identifican en su propio panel de Arena");
        var partial = ratings.Shards.ForOffer(rell, offer with { Shards = [null, ArenaShard.Haste, ArenaShard.Health] });
        Check(partial?[1]?.ArenaChoice is { Tier: Tier.S, Recommended: false },
            "la lectura incompleta no destaca un ganador");

        if (screenshot is not null)
        {
            var image = Diagnostics.OpenBitmap(screenshot);
            var layouts = new LayoutStore(Path.Combine(assets, "layouts.json"), "")
                .Find(Mode.Arena, image.PixelWidth, image.PixelHeight);
            using var detector = new Detection(ratings, Path.Combine(assets, "tessdata"));
            var captured = detector.ReadPanel(image, new Int32Rect(0, 0, image.PixelWidth, image.PixelHeight),
                layouts, Mode.Arena, previous: null);
            Check(captured is not null, "el yunque de la captura se detectó");
            CheckRell(ratings, rell, captured!.Value.Result);
            Console.WriteLine("Yunque de Rell: Poder C · Aceleración S recomendada · Vida A.");
        }
        Console.WriteLine("Fragmentos de Arena verificados con afinidad local y lectura parcial.");
    }

    private static void CheckRell(Ratings ratings, GameContext rell, DetectionResult offer)
    {
        var badges = ratings.Shards.ForOffer(rell, offer);
        Check(offer.Shards is [ArenaShard.Power, ArenaShard.Haste, ArenaShard.Health] && badges is
            [{ ArenaChoice: { Tier: Tier.C } }, { ArenaChoice: { Tier: Tier.S, Recommended: true } }, { ArenaChoice: { Tier: Tier.A } }],
            $"los tres fragmentos quedan identificados y Rell prioriza aceleración: {string.Join(" / ", offer.Texts)}");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

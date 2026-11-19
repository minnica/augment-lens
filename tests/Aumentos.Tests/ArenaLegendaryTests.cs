using System.IO;
using System.Windows;
using Aumentos;

internal static class ArenaLegendaryTests
{
    private static readonly string[] RellOffer = ["Flautas de Bandle", "Fuerza de la Naturaleza", "Convergencia de Zeke"];

    internal static void Run(Ratings ratings, string assets, string? screenshot)
    {
        var rell = new GameContext(Mode.Arena, "Rell");
        var offer = new DetectionResult(true, new int?[3], RellOffer, [true, true, true]);
        CheckRellOffer(ratings, rell, offer);

        var jinx = ratings.Legendaries.ForOffer(rell with { Champion = "Jinx" },
            offer with { Texts = ["Filo del Infinito", "Corazón de Acero", "Convergencia de Zeke"] });
        Check(jinx is [ { ArenaChoice: { Tier: Tier.A, Recommended: true } }, { Local: "C" }, { Local: "C" } ],
            "los objetos de críticos favorecen a Jinx y los de tanque a Rell");
        Check(ratings.Prismatics.Identify(offer) is null &&
            ratings.Legendaries.Identify(offer with { Texts = ["Cuota del Segador", "Armadura Pétrea", "Fulminación"] }) is null &&
            ratings.Legendaries.Identify(offer with { Texts = ["Flautas de Bandle", "Guantelete del Agujero Negro", "Convergencia de Zeke"] }) is null &&
            ratings.Legendaries.Identify(offer with { Texts = ["Botas de Mercurio", "Fragmento de Vida", "Botas Jonias de la Lucidez"] }) is null &&
            ratings.Legendaries.Identify(offer with { Cards = [1, null, null] }) is null &&
            ratings.Legendaries.ForOffer(rell with { Mode = Mode.Caos }, offer) is null,
            "las cartas legendarias se distinguen de prismáticos, fragmentos, botas y aumentos");

        var partial = ratings.Legendaries.ForOffer(rell,
            offer with { Texts = ["Flautas de Bandle", "Convergencia de Zeke", ""], Items = [true, true, false] });
        Check(partial is [ { ArenaChoice: { Tier: Tier.S, Recommended: false } }, { Local: "A" }, null ],
            "una carta ilegible conserva los tiers conocidos sin elegir la mejor de toda la oferta");

        if (screenshot is not null)
        {
            var image = Diagnostics.OpenBitmap(screenshot);
            var layouts = new LayoutStore(Path.Combine(assets, "layouts.json"), "")
                .Find(Mode.Arena, image.PixelWidth, image.PixelHeight);
            using var detector = new Detection(ratings, Path.Combine(assets, "tessdata"));
            var read = detector.ReadPanel(image, new Int32Rect(0, 0, image.PixelWidth, image.PixelHeight),
                layouts, Mode.Arena, previous: null);
            Check(read is not null, "panel de objetos legendarios en la captura de Rell");
            CheckRellOffer(ratings, rell, read!.Value.Result);
            Console.WriteLine("Captura de Rell: Flautas de Bandle S recomendadas · Fuerza de la Naturaleza B · Convergencia de Zeke A.");
        }
        Console.WriteLine("Tiers de objetos legendarios verificados con afinidad por campeón.");
    }

    private static void CheckRellOffer(Ratings ratings, GameContext rell, DetectionResult offer)
    {
        var badges = ratings.Legendaries.ForOffer(rell, offer);
        Check(ratings.Legendaries.Identify(offer)?.Select(item => item?.Id).SequenceEqual([222524, 224401, 223050]) == true &&
            badges is [
                { ArenaChoice: { Tier: Tier.S, Recommended: true } },
                { ArenaChoice: { Tier: Tier.B, Recommended: false } },
                { ArenaChoice: { Tier: Tier.A, Recommended: false } } ] &&
            badges.All(badge => badge!.External is null),
            $"el control de Rell activa Flautas de Bandle: {string.Join(" / ", offer.Texts)}");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

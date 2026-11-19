using System.IO;
using System.Windows;
using Aumentos;

internal static class ArenaPrismaticTests
{
    private static readonly string[] RellOffer = ["Cuota del Segador", "Armadura Pétrea", "Fulminación"];

    internal static void Run(Ratings ratings, string assets, string? screenshot)
    {
        var rell = new GameContext(Mode.Arena, "Rell");
        var offer = new DetectionResult(true, new int?[3], RellOffer, [true, true, true]);
        CheckRellOffer(ratings, rell, offer);

        var jinx = ratings.Prismatics.ForOffer(rell with { Champion = "Jinx" }, offer);
        Check(jinx?[0]?.Local == "A" && jinx[1]?.Local == "C" && jinx[2]?.Local == "A",
            "el tier baja para un tanque si el campeón depende de los ataques básicos");

        var shared = offer with { Texts = ["Garra del Merodeador", "Desesperación Eterna", "Eco de Luden"] };
        Check(ratings.Prismatics.ForOffer(rell, shared) is null &&
            ratings.Prismatics.ForOffer(rell with { Mode = Mode.Caos }, offer) is null &&
            ratings.Prismatics.ForOffer(rell, offer with { PanelVisible = false }) is null &&
            ratings.Prismatics.ForOffer(rell, offer with { Cards = [1, null, null] }) is null,
            "objetos normales, aumentos, otros modos y panel ausente conservan su flujo");

        var partial = ratings.Prismatics.ForOffer(rell,
            offer with { Texts = ["Garra del Merodeador", "Armadura Pétrea", ""], Items = [true, true, false] });
        Check(partial?[1]?.ArenaChoice is { Tier: Tier.S, Recommended: false } && partial[2] is null,
            "un título exclusivo confirma al prismático ambiguo en un panel incompleto");

        if (screenshot is not null)
        {
            var image = Diagnostics.OpenBitmap(screenshot);
            var layouts = new LayoutStore(Path.Combine(assets, "layouts.json"), "")
                .Find(Mode.Arena, image.PixelWidth, image.PixelHeight);
            using var detector = new Detection(ratings, Path.Combine(assets, "tessdata"));
            var result = detector.ReadPanel(image, new Int32Rect(0, 0, image.PixelWidth, image.PixelHeight),
                layouts, Mode.Arena, null);
            Check(result is not null, "panel prismático de la captura de Rell");
            CheckRellOffer(ratings, rell, result!.Value.Result);
            Console.WriteLine("Captura de Rell: Cuota del Segador C · Armadura Pétrea S recomendada · Fulminación C.");
        }
        Console.WriteLine("Tiers de objetos prismáticos verificados por campeón, con aislamiento de otros paneles.");
    }

    private static void CheckRellOffer(Ratings ratings, GameContext rell, DetectionResult offer)
    {
        var badges = ratings.Prismatics.ForOffer(rell, offer);
        Check(badges is [
                { ArenaChoice: { Tier: Tier.C, Recommended: false } },
                { ArenaChoice: { Tier: Tier.S, Recommended: true } },
                { ArenaChoice: { Tier: Tier.C, Recommended: false } } ] &&
            badges.All(badge => badge!.External is null) && !offer.Cards.Any(id => id.HasValue),
            $"Armadura Pétrea supera a las dos opciones de ataque básico para Rell: {string.Join(" / ", offer.Texts)}");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

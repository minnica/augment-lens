using System.IO;
using System.Windows;
using Aumentos;

internal static class ArenaPrismaticTests
{
    private static readonly string[] RellOffer = ["Cuota del Segador", "Armadura Pétrea", "Fulminación"];
    private static readonly string[] TristanaOffer = ["Cuota del Segador", "Escultor de Runas", "Decapitador"];
    private static readonly string[] ChoGathOffer = ["Fuerza de la Entropía", "Manto de la Noche Estrellada", "Armadura de Warmog"];

    internal static void Run(Ratings ratings, string assets, string? screenshot, string? tristanaScreenshot,
        string? choGathScreenshot = null, string? renataScreenshot = null)
    {
        var rell = new GameContext(Mode.Arena, "Rell");
        var offer = new DetectionResult(true, new int?[3], RellOffer, [true, true, true]);
        CheckRellOffer(ratings, rell, offer);

        var jinx = ratings.Prismatics.ForOffer(rell with { Champion = "Jinx" }, offer)?.Badges;
        Check(jinx?[0]?.LocalText == "S+" && jinx[1]?.Local == "C" && jinx[2]?.Local == "A",
            "el tier baja para un tanque si el campeón depende de los ataques básicos");

        var shared = offer with { Texts = ["Garra del Merodeador", "Desesperación Eterna", "Eco de Luden"] };
        Check(ratings.Prismatics.ForOffer(rell, shared) is null &&
            ratings.Prismatics.ForOffer(rell with { Mode = Mode.Caos }, offer) is null &&
            ratings.Prismatics.ForOffer(rell, offer with { PanelVisible = false }) is null &&
            ratings.Prismatics.ForOffer(rell, offer with { Cards = [1, null, null] }) is null,
            "objetos normales, aumentos, otros modos y panel ausente conservan su flujo");

        var partial = ratings.Prismatics.ForOffer(rell,
            offer with { Texts = ["Garra del Merodeador", "Armadura Pétrea", ""], Items = [true, true, false] })?.Badges;
        Check(partial?[1]?.ArenaChoice is { Tier: Tier.S, Recommended: false } && partial[2] is null,
            "un título exclusivo confirma al prismático ambiguo en un panel incompleto");

        var tristana = new GameContext(Mode.Arena, "Tristana");
        var tristanaOffer = offer with { Texts = TristanaOffer };
        var top = CheckTristanaOffer(ratings, tristana, tristanaOffer);
        var reroll = ratings.Prismatics.ForOffer(tristana, tristanaOffer with
        {
            Texts = ["Armadura Pétrea", "Escultor de Runas", "Decapitador"]
        });
        Check(reroll?.Top.Entries == top.Entries && !reroll.Top.IsOffered(0) &&
            reroll.Badges[2]?.ArenaChoice?.Recommended == true,
            "el reroll cambia la disponibilidad y la mejor opción de la oferta");

        var choGath = new GameContext(Mode.Arena, "Chogath");
        CheckChoGathOffer(ratings, choGath, offer with { Texts = ChoGathOffer });
        var renata = new GameContext(Mode.Arena, "Renata");
        var crown = ratings.RecoverArenaItemTitle("Corona de la Reina Fraomentada");
        Check(crown == Ratings.Normalize("Corona de la Reina Fragmentada") &&
            ratings.RecoverArenaItemTitle("Corona de la Reina Frxomentada") is null &&
            ratings.RecoverArenaItemTitle("Corona de la Reina") is null,
            "la Corona admite un glifo mal leído y sigue requiriendo un título largo y unívoco");
        CheckRenataOffer(ratings, renata, offer with
        {
            Texts = ["Espada Encantada por la Luna", crown!, "Filo del Crepúsculo"]
        });
        if (screenshot is not null)
        {
            CheckRellOffer(ratings, rell, ReadScreenshot(ratings, assets, screenshot));
            Console.WriteLine("Captura de Rell: Cuota del Segador C · Armadura Pétrea S recomendada · Fulminación C.");
        }
        if (tristanaScreenshot is not null)
        {
            CheckTristanaOffer(ratings, tristana, ReadScreenshot(ratings, assets, tristanaScreenshot));
            Console.WriteLine("Captura de Tristana: Top 5 y Cuota del Segador S+ recomendada.");
        }
        if (choGathScreenshot is not null)
        {
            CheckChoGathOffer(ratings, choGath, ReadScreenshot(ratings, assets, choGathScreenshot));
            Console.WriteLine("Captura de Cho'Gath: Manto de la Noche Estrellada muestra Top #5 sobre la carta.");
        }
        if (renataScreenshot is not null)
        {
            CheckRenataOffer(ratings, renata, ReadScreenshot(ratings, assets, renataScreenshot));
            Console.WriteLine("Captura de Renata: Corona de la Reina Fragmentada B recomendada.");
        }
        Console.WriteLine("Tiers de objetos prismáticos verificados por campeón, con aislamiento de otros paneles.");
    }

    private static DetectionResult ReadScreenshot(Ratings ratings, string assets, string screenshot)
    {
        var image = Diagnostics.OpenBitmap(screenshot);
        var layouts = new LayoutStore(Path.Combine(assets, "layouts.json"), "")
            .Find(Mode.Arena, image.PixelWidth, image.PixelHeight);
        using var detector = new Detection(ratings, Path.Combine(assets, "tessdata"));
        var read = detector.ReadPanel(image, new Int32Rect(0, 0, image.PixelWidth, image.PixelHeight),
            layouts, Mode.Arena, null);
        return read?.Result ?? throw new InvalidOperationException("No se reconoció el panel prismático de la captura.");
    }

    private static ArenaPrismaticTop CheckTristanaOffer(Ratings ratings, GameContext tristana, DetectionResult offer)
    {
        var result = ratings.Prismatics.ForOffer(tristana, offer);
        Check(result is { Badges: [
                { ArenaChoice: { Tier: Tier.S, Recommended: true, TopPosition: 1 } },
                { ArenaChoice: { Tier: Tier.C, Recommended: false } },
                { ArenaChoice: { Tier: Tier.A, Recommended: false } } ] },
            "Cuota del Segador es la mejor opción de la oferta de Tristana");
        var top = result!.Top;
        Check(top.Entries.Length == 5 && top.Entries[0].Id == 443090 && top.IsOffered(0) &&
            top.Entries.Select(entry => entry.Id).Distinct().Count() == 5 &&
            top.Entries.Select((entry, index) => top.IsOffered(index) == TristanaOffer.Contains(entry.Rating.Name)).All(match => match) &&
            Enumerable.Range(0, 5).Any(index => !top.IsOffered(index)),
            "el top de Tristana abarca todo el catálogo y distingue las opciones que salieron");
        return top;
    }

    private static void CheckChoGathOffer(Ratings ratings, GameContext choGath, DetectionResult offer)
    {
        var result = ratings.Prismatics.ForOffer(choGath, offer);
        Check(result is { Badges: [
                { ArenaChoice: { Tier: Tier.A, TopPosition: null, Recommended: false } },
                { ArenaChoice: { Tier: Tier.A, TopPosition: 5, TopLabel: " · Top #5", Recommended: false } },
                { ArenaChoice: { Tier: Tier.A, TopPosition: null, Recommended: false } } ] } &&
            result.Top.Entries[4].Id == 443059 && result.Top.IsOffered(4),
            "la posición del distintivo de Manto coincide con el top lateral y conserva el empate de tiers");
    }

    private static void CheckRellOffer(Ratings ratings, GameContext rell, DetectionResult offer)
    {
        var badges = ratings.Prismatics.ForOffer(rell, offer)?.Badges;
        Check(badges is [
                { ArenaChoice: { Tier: Tier.C, Recommended: false } },
                { ArenaChoice: { Tier: Tier.S, Recommended: true } },
                { ArenaChoice: { Tier: Tier.C, Recommended: false } } ] &&
            badges.All(badge => badge!.External is null) && !offer.Cards.Any(id => id.HasValue),
            $"Armadura Pétrea supera a las dos opciones de ataque básico para Rell: {string.Join(" / ", offer.Texts)}");
    }

    private static void CheckRenataOffer(Ratings ratings, GameContext renata, DetectionResult offer)
    {
        var badges = ratings.Prismatics.ForOffer(renata, offer)?.Badges;
        Check(offer.Items.All(item => item) && badges is [
                { ArenaChoice: { Tier: Tier.C, Recommended: false } },
                { ArenaChoice: { Name: "Corona de la Reina Fragmentada", Tier: Tier.B, Recommended: true } },
                { ArenaChoice: { Tier: Tier.C, Recommended: false } } ],
            $"la Corona supera las dos alternativas C para Renata: {string.Join(" / ", offer.Texts)}");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

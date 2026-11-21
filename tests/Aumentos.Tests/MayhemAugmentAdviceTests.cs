using System.IO;
using System.Windows;
using Aumentos;

internal static class MayhemAugmentAdviceTests
{
    internal static void Run(Ratings ratings, string assets, string? capturePath)
    {
        const int vulnerability = 1092, stillHere = 3131, littleHelp = 2096;
        int?[] offer = [vulnerability, stillHere, littleHelp];
        var patch = ratings.Catalog.Patch;
        var sivir = new GameContext(Mode.Caos, "Sivir");
        var advice = new MayhemAugmentAdvice(ratings, MayhemItemCatalog.Load(patch, assets));
        var snapshot = new OpGgSnapshot(patch, Mode.Caos, "Sivir", DateTimeOffset.UtcNow,
            new() { [vulnerability] = new("S"), [stillHere] = new("S"), [littleHelp] = new("S"), [3086] = new("S") });
        var match = new MayhemMatch([3032, 3085], [new("JarvanIV", []), new("Teemo", []), new("Kennen", [])],
            CombatStats: new(0.58, 0, 500));

        var original = Compare(offer, match);
        Check(Winner(original) == 0 && original[0]?.LocalReason?.Contains("rebotes") == true &&
            original.All(badge => badge?.VisibleTierText == "S" && badge.External?.Tier == "S"),
            "los tres tiers de Sivir siguen en S y Vulnerabilidad gana con 58 % de crítico");
        var capped = match with { CombatStats = new(1, .15, 500) };
        Check(Winner(Compare(offer, capped)) == 2, "al llegar al tope de crítico conviene el alcance de la R");
        Check(Winner(Compare(offer, match with { Opponents = [..match.Opponents, new("Nocturne", [])] })) == 1,
            "con poco robo de vida y dos rivales que se lanzan encima gana Sigo Aquí");
        Check(Winner(Compare([littleHelp, vulnerability, stillHere], match)) == 1,
            "el desempate sigue a la carta y no a su posición");
        Check(Winner(Compare([littleHelp, null, stillHere], match)) == -1 &&
            Winner(Compare([littleHelp, littleHelp, stillHere], match)) == -1 &&
            Winner(Compare(offer, null)) == -1 && Winner(Compare(offer, match with { CombatStats = null })) == -1,
            "el OCR incompleto o duplicado y la falta de evidencia no producen una recomendación");
        Check(Winner(Compare(offer, match, snapshot with { Cards = new(snapshot.Cards) { [littleHelp] = new("A") } })) == 0 &&
            Winner(Compare(offer, match, snapshot with
                { Cards = new(snapshot.Cards) { [vulnerability] = new("A"), [stillHere] = new("A") } })) == -1,
            "solo se desempatan las cartas con el mejor tier visible");

        const int scoped = 1170;
        var kog = new GameContext(Mode.Caos, "KogMaw");
        var kogOffer = new int?[] { vulnerability, stillHere, scoped };
        var kogSource = snapshot with { Champion = kog.Champion, Cards = new(snapshot.Cards) { [scoped] = new("S") } };
        var kogMatch = match with { Items = [3153], CombatStats = new(.25, .1, 500) };
        var kogBadges = CardComparison.ForOffer(ratings, kog, kogOffer, new bool[3], kogSource);
        advice.Apply(kog, kogMatch, kogOffer, kogBadges);
        Check(Winner(kogBadges) == 0 && kogBadges[0]?.LocalReason?.Contains("Espada del Rey Arruinado") == true,
            "el daño de un objeto confirmado puede decidir el desempate para otro campeón");

        var sample = new Dictionary<int, AramHexTier> { [3086] = new(Tier.A, 3400), [stillHere] = new(Tier.B, 3200),
            [littleHelp] = new(Tier.C, 3100) };
        var hex = new AramHexSnapshot(patch, patch, 1, DateTimeOffset.UtcNow, new() { [sivir.Champion] = sample });
        Check(Winner(Compare([3086, stillHere, littleHelp], match, aramHex: hex)) == 0 &&
            Winner(Compare([3086, stillHere, littleHelp], match, aramHex: hex with { SourcePatch = "16.22" })) == -1,
            "AramHex desempata ante cobertura de la oferta en el parche vigente");

        if (capturePath is not null)
        {
            var layouts = new LayoutStore(Path.Combine(assets, "layouts.json"), Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json"));
            var image = Diagnostics.OpenBitmap(capturePath);
            var bounds = new Int32Rect(0, 0, image.PixelWidth, image.PixelHeight);
            using var detector = new Detection(ratings, Path.Combine(assets, "tessdata"));
            var read = detector.ReadPanel(image, bounds, layouts.Find(Mode.Caos, bounds.Width, bounds.Height), Mode.Caos, previous: null);
            Check(read?.Result.Cards.SequenceEqual(offer) == true && Winner(Compare(read.Value.Result.Cards, match)) == 0,
                "la captura de Sivir se reconoce y recorre el desempate de las tres S");
        }
        Console.WriteLine("Desempate de aumentos de ARAM Caos verificado.");

        CardComparison?[] Compare(int?[] ids, MayhemMatch? context, OpGgSnapshot? source = null, AramHexSnapshot? aramHex = null)
        {
            var badges = CardComparison.ForOffer(ratings, sivir, ids, new bool[ids.Length], source ?? snapshot, aramHex: aramHex);
            advice.Apply(sivir, context, ids, badges);
            return badges;
        }
    }

    private static int Winner(CardComparison?[] badges) => Enumerable.Range(0, badges.Length)
        .Where(index => badges[index]?.RecommendedAugment == true).DefaultIfEmpty(-1).Single();

    private static void Check(bool result, string message)
    {
        if (!result)
            throw new InvalidOperationException(message);
    }
}

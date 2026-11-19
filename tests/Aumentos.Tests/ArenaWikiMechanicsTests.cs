using System.IO;
using Aumentos;

internal static class ArenaWikiMechanicsTests
{
    internal static void Run(Ratings ratings, string assets)
    {
        var wiki = ArenaWikiMechanics.Load(assets, ratings.Catalog);
        Check(wiki is
        {
            SourceRevision: > 0, SourceCount: >= 250, MappedCount: >= 235,
            License: "CC BY-SA 3.0", ValuesMatchPatch: false
        }, "la fuente del Wiki incluye cobertura, atribución y la revisión de sus mecánicas");

        var typhoon = ratings.Recognize(Mode.Arena, "Tifón")!;
        Check(wiki!.Find(typhoon) is
        {
            Name: "Typhoon", Rarity: "Silver", Level1: not null, Level2: not null, Level3: null,
            Signals: ArenaWikiSignal.AppliesOnHitToAdditionalTarget
        }, "la carta de Arena en español se vincula al nombre y a la mecánica del Wiki");
        Check(wiki.Find(typhoon with { Mode = nameof(Mode.Caos) }) is null &&
            wiki.Find(typhoon with { Key = "Other" }) is null,
            "el Wiki requiere modo, ID y clave de catálogo compatibles");

        var purist = ratings.Recognize(Mode.Arena, "Purista: Hechicero")!;
        Check(wiki.Find(purist) is { Name: "Purist - Caster" } && wiki.Find(29) is
        {
            Signals: ArenaWikiSignal.AppliesOnHitViaAbilities, Level1: "1 time.", Level3: "3 times."
        }, "los alias del catálogo y los niveles explícitos tienen su referencia de origen");
        Check(wiki.Find(22) is { Level1: var deftLevel } && deftLevel!.Contains("40%") && !wiki.ValuesMatchPatch,
            "la revisión del Wiki no se presenta como el valor vigente de Hábil");

        var wrongPatch = ratings.Catalog with { Patch = "0.0" };
        Check(ArenaWikiMechanics.Load(assets, wrongPatch) is null &&
            ArenaWikiMechanics.Load(Path.Combine(assets, "missing-wiki"), ratings.Catalog) is null,
            "el catálogo de Arena se puede usar sin una instantánea compatible del Wiki");
    }

    private static void Check(bool value, string description)
    {
        if (!value)
            throw new InvalidOperationException(description);
    }
}

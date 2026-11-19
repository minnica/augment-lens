using Aumentos;

internal static class ArenaAugmentAffinityTests
{
    internal static void Run(Ratings ratings)
    {
        var affinity = new ArenaAugmentAffinity(ratings);
        var covered = ratings.Catalog.Augments.Count(card => card.Mode == nameof(Mode.Arena) &&
            ratings.RateEditorial(Mode.Arena, "Chogath", card.Id) is null &&
            affinity.Evaluate("Chogath", card.Id)?.Tier is not null);
        Check(covered >= 24, $"cobertura editorial nueva de la Wiki ({covered})");

        int[] choBuild = [443083, 223111, 444644, 223084, 223068];
        var cho = Offer("Chogath", [181, 110, 44], equipped: choBuild);
        Check(cho[0] is { LocalText: "S+", UsesWiki: true, RecommendedAugment: true } &&
            cho[0]!.LocalReason!.Contains("tu vida máxima") &&
            cho[1]?.LocalReason?.Contains("vínculo") == true && cho[2]?.LocalText == "A", "oferta y equipo reales de Cho’Gath");
        var slow = affinity.Evaluate("Chogath", 75, equippedItems: choBuild);
        Check(slow is { Tier: Tier.S, Contextual: true } && affinity.Evaluate("Chogath", 75) is null,
            "Cocción Lenta escala con la vida máxima que realmente compró Cho’Gath");
        Check(affinity.Evaluate("Chogath", 29, equippedItems: [223115]) is { Tier: Tier.B, Contextual: true },
            "Arma Etérea usa Diente de Nashor sin eliminar la excepción editorial de Cho’Gath");

        int?[] jaxCards = [41, 87, 193];
        var jaxHealth = Offer("Jax", jaxCards, equipped: [223084, 223068]);
        var jaxHit = Offer("Jax", jaxCards, equipped: [223124, 3153]);
        Check(jaxHealth[0] is { LocalText: "S+", RecommendedAugment: true } &&
            jaxHit[1] is { LocalText: "S+", RecommendedAugment: true } && !jaxHit[0]!.RecommendedAugment,
            "Jax cambia de Goliat con vida a Tifón con efectos al impacto");
        var duplicate = Offer("Jax", jaxCards, equipped: [3153, 223153, 999999, -1, 0]);
        Check(duplicate.All(badge => badge?.LocalText != "S+" && badge?.RecommendedAugment != true),
            "IDs desconocidos o la misma compra en dos formatos no activan una sinergia doble");
        var both = Offer("Jax", jaxCards, equipped: [223084, 223068, 223124, 223153]);
        Check(both.Take(2).All(badge => badge is { LocalText: "S+", RecommendedAugment: false }),
            "un empate entre dos afinidades contextuales conserva ambas letras sin elegir al azar");
        var typhoonUpgrade = Offer("Jax", jaxCards, [null, 2, null], [223124, 223153]);
        Check(typhoonUpgrade[1] is { LocalText: "S+", UpgradeLevel: 2, RecommendedUpgrade: false } &&
            typhoonUpgrade[1]!.DetailText.Contains("Tier del efecto base"),
            "una mejora sin regla propia conserva la sinergia de Tifón con los objetos equipados");
        var legacyPick = Offer("Jax", [41, 81, 193]);
        var newPick = Offer("Jax", [41, 81, 193], equipped: [223084, 223068]);
        Check(legacyPick[1]?.RecommendedAugment == true && newPick[0]?.RecommendedAugment == true &&
            newPick[1]?.RecommendedAugment == false, "el inventario retira la recomendación editorial que quedó debajo");
        Check(affinity.Evaluate("Jax", 22, 2, [223124, 223153]) is { Tier: Tier.A } &&
            affinity.Evaluate("Jax", 22, 3, [223124, 223153]) is { Tier: Tier.S, Contextual: true },
            "Nivel 3 de Hábil aprovecha los efectos de impacto de Jax");
        var jaxUpgrade = Offer("Jax", [22, 51, 69], [3, 2, null], [223124, 223153]);
        Check(jaxUpgrade[0] is { LocalText: "S+", UpgradeLevel: 3, RecommendedUpgrade: true } &&
            jaxUpgrade.Skip(1).All(badge => badge?.RecommendedUpgrade == false),
            "la mejora Nv.3 de Hábil conserva su techo real en una oferta mixta");

        var observed = Offer("Jax", [79, 115, 69]);
        Check(observed[0] is { LocalText: "S+", LocalReason: "Cadencia Letal y Conquistador favorecen el combate sostenido" } &&
            observed[1] is { LocalText: "A", LocalReason: "Aumenta el alcance de tus ataques básicos" } &&
            observed[2] is { LocalText: "B", LocalReason: "Repele a rivales cercanos al perder vida" } &&
            ratings.Rate(Mode.Arena, "Ashe", 44) == Tier.A && affinity.Evaluate("Jax", 44) is null,
            "las cartas de la captura explican el efecto y Frío valora campeones que ralentizan regularmente");

        int?[] marksmanCards = [1328, 22, 52];
        var tristanaInitial = Offer("Tristana", marksmanCards, [2, 2, 2]);
        var tristanaCrit = Offer("Tristana", marksmanCards, [2, 2, 2], [223031, 223032]);
        Check(tristanaInitial[2] is { RecommendedUpgrade: true, LocalText: "A" } &&
            tristanaCrit[0] is { RecommendedUpgrade: true, LocalText: "A" } &&
            affinity.Evaluate("Tristana", 1328, 3, [223031, 223032]) is null,
            "los críticos comprados desempatan el Nv.2 real de Ritmo Crítico");
        var same = Offer("Tristana", marksmanCards, [2, 2, 2], [223031, 223032, 223006, 223153]);
        Check(same.All(badge => badge?.RecommendedUpgrade == false),
            "equipar velocidad y críticos deja empatadas las dos mejoras Nv. 2");
        var patch = ratings.Catalog.Patch;
        var external = new OpGgSnapshot(patch, Mode.Arena, "Tristana", DateTimeOffset.UtcNow,
            new() { [27] = new(PickPercent: 99m) });
        var unknownUpgrade = CardComparison.ForOffer(ratings, new(Mode.Arena, "Tristana"), [27, 22, 52], new bool[3],
            external, upgradeLevels: [2, 2, 2], arenaItems: [223031, 223032]);
        Check(unknownUpgrade[0] is { LocalText: "A", UsesWiki: true, UpgradeLevel: 2, External.PickPercent: 99m } &&
            unknownUpgrade[0]!.LocalReason!.Contains("Tier del efecto base") &&
            unknownUpgrade.All(badge => badge is { RecommendedUpgrade: false, RecommendedAugment: false }),
            "la oferta completa conserva afinidad base sin valorar una mejora con incremento incierto");
        var incomplete = Offer("Tristana", [1328, null, 52], [2, null, 2], [223031, 223032]);
        Check(incomplete.All(badge => badge?.RecommendedUpgrade != true), "no recomienda con cartas sin leer");
        Check(affinity.Evaluate("Jax", 87, 2) is null && affinity.Evaluate("Jax", 87, 4) is null &&
            affinity.Evaluate("Unknown", 87) is null && affinity.Evaluate("Jax", 239) is null &&
            affinity.Evaluate("Jax", 404) is null,
            "conserva Variable e ignora niveles, campeones y reglas sin respaldo");
        if (ratings.ArenaAugment(27)?.MaxLevel == 2)
            Check(affinity.Evaluate("Tristana", 27, 3) is null,
                "la evaluación directa rechaza un nivel mayor que el máximo del catálogo");
        Console.WriteLine($"Afinidad Arena verificada con inventario, niveles y {covered} valoraciones adicionales.");

        CardComparison?[] Offer(string champion, int?[] ids, int?[]? levels = null, int[]? equipped = null) =>
            CardComparison.ForOffer(ratings, new(Mode.Arena, champion), ids, new bool[ids.Length], null,
                upgradeLevels: levels, arenaItems: equipped);
    }

    private static void Check(bool condition, string label)
    {
        if (!condition)
            throw new InvalidOperationException(label);
    }
}

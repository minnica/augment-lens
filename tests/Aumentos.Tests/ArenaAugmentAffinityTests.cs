using Aumentos;

internal static class ArenaAugmentAffinityTests
{
    internal static void Run(Ratings ratings)
    {
        var affinity = new ArenaAugmentAffinity(ratings);
        var unknownPower = Offer("AurelionSol", [322])[0];
        var utilityPower = Offer("AurelionSol", [322], equipped: [223020, 447104, 3348])[0];
        var burnPower = Offer("AurelionSol", [322], equipped: [223020, 222503])[0];
        var doublePower = Offer("AurelionSol", [322], equipped: [222503, 226653, 2257])[0];
        Check(unknownPower is { IsArenaVariable: true, LocalReason: "Depende del daño de tus objetos y aumentos" } &&
            utilityPower is { IsArenaVariable: true, LocalReason: "Sin daño directo confirmado en tus objetos · Aumentos por confirmar" } &&
            burnPower is { LocalText: "B", LocalReason: "Amplifica el daño de Antorcha de Fuegoscuro", UsesWiki: false } &&
            doublePower is { LocalText: "A", LocalReason: "Amplifica el daño de Antorcha de Fuegoscuro y Angustia de Liandry" },
            "Poder Mejorado distingue un inventario desconocido, utilidad y efectos propios de daño confirmados");

        var covered = ratings.Catalog.Augments.Count(card => card.Mode == nameof(Mode.Arena) &&
            ratings.RateEditorial(Mode.Arena, "Chogath", card.Id) is null &&
            affinity.Evaluate("Chogath", card.Id)?.Tier is not null);
        Check(covered >= 24, $"cobertura editorial nueva de la Wiki ({covered})");

        var milio = Offer("Milio", [35, 5, 68]);
        Check(milio[0] is { LocalText: "B", UsesWiki: true } && milio[0]!.LocalReason!.Contains("Ignición y Extenuación") &&
            milio[1] is { LocalText: "A", UsesWiki: true } && milio[1]!.LocalReason!.Contains("tu aliado") &&
            milio[2] is { LocalText: "A" } && milio.All(badge => badge?.RecommendedAugment == false),
            "Milio recibe tier para Siente el Ardor y Estandarte de Mando y conserva el empate con Recursividad");
        Check(affinity.Evaluate("Chogath", 35) is { Tier: Tier.A } && affinity.Evaluate("Chogath", 5) is { Tier: Tier.B },
            "los perfiles de primera línea aprovechan Siente el Ardor y reciben una valoración general para Estandarte");

        var renata = Offer("Renata", [198, 215, 94]);
        Check(renata[0] is { LocalText: "A", RecommendedAugment: false, UsesWiki: true } &&
            renata[0]!.DetailText.Contains("enemigo cercano") &&
            renata[1] is { LocalText: "A", RecommendedAugment: true, UsesWiki: true } &&
            renata[1]!.DetailText.Contains("aceleración permanente") &&
            renata[2] is { LocalText: "A", RecommendedAugment: false, UsesWiki: true } &&
            renata[2]!.DetailText.Contains("Gastas parte de tu vida"),
            "Renata recomienda Bendición Oscura con tres aumentos A por la aceleración que acumula con su E");
        Check(Offer("Renata", [94, 198, 215])[2] is { RecommendedAugment: true } &&
            Offer("Renata", [198, 215, null]).All(badge => badge?.RecommendedAugment != true) &&
            Offer("Renata", [198, 215, 94], [2, null, null]).All(badge => badge?.RecommendedAugment != true),
            "el desempate de Renata sigue la carta y requiere comparar la oferta completa sin mezclar mejoras");
        var renataPrismatic = Offer("Renata", [123, 195, 30]);
        Check(renataPrismatic is [
                { LocalText: "Variable", LocalReason: "Cada uso cambia el hechizo al azar", RecommendedAugment: false },
                { LocalText: "B", RecommendedAugment: false },
                { LocalText: "A", LocalReason: "Convierte parte de tu poder de habilidad en aceleración", RecommendedAugment: false } ],
            "Ruleta del Invocador explica el azar y la oferta conserva los tiers conocidos de Renata");
        var renataUpgrade = Offer("Renata", [1332, 30, 88], [null, 3, null]);
        Check(renataUpgrade is [
                { LocalText: "C", LocalReason: "Ganas PH al perder vida, pero cada habilidad te cuesta vida", UsesWiki: true },
                { LocalText: "A", UpgradeLevel: 3, RecommendedUpgrade: true,
                    DetailText: "Conversión de PH en aceleración de 30 % a 45 %" },
                { LocalText: "B", RecommendedUpgrade: false } ] &&
            ratings.ArenaAugment(30)?.EffectAt(2) == "Conversión de PH en aceleración de 20 % a 30 %" &&
            affinity.Evaluate("Swain", 1332) is { Tier: Tier.A } &&
            affinity.Evaluate("Aatrox", 1332) is { Tier: Tier.B },
            "Pacto Ominoso valora el costo de vida según el campeón y Eureka Nv. 3 gana la oferta de Renata");

        var rellSpeed = Offer("Rell", [85, 1405, 305]);
        Check(rellSpeed[0] is { LocalText: "A" } &&
            rellSpeed[1] is { LocalText: "B", UsesWiki: true, LocalReason: "Dañar a un campeón con habilidades da un impulso breve de velocidad" } &&
            rellSpeed[2] is { LocalText: "A", UsesWiki: true } && rellSpeed[2]!.LocalReason!.Contains("resistencia a ralentizaciones"),
            "Rell distingue el impulso reactivo de Demonio Veloz del posicionamiento constante de Día de Pierna");
        var rellDamage = Offer("Rell", [85, 136, 26]);
        Check(rellDamage[2] is { LocalText: "B", UsesWiki: true, LocalReason: "Infliges más daño mientras seas más rápido que tu objetivo" } &&
            affinity.Evaluate("Ashe", 26) is { Tier: Tier.A } && affinity.Evaluate("Ahri", 1405) is { Tier: Tier.A },
            "No Parpadees depende de una ventaja de velocidad y los lanzadores aprovechan Demonio Veloz");

        var rellPrismatic = Offer("Rell", [85, 317, 224], [2, null, null]);
        Check(rellPrismatic[0] is { LocalText: "A", UpgradeLevel: 2 } &&
            rellPrismatic[1] is { LocalText: "Variable", LocalReason: "Cambia tus aumentos por prismáticos aleatorios" } &&
            rellPrismatic[2] is { LocalText: "Variable", LocalReason: "Eclosiona con 5 derribos, uno por rival y ronda" } &&
            rellPrismatic.All(badge => badge is { RecommendedAugment: false, RecommendedUpgrade: false }),
            "Pandora y Huevo muestran condiciones concretas sin promover una oferta de resultado incierto");
        var rellSoul = Offer("Rell", [62, 85, 4], [null, 3, null]);
        Check(rellSoul[0] is { LocalText: "Variable", LocalReason: "Las Almas de Dragón que recibes son aleatorias" } &&
            rellSoul[1] is { LocalText: "A", UpgradeLevel: 3 } &&
            rellSoul[2] is { LocalText: "C", UsesWiki: true, LocalReason: "Bloquea Tormenta Magnética, clave para agrupar rivales" },
            "Omnialma depende de las almas que salgan y De Vuelta a lo Básico penaliza la pérdida de la definitiva de Rell");

        var rellDealer = Offer("Rell", [349, 350, 347]);
        Check(rellDealer[0] is { LocalText: "B", UsesWiki: false } && rellDealer[0]!.LocalReason!.Contains("10 % de vida") &&
            rellDealer[1] is { LocalText: "A", UsesWiki: false } && rellDealer[1]!.LocalReason!.Contains("cada 10 s") &&
            rellDealer[2] is { LocalText: "Variable", LocalReason: "Aplica Heridas Graves, útiles contra la curación rival" },
            "las cartas del Mercader usan las mecánicas del cliente y el Putrificador depende de la curación rival");

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

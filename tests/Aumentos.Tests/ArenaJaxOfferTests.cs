using Aumentos;

internal static class ArenaJaxOfferTests
{
    internal static void Run(Ratings ratings)
    {
        var game = new GameContext(Mode.Arena, "Jax");
        var patch = ratings.Catalog.Patch;
        int?[] cards = new[]
        {
            "Sinfonía de Guerra", "De Más Largo Alcance", "Centro del Universo", "Goliat", "Bailarín de Tap"
        }.Select(name => ratings.Recognize(game.Mode, name)?.Id).ToArray();
        Check(cards.SequenceEqual([79, 115, 193, 41, 81]), "la lectura de los títulos reconoce los cinco aumentos de Jax");

        var opgg = new OpGgSnapshot(patch, game.Mode, game.Champion, DateTimeOffset.UtcNow,
            new() { [79] = new(PickPercent: 20m), [115] = new(PickPercent: 3m) });
        var ggez = new GgezArenaSnapshot(game.Champion, patch, patch, DateTimeOffset.UtcNow, 1000,
            new() { [79] = new(17m, 170), [115] = new(4m, 40) });
        var picks = CardComparison.ForOffer(ratings, game, [cards[0], cards[4], cards[1]], new bool[3], opgg, ggez: ggez);
        Check(picks.Select(card => card?.LocalText).SequenceEqual(["S+", "A", "A"]) &&
            picks[0] is { External.PickPercent: 20m, Arena.Percent: 17m, RecommendedAugment: true } &&
            picks[2] is { External.PickPercent: 3m, Arena.Percent: 4m },
            "los tiers locales para Sinfonía y Alcance conviven con sus tasas publicadas");

        var tied = CardComparison.ForOffer(ratings, game, [cards[3], cards[4], cards[2]], new bool[3], null);
        Check(tied.Select(card => card?.LocalText).SequenceEqual(["A", "A", "B"]) &&
            tied[0] is { RecommendedAugment: false, LocalReason.Length: > 0 } &&
            tied[1] is { RecommendedAugment: true, LocalReason.Length: > 0 } &&
            tied[2] is { LocalReason: "Daña a rivales que cruzan la órbita", External: null, Arena: null } &&
            ratings.Rate(Mode.Arena, "Rell", 193) == Tier.B,
            "Goliat y Bailarín conservan A, y Centro del Universo tiene estimación y motivo sin tasas");

        int?[] statsOffer = new[] { "A Bofetadas", "¡Estadísticas!", "El Brutalizador" }
            .Select(name => ratings.Recognize(game.Mode, name)?.Id).ToArray();
        var stats = CardComparison.ForOffer(ratings, game, statsOffer, new bool[3], null);
        Check(statsOffer.SequenceEqual([136, 226, 82]) &&
            stats.Select(card => card?.LocalText).SequenceEqual(["C", "A", "B"]) &&
            stats[1] is { RecommendedAugment: true, LocalReason: "Eliges mejoras en dos yunques de estadísticas" },
            "¡Estadísticas! y El Brutalizador tienen tier, y A Bofetadas mantiene su afinidad de Jax");

        int?[] attackOffer = new[] { "Hábil", "Servir Más Allá de la Muerte", "Tifón" }
            .Select(name => ratings.Recognize(game.Mode, name)?.Id).ToArray();
        var attack = CardComparison.ForOffer(ratings, game, attackOffer, new bool[3], null);
        Check(attackOffer.SequenceEqual([22, 120, 87]) &&
            attack.Select(card => card?.LocalText).SequenceEqual(["A", "B", "A"]) &&
            attack[0]?.RecommendedAugment == true &&
            attack[1]?.LocalReason == "Un derribo detiene la pérdida de vida" &&
            attack[2]?.RecommendedAugment == false,
            "Servir Más Allá de la Muerte explica su condición, y Tifón favorece los ataques de Jax");

        int?[] mixedOffer = new[] { "Hábil", "Toque Mortal", "Enhebrar la Aguja" }
            .Select(name => ratings.Recognize(game.Mode, name)?.Id).ToArray();
        var mixed = CardComparison.ForOffer(ratings, game, mixedOffer, new bool[3], null, upgradeLevels: [2, null, null]);
        Check(mixedOffer.SequenceEqual([22, 217, 84]) &&
            mixed.Select(card => card?.LocalText).SequenceEqual(["A", "A", "A"]) &&
            mixed[0]?.LocalReason == "Velocidad de ataque de 60 % a 80 %" &&
            mixed.All(card => card is { RecommendedAugment: false, RecommendedUpgrade: false, LocalReason.Length: > 0 }) &&
            ratings.Rate(Mode.Arena, "Gwen", 84) == Tier.A,
            "la penetración doble tiene tier para Jax y para otros campeones de perfil híbrido");

        int?[] defenseOffer = new[] { "No te Muevas", "Hábil", "Inmune al Dolor" }
            .Select(name => ratings.Recognize(game.Mode, name)?.Id).ToArray();
        var defense = CardComparison.ForOffer(ratings, game, defenseOffer, new bool[3], null, upgradeLevels: [null, 3, null]);
        Check(defenseOffer.SequenceEqual([207, 22, 251]) &&
            defense.Select(card => card?.LocalText).SequenceEqual(["C", "A", "B"]) &&
            defense[1] is { RecommendedUpgrade: true, LocalReason: "Velocidad de ataque de 80 % a 130 % y sin límite" } &&
            defense[2]?.LocalReason == "Recibes parte del daño a lo largo del tiempo",
            "la invisibilidad quieta y el daño diferido tienen tier en la oferta de Jax");

        int?[] repelOffer = new[] { "Repulsión", "Hábil", "¡Enciéndelos!" }
            .Select(name => ratings.Recognize(game.Mode, name)?.Id).ToArray();
        var repel = CardComparison.ForOffer(ratings, game, repelOffer, new bool[3], null, upgradeLevels: [null, 3, null]);
        Check(repelOffer.SequenceEqual([69, 22, 51]) &&
            repel.Select(card => card?.LocalText).SequenceEqual(["B", "A", "A"]) &&
            repel[1]?.RecommendedUpgrade == true && repel[2]?.RecommendedUpgrade == false &&
            repel[0]?.LocalReason == "Repele a rivales cercanos al perder vida",
            "Repulsión conserva su afinidad defensiva al compararla con dos mejoras de ataques");

        int?[] openingOffer = new[] { "Cerbero", "¡Enciéndelos!", "Cañón de Cristal" }
            .Select(name => ratings.Recognize(game.Mode, name)?.Id).ToArray();
        var opening = CardComparison.ForOffer(ratings, game, openingOffer, new bool[3], null, upgradeLevels: [null, 2, null]);
        Check(openingOffer.SequenceEqual([323, 51, 1325]) &&
            opening.Select(card => card?.LocalText).SequenceEqual(["A", "A", "C"]) &&
            opening[0]?.LocalReason == "Acelera los primeros ataques y recompensa tres impactos" &&
            opening[1] is { RecommendedUpgrade: true, LocalReason: "Fuegos artificiales de 4 a 5 · Daño de 90 % a 100 %" },
            "Cerbero tiene afinidad con Jax y se puede comparar con la mejora de sus ataques");

        int?[] criticalOffer = new[] { "Guantelete Deslumbrante", "¡Enciéndelos!", "Verdugo de Gigantes" }
            .Select(name => ratings.Recognize(game.Mode, name)?.Id).ToArray();
        var critical = CardComparison.ForOffer(ratings, game, criticalOffer, new bool[3], null, upgradeLevels: [null, 2, null]);
        Check(criticalOffer.SequenceEqual([48, 51, 195]) &&
            critical.Select(card => card?.LocalText).SequenceEqual(["B", "A", "B"]) &&
            critical[1] is { RecommendedUpgrade: true, LocalReason: "Fuegos artificiales de 4 a 5 · Daño de 90 % a 100 %" } &&
            critical[2]?.LocalReason == "El daño depende del tamaño del rival" &&
            ratings.Rate(Mode.Arena, "Lux", 48) == Tier.A,
            "Guantelete distingue a lanzadores de habilidades y Verdugo muestra su condición de tamaño");

        var partial = CardComparison.ForOffer(ratings, game, [41, 81, null], new bool[3], null);
        Check(partial.All(card => card?.RecommendedAugment == false),
            "espera la tercera carta antes de recomendar un aumento");
        Console.WriteLine("Tiers locales de Jax verificados con y sin datos de Arena.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

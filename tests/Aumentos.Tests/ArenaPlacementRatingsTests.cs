using Aumentos;

internal static class ArenaPlacementRatingsTests
{
    internal static void Run(Ratings ratings)
    {
        const string champion = "Renata";
        var catalog = ratings.Catalog;
        var patch = catalog.Patch;
        var cards = catalog.Augments.Where(card => card.Mode == nameof(Mode.Arena) &&
            ratings.Rate(Mode.Arena, champion, card.Id) is null && ratings.VariableReason(Mode.Arena, card.Id) is null)
            .Take(6).Select(card => card.Id).ToArray();
        var current = new GgezArenaSnapshot(champion, patch, patch, DateTimeOffset.UtcNow, 1200, new()
        {
            [cards[0]] = new GgezArenaPick(20, 240, 2.20m, 1),
            [cards[1]] = new GgezArenaPick(12.5m, 150, 3.20m, 1),
            [cards[2]] = new GgezArenaPick(12.5m, 150, 3.45m, 1),
            [cards[3]] = new GgezArenaPick(12.5m, 150, 3.75m, 1),
            [cards[4]] = new GgezArenaPick(2, 24, 1.30m, 1),
            [cards[5]] = new GgezArenaPick(12.5m, 150, 2.00m, 2)
        }, PlacementCount: 6, PlacementSchemaRead: true);
        var game = new GameContext(Mode.Arena, champion);
        var ranked = Offer(game, [cards[1], cards[0], cards[2]], current);
        Check(ranked[1] is { Local: CardComparison.Missing, DisplayedArenaPlacement: { Tier: Tier.S, Games: 240, Global: false },
                RecommendedAugment: true } && ranked.Count(card => card?.RecommendedAugment == true) == 1,
            "la mejor carta se distingue por resultados observados con muestra suficiente");
        Check(Offer(game, [cards[4], cards[5], cards[0]], current).Take(2).All(card => card?.DisplayedArenaPlacement is null),
            "se requiere muestra suficiente y otras cartas de la misma rareza");
        Check(Offer(game, [cards[0], cards[4], cards[2]], current).All(card => card?.RecommendedAugment != true) &&
            Offer(game with { Champion = "Lux" }, [cards[0]], current)[0]?.DisplayedArenaPlacement is null &&
            Offer(game, [cards[0]], current with { SourcePatch = LeaguePatch.Previous(patch)! })[0]?.DisplayedArenaPlacement is null,
            "no recomienda con una carta sin muestra ni presta el tier a otro campeón o parche");
        Check(Offer(game, [cards[0]], current with { PlacementCount = null })[0]?.DisplayedArenaPlacement is null,
            "las copias de solo frecuencia no producen tiers estadísticos");

        var global = current with { Champion = "" };
        Check(Offer(game, [cards[0], cards[1], cards[2]], null, global) is
            [{ DisplayedArenaPlacement.Global: true, RecommendedAugment: false }, ..] &&
            Offer(game, [cards[0]], current, global)[0]?.DisplayedArenaPlacement is { Global: false },
            "la muestra global queda identificada como respaldo y el dato de campeón tiene prioridad");

        var power = current with { Picks = new(current.Picks)
        {
            [198] = new GgezArenaPick(10, 120, 2.15m, 1),
            [215] = new GgezArenaPick(10, 120, 3.80m, 1),
            [94] = new GgezArenaPick(10, 120, 3.85m, 1)
        } };
        var configured = Offer(game, [198, 215, 94], power);
        Check(configured[1] is { RecommendedAugment: true, DisplayedArenaPlacement: null, ArenaPlacement: not null } &&
            configured.All(card => card?.Local == "A"),
            "el desempate contextual de Renata conserva su letra y permite consultar la muestra");
        Check(Offer(game, [198, 215, 94], power, levels: [2, null, null]).All(card => card?.ArenaPlacement is null),
            "las posiciones agregadas de GGEZ quedan fuera de una oferta de mejoras");
        Console.WriteLine("Tiers de Arena verificados con posición, muestra, rareza, alcance y desempate contextual.");

        CardComparison?[] Offer(GameContext context, int[] ids, GgezArenaSnapshot? sample,
            GgezArenaSnapshot? globalSample = null, int?[]? levels = null) =>
            CardComparison.ForOffer(ratings, context, ids.Select(id => (int?)id).ToArray(), new bool[ids.Length], null,
                ggez: sample, upgradeLevels: levels, ggezGlobal: globalSample);
    }

    private static void Check(bool success, string message)
    {
        if (!success)
            throw new InvalidOperationException(message);
    }
}

using System.Text.Json;
using Aumentos;

internal static class MayhemBuildTests
{
    internal static void Run(Ratings ratings, string assets)
    {
        using var data = JsonDocument.Parse("""
            [
              {"riotId":"self#000","rawChampionName":"game_character_displayname_Zilean","team":"ORDER","items":[{"itemID":1001},{"itemID":3802},{"itemID":2031}]},
              {"riotId":"ally#000","rawChampionName":"game_character_displayname_Rell","team":"ORDER","items":[{"itemID":3075}]},
              {"rawChampionName":"game_character_displayname_Ornn","team":"CHAOS","items":[{"itemID":2504}]},
              {"rawChampionName":"game_character_displayname_Jinx","team":"CHAOS","items":[{"itemID":3111}]},
              {"rawChampionName":"game_character_displayname_Soraka","team":"CHAOS","items":[]}
            ]
            """);
        var game = new GameContext(Mode.Caos, "Zilean");
        var match = LiveGame.ParseMatch(Mode.Caos, data.RootElement, "self#000");
        Check(match?.Game == game && match.Mayhem is { Opponents.Length: 3, Items.Length: 3 },
            "inventarios del jugador y del equipo rival separados de los aliados");
        Check(LiveGame.ParseMatch(Mode.Arena, data.RootElement, "self#000") == new LiveMatch(game with { Mode = Mode.Arena }),
            "Arena conserva solo su contexto de cartas");

        var patch = ratings.Catalog.Patch;
        var build = new OpGgBuildSnapshot(Mode.Caos, "Zilean", patch, patch, DateTimeOffset.UtcNow,
        [
            new(OpGgBuildSection.Boots, [[new(3020, "Botas del Hechicero")], [new(3158, "Botas Jonias de la Lucidez")]]),
            new(OpGgBuildSection.Core,
                [[new(6655, "Eco de Luden"), new(4645, "Lumbría"), new(3089, "Sombrero Mortífero de Rabadon")],
                 [new(6655, "Eco de Luden"), new(4646, "Descarga Tormentosa"), new(4645, "Lumbría")]])
        ]);
        var source = MayhemBuilds.Load(ratings.Catalog, assets);
        var initial = source.Recommend(game, match!.Mayhem, build);
        Check(initial?.Steps.Select(step => step.Item.Id).SequenceEqual([6655, 3020, 4645, 3089, 3135, 3165]) == true &&
            initial.Steps[4].Detail == "2 rivales con compras de resistencia mágica" && initial.Steps[5].Detail == "Curación · Soraka",
            "las compras de resistencia y la sanadora determinan los dos últimos objetos terminados");

        var flat = match.Mayhem! with { Opponents = [new("Jinx", []), new("Brand", []), new("Caitlyn", [])] };
        var withAssassin = flat with { Opponents = [..flat.Opponents, new("Zed", [])] };
        Check(source.Recommend(game, flat, build)?.Steps[4].Item.Id == 4646 &&
            source.Recommend(game, withAssassin, build)?.Steps[4].Item.Id == 3157,
            "si cambia la composición rival, cambian las recomendaciones");

        var owned = match.Mayhem! with { Items = [6655, 3158, 4645, 3089, 3137] };
        var remaining = source.Recommend(game, owned, build);
        Check(remaining?.Steps.Select(step => step.Item.Id).SequenceEqual([6655, 3158, 4645, 3089, 3137, 3165]) == true &&
            remaining.Steps.Take(5).All(step => step.Detail == "Comprado"),
            "respeta las botas y el objeto de penetración ya comprados y rellena el único hueco");
        var full = owned with { Items = [..owned.Items, 3165] };
        Check(source.Recommend(game, full, build)?.Steps.All(step => step.Detail == "Comprado") == true &&
            source.Recommend(game with { Mode = Mode.Arena }, match.Mayhem, build) is null,
            "reconoce el inventario completo y las builds situacionales no se activan en Arena");

        var marksman = game with { Champion = "Jinx" };
        var marksmanBuild = build with { Champion = "Jinx", Groups =
        [
            new(OpGgBuildSection.Boots, [[new(3006, "Grebas del Berserker")]]),
            new(OpGgBuildSection.Core, [[new(3032, "Flechas Salvajes de Yun Tal"), new(3031, "Filo del Infinito"),
                new(3094, "Cañón de Fuego Rápido")]])
        ] };
        var armor = new MayhemMatch([], [new("Ornn", [3075]), new("Caitlyn", [3047]), new("Soraka", [])]);
        var marksmanPlan = source.Recommend(marksman, armor, marksmanBuild);
        Check(marksmanPlan is not null && marksmanPlan.Steps[^2].Item.Id == 3036 &&
            marksmanPlan.Steps[^1].Item.Id == 3072,
            "las compras de armadura favorecen penetración física sin duplicar el grupo de Último Suspiro");
        Console.WriteLine("Build situacional de ARAM Caos verificada con inventario, composición y compras rivales.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

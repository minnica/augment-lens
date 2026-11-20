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
        var arena = LiveGame.ParseMatch(Mode.Arena, data.RootElement, "self#000");
        Check(arena?.Game == game with { Mode = Mode.Arena } && arena.Mayhem is null &&
            arena.Arena is { Items.Length: 3, Opponents.Length: 0 },
            "el respaldo de Arena conserva el inventario y no deduce rivales por ORDER o CHAOS");

        var patch = ratings.Catalog.Patch;
        var build = new OpGgBuildSnapshot(Mode.Caos, "Zilean", patch, patch, DateTimeOffset.UtcNow,
        [
            new(OpGgBuildSection.Start, [[new(1056, "Sortija de Doran"), new(2003, "Poción de Vida")], [new(1038, "Espadón")]]),
            new(OpGgBuildSection.Boots, [[new(3020, "Botas del Hechicero")], [new(3158, "Botas Jonias de la Lucidez")]]),
            new(OpGgBuildSection.Core,
                [[new(6655, "Eco de Luden"), new(4645, "Lumbría"), new(3089, "Sombrero Mortífero de Rabadon")],
                 [new(6655, "Eco de Luden"), new(4646, "Descarga Tormentosa"), new(4645, "Lumbría")]])
        ]);
        var source = MayhemBuilds.Load(ratings, assets);
        Check(!source.HasStartingPurchase(null, build) && !source.HasStartingPurchase(new([2003], []), build) &&
            source.HasStartingPurchase(new([1056], []), build) && source.HasStartingPurchase(new([1038], []), build) &&
            source.HasStartingPurchase(new([6655], []), null),
            "los objetos iniciales se ocultan tras una pieza recomendada o un objeto terminado");
        var initial = source.Recommend(game, match!.Mayhem, build);
        Check(initial?.Steps.Select(step => step.Item.Id).SequenceEqual([6655, 3020, 4645, 3089, 3135, 3165]) == true &&
            initial.Steps[4].Detail == "Contra 1 rival con objetos de resistencia mágica" &&
            initial.Steps[5].Detail == "Para reducir la curación de Soraka",
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

        var completed = new MayhemMatch([6655, 3158, 4645, 3089, 3102, 3157],
            [new("Ornn", [2504]), new("Brand", [3089]), new("Lux", [6655]), new("Veigar", [4645])]);
        var swap = source.Recommend(game, completed, build);
        Check(swap is { Swap.Outgoing.Id: 3157, Swap.Incoming.Id: 3135 } &&
            swap.Steps.Select(step => step.Item.Id).SequenceEqual(completed.Items) &&
            swap.Swap.Reason == "Contra 1 rival con objetos de resistencia mágica",
            "el mago gana penetración y conserva la defensa contra la presión mágica y su núcleo");
        Check(source.Recommend(game, completed, null)?.Swap is { Outgoing.Id: 3157, Incoming.Id: 3135 },
            "el cambio funciona con el catálogo local cuando falta OP.GG y hay un objeto situacional intercambiable");
        var heals = completed with { Opponents = [new("Aatrox", [3072]), new("Briar", [3153])] };
        Check(source.Recommend(game, heals, build)?.Swap is { Incoming.Id: 3165, Reason: "Para reducir la curación de Aatrox y Briar" },
            "la recomendación cambia cuando cambian las compras rivales con el mismo inventario");
        var replaced = completed with { Items = [6655, 3158, 4645, 3089, 3102, 3135] };
        Check(source.Recommend(game, replaced, build) is { Swap: null } &&
            source.Recommend(game, completed with { Opponents = [new("Ornn", [1033]), new("Lux", [1033])] }, build) is { Swap: null },
            "el cambio desaparece cuando ya cubres la amenaza y no se activa con componentes sueltos");

        var damageMix = flat with { Opponents = [..flat.Opponents, new("Zed", []), new("Akali", [6673])] };
        var updatedMix = damageMix with { Opponents = [..flat.Opponents, new("Zed", []), new("Akali", [3089])] };
        Check(source.EstimateEnemyDamage(damageMix) is { PhysicalPercent: 70, MagicPercent: 30 } &&
            source.EstimateEnemyDamage(updatedMix) is { PhysicalPercent: 60, MagicPercent: 40 } &&
            source.EstimateEnemyDamage(damageMix with { Opponents = [new("Ornn", []), new("Desconocido", [])] }) is null,
            "la proporción cambia con las compras rivales y queda pendiente sin perfiles de daño identificables");

        var leona = new GameContext(Mode.Caos, "Leona");
        var leonaBuild = build with { Champion = leona.Champion, Groups =
        [
            new(OpGgBuildSection.Boots, [[new(3111, "Botas de Mercurio")]]),
            new(OpGgBuildSection.Core,
                [[new(3084, "Corazón de Acero"), new(3121, "Invierno Nórdico"), new(2502, "Desesperación Eterna")],
                 [new(3084, "Corazón de Acero"), new(2502, "Desesperación Eterna"), new(3075, "Cota de Espinas")]])
        ] };
        var leonaMatch = new MayhemMatch([3084, 3111, 3119, 3067],
            [new("Ezreal", [1052]), new("Karma", [1058]), new("Lux", []), new("Nocturne", [6696]), new("Ashe", [])]);
        var apPurchases = leonaMatch with { Opponents = [new("Ezreal", [6655, 3118]), new("Karma", [3118, 6655]),
            new("Lux", [4646, 6655]), new("Nocturne", [6676, 6696]), new("Ashe", [3153, 3085])] };
        var reactive = source.Recommend(leona, apPurchases, leonaBuild);
        Check(source.Recommend(leona, leonaMatch, leonaBuild)?.Steps[3] is { Item.Id: 2502, Detail: "Núcleo de OP.GG" } &&
            reactive?.Steps[3] is { Item.Id: 2504, Detail: "Por el AP comprado por Ezreal y Karma" } &&
            reactive.Steps[4].Item.Id == 2502 && reactive.Steps.Take(3).Select(step => step.Item.Id).SequenceEqual([3084, 3111, 3119]) &&
            source.Recommend(leona, apPurchases with { Items = [..leonaMatch.Items, 2504] }, leonaBuild)?.Steps[4].Item.Id == 2502,
            "las compras confirmadas de AP adelantan resistencia mágica, respetan el inventario y dan paso al núcleo tras comprarla");

        var aurelion = new GameContext(Mode.Caos, "AurelionSol");
        var aurelionBuild = build with { Champion = aurelion.Champion, Groups =
        [
            new(OpGgBuildSection.Core, [[new(2503, "Antorcha de Fuegoscuro"), new(3116, "Cetro de Cristal de Rylai"),
                new(6653, "El Tormento de Liandry")]])
        ] };
        var mercurysOnly = new MayhemMatch([2503, 3116, 6653, 3020, 3916],
            [new("Quinn", [226668, 3006]), new("Rengar", [3111, 3031, 6699]), new("Sivir", [3032, 3006, 3031, 3153]),
             new("Ziggs", [6655, 4645, 3020]), new("Warwick", [226668, 3111, 1053])]);
        var resisted = mercurysOnly with { Opponents = [..mercurysOnly.Opponents.Take(4), new("Warwick", [226668, 3111, 2504])] };
        Check(source.Recommend(aurelion, mercurysOnly, aurelionBuild)?.Steps.All(step => step.Item.Id != 3135) == true &&
            source.Recommend(aurelion, resisted, aurelionBuild)?.Steps[4] is
                { Item.Id: 3135, Detail: "Contra 1 rival con objetos de resistencia mágica" },
            "dos pares de Mercurio no activan penetración porcentual, pero una compra fuerte de resistencia mágica sí");
        var noResistance = mercurysOnly with { Items = [2503, 3116, 6653, 3020, 3089], Opponents = [] };
        Check(source.Recommend(aurelion, noResistance, aurelionBuild)?.Steps[5].Item.Id == 4646,
            "Báculo del Vacío no rellena un espacio por defecto sin resistencia rival");

        var lightResistance = new MayhemMatch([2503, 3116, 6653, 3020, 4630],
            [new("Ziggs", [1033], 14), new("Sivir", [1033], 14)], new(20, .71775));
        Check(source.Recommend(aurelion, lightResistance with { MagicPenetration = null }, aurelionBuild)?.Steps[4].Item.Id == 3135 &&
            source.Recommend(aurelion, lightResistance, aurelionBuild)?.Steps[4] is { Item.Id: 3089 } &&
            source.Recommend(aurelion, lightResistance with { Opponents = [new("Ziggs", [1033], 14), new("Warwick", [2504], 14)] },
                aurelionBuild)?.Steps[4].Item.Id == 3135,
            "la penetración en vivo y la Joya Maldita evitan Báculo contra compras leves y lo conservan contra un rival muy resistente");
        var extraPenInSource = aurelionBuild with { Groups = [new(OpGgBuildSection.Core,
            [[new(2503, "Antorcha de Fuegoscuro"), new(3116, "Cetro de Cristal de Rylai"), new(3135, "Báculo del Vacío")]])] };
        Check(source.Recommend(aurelion, lightResistance, extraPenInSource)?.Steps.All(step => step.Item.Id != 3135) == true &&
            source.Recommend(aurelion, lightResistance with { Opponents = [new("Ziggs", [1033]), new("Sivir", [1033], 14)] },
                extraPenInSource)?.Steps.Any(step => step.Item.Id == 3135) == true,
            "la ruta publicada también se adapta cuando hay medidas completas y sigue disponible si falta el nivel rival");
        var fullWithPenetration = new MayhemMatch([2503, 3116, 6653, 3020, 3089, 4646],
            [new("Ziggs", [3050], 14), new("Sivir", [3050], 14)], new(35, .60));
        Check(source.Recommend(aurelion, fullWithPenetration with { MagicPenetration = null }, aurelionBuild)?.Swap?.Incoming.Id == 3135 &&
            source.Recommend(aurelion, fullWithPenetration, aurelionBuild) is { Swap: null },
            "la build completa tampoco propone cambiar a Báculo cuando la penetración acumulada ya cubre a los rivales");

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
        var mortalReminder = new MayhemMatch([3006, 3032, 3031, 3094, 3033, 3072], [new("Ornn", [3075])]);
        Check(source.Recommend(marksman, mortalReminder, marksmanBuild) is { Swap: null },
            "Recordatorio Mortal ya cubre la armadura y no se cambia por otro objeto de penetración");

        var karthus = new GameContext(Mode.Caos, "Karthus");
        OpGgBuildItem malignance = new(3118, "Maldad"), shadowflame = new(4645, "Lumbría"),
            liandry = new(6653, "El Tormento de Liandry"), deathcap = new(3089, "Sombrero Mortífero de Rabadon");
        var karthusBuild = build with { Champion = karthus.Champion, Groups =
            [new(OpGgBuildSection.Core, [[malignance, liandry, shadowflame], [malignance, shadowflame, deathcap]])] };
        var criticalMatch = new MayhemMatch([3118, 3089, 6653, 4645, 3135, 3165],
            [new("Karma", [3089, 2530, 124011, 6621, 6617, 3158]),
             new("Yunara", [3124, 3085, 3115, 3153, 3302, 6665]),
             new("Rengar", [6676, 3074, 3031, 3036, 126697, 3111]),
             new("Xayah", [3032, 3006, 6675, 3033, 3153, 6676]),
             new("Sejuani", [3084, 3075, 3742, 3110, 6665, 3143])]);
        Check(source.Recommend(karthus, criticalMatch, karthusBuild)?.Swap is
            { Outgoing.Id: 4645, Incoming.Id: 3157, Reason: "Contra los golpes críticos de Rengar y Xayah" } &&
            source.Recommend(karthus, criticalMatch with { Items = [3118, 3089, 6653, 3157, 3135, 3165] }, karthusBuild) is { Swap: null },
            "una build completa de Karthus añade defensa contra críticos y conserva Rabadon, penetración y heridas graves");
        Console.WriteLine("Build situacional de ARAM Caos verificada con inventario, composición y compras rivales.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

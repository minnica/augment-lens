using Aumentos;

internal static class ArenaBuildTests
{
    internal static void Run(Ratings ratings)
    {
        var planner = new ArenaBuilds(ratings);
        var cho = new GameContext(Mode.Arena, "Chogath");
        int[] equipped = [443083, 223111, 444644, 223084, 223068];
        var match = new ArenaMatch([..equipped, 222051, 220007, 223111],
            [new("Brand", [226655]), new("Lux", [223089, 224645]), new("Veigar", [223135])]);
        var patch = ratings.Catalog.Patch;
        var source = new OpGgBuildSnapshot(Mode.Arena, "Chogath", patch, LeaguePatch.Previous(patch) ?? patch,
            DateTimeOffset.UtcNow,
            [new(OpGgBuildSection.Core, [[new(223084, "Corazón de Acero"), new(223068, "Égida de Fuego Solar"), new(226665, "Jak'Sho")]]),
             new(OpGgBuildSection.Boots, [[new(223047, "Punteras de Acero Revestidas")]]),
             new(OpGgBuildSection.Prismatic, [[new(447119, "Vara del Rayo")]])]);
        var defense = planner.Recommend(cho, match, source);
        Check(defense is not null && defense.Steps.Select(step => step.Item.Id).SequenceEqual([..equipped, 222504]) &&
            defense.Steps.Take(5).All(step => step is { Detail: "Comprado", Owned: true }) && !defense.Steps[5].Owned &&
            defense.Steps[5].Item.Name == "Rukerno Kaénico" && defense.Steps[5].Detail == "Contra el daño mágico de Brand y Lux",
            "Cho'Gath conserva dos prismáticos y sus botas, y completa el sexto hueco contra daño mágico");

        var physical = match with { Opponents = [new("Jinx", [223031, 223032]), new("Caitlyn", [223031]), new("Graves", [223031])] };
        Check(planner.Recommend(cho, physical, source)?.Steps[5] is { Item.Id: 223143, Detail: "Contra los golpes críticos de Jinx" },
            "el sexto objeto cambia ante un rival con varios objetos de críticos");

        var damageMix = match with { Opponents = [new("Lux", [223031]), new("Jinx", [223031]), new("Brand", []), new("Caitlyn", [])] };
        var updatedMix = damageMix with { Opponents = [new("Lux", [223089]), ..damageMix.Opponents.Skip(1)] };
        Check(planner.EstimateEnemyDamage(damageMix) is { PhysicalPercent: 50, MagicPercent: 50 } &&
            planner.EstimateEnemyDamage(updatedMix) is { PhysicalPercent: 33, MagicPercent: 67 } &&
            planner.EstimateEnemyDamage(physical) is { PhysicalPercent: 100, MagicPercent: 0 } &&
            planner.EstimateEnemyDamage(match with { Opponents = [] }) is null,
            "el daño estimado usa las compras de hasta tres rivales confirmados y espera si aún no hay rivales");

        var jinx = new GameContext(Mode.Arena, "Jinx");
        var antiHeal = planner.Recommend(jinx, new ArenaMatch([223006, 223032, 223031, 223072],
            [new("Aatrox", [223074, 223075]), new("Ornn", [223110, 226665]), new("Lux", [])]), null);
        Check(antiHeal is { Steps.Length: 6 } && antiHeal.Steps[4] is { Item.Id: 223033, Detail: "Para reducir la curación de Aatrox" } &&
            antiHeal.Steps.All(step => step.Item.Id is not (223036 or 223302 or 226694)),
            "contra curación y armadura conserva un solo objeto de Último Suspiro");

        var brand = new GameContext(Mode.Arena, "Brand");
        var pen = planner.Recommend(brand, new ArenaMatch([223020, 226655, 223089, 224645],
            [new("Ornn", [222504, 226665]), new("Swain", [224401])]), null);
        Check(pen is { Steps.Length: 6 } && pen.Steps[4] is { Item.Id: 223165 } &&
            pen.Steps[5] is { Item.Id: 223135, Detail: "Contra la resistencia mágica de Ornn y Swain" },
            "el mago encuentra heridas graves y penetración frente a inventarios que las justifican");

        var fallback = planner.Recommend(cho, new ArenaMatch([], []), null);
        Check(fallback is { Steps.Length: 6 } &&
            fallback.Steps.All(step => step.Detail != "De la build de OP.GG" && step.Item.Id < 440000),
            "la ruta local de seis objetos sigue disponible sin fuente ni información de rivales");
        var hosted = planner.Recommend(cho, new ArenaMatch([], []), source);
        Check(hosted is { Steps.Length: 6 } && hosted.Steps.Take(4).All(step => step.Detail == "De la build de OP.GG") &&
            hosted.Steps.All(step => step.Item.Id != 447119), "se aprovecha el parche anterior sin recomendar comprar prismáticos");

        var mixedSource = source with { Groups = [
            new(OpGgBuildSection.Core, [[new(228009, "Multiherramienta"), new(224403, "La Espátula Dorada"), new(223003, "Báculo del Arcángel")]])] };
        var transformed = planner.Recommend(cho, new ArenaMatch([223040, 223111], []), mixedSource);
        Check(transformed is { Steps.Length: 6 } && transformed.Steps[0] is { Item.Id: 223040, Detail: "Comprado" } &&
            transformed.Steps[1] is { Item.Id: 223111, Detail: "Comprado" } &&
            transformed.Steps.Skip(2).All(step => step.Item.Id is not (223003 or 228009 or 224403 or 223111)),
            "el objeto transformado ocupa su hueco y excluye versiones duplicadas o bloqueadas");
        var allOwned = planner.Recommend(cho, match with { Items = [..equipped, 223075] }, source);
        Check(allOwned is { Steps.Length: 6 } && allOwned.Steps.All(step => step.Detail == "Comprado") &&
            planner.Recommend(cho with { Mode = Mode.Caos }, match, source) is null,
            "seis huecos llenos conservan el inventario y excluyen otros modos");
        var completed = new ArenaMatch([..equipped, 223110],
            [new("Brand", [226655, 223089]), new("Lux", [223089, 224645]), new("Veigar", [223135])]);
        Check(planner.Recommend(cho, completed, source) is { Swap.Outgoing.Id: 223110, Swap.Incoming.Id: 222504 } &&
            planner.Recommend(cho, completed with { Opponents = physical.Opponents }, source) is
                { Swap.Outgoing.Id: 223110, Swap.Incoming.Id: 223143 },
            "con seis objetos, el cambio conserva las botas, los prismáticos y el núcleo al pasar de daño mágico a críticos");
        Check(planner.Recommend(cho, completed with { Opponents = [] }, source) is { Swap: null } &&
            planner.Recommend(cho, completed with { InventoryKnown = false }, source) is { Swap: null },
            "el cambio necesita inventario conocido y compras de rivales confirmados");

        var armorAndHealing = new ArenaMatch([223006, 223032, 223031, 223072, 223036, 223046],
            [new("Aatrox", [223074]), new("Ornn", [223110, 226665])]);
        Check(planner.Recommend(jinx, armorAndHealing, null) is { Swap.Outgoing.Id: 223036, Swap.Incoming.Id: 223033 },
            "un cambio de Último Suspiro añade heridas graves y conserva la penetración de armadura");
        FollowSuggestedSwap(planner, brand, patch);
        Console.WriteLine("Ruta de Arena verificada con los cinco objetos de Cho'Gath, fuente anterior y counters por rival.");
    }

    private static void FollowSuggestedSwap(ArenaBuilds planner, GameContext brand, string patch)
    {
        const int morello = 223165, voidStaff = 223135;
        int[] kept = [223020, 443083, 226655, 224645, 223089];
        var source = new OpGgBuildSnapshot(Mode.Arena, brand.Champion, patch, patch, DateTimeOffset.UtcNow,
            [new(OpGgBuildSection.Core, [[new(226655, "Tormento de Liandry"), new(224645, "Llamasombría"),
                new(223089, "Sombrero Mortífero de Rabadon"), new(morello, "Morellonomicón")]])]);
        var full = new ArenaMatch([..kept, morello], [new("Ornn", [222504, 226665]), new("Swain", [224401])]);
        var afterSale = new ArenaMatch([..kept.Reverse()], []);
        var suggested = planner.Recommend(brand, full, source);
        Check(suggested?.Swap is { Outgoing.Id: morello, Incoming.Id: voidStaff } && Next() == morello,
            "sin el cambio pendiente la ruta de OP.GG vuelve a recomendar el Morellonomicón vendido");

        planner.RememberSuggestedSwap(brand, suggested);
        planner.ObserveInventory(brand, full with { Opponents = [] });
        planner.ObserveInventory(brand, afterSale);
        var pending = planner.Recommend(brand, afterSale, source);
        Check(pending is { Swap: null } && pending.Steps.Count(step => step.Owned) == 5 &&
            pending.Steps[^1] is { Item.Id: voidStaff, Owned: false } next && next.Detail == suggested!.Swap!.Reason,
            "tras vender el Morellonomicón, el Báculo del Vacío pasa a siguiente compra aunque falten datos de rivales");
        var unknown = afterSale with { InventoryKnown = false, Items = [] };
        planner.ObserveInventory(brand, unknown);
        Check(planner.Recommend(brand, unknown, source)?.Steps[^1] is { Item.Id: voidStaff, Owned: false },
            "una lectura incompleta del inventario mantiene el cambio pendiente");

        var bought = afterSale with { Items = [..kept, voidStaff] };
        planner.ObserveInventory(brand, bought);
        Check(planner.Recommend(brand, bought, source) is { Swap: null } completed && completed.Steps.All(step => step.Owned) &&
            Next() == morello, "comprar el reemplazo completa la ruta y deja de reservarlo");

        planner.RememberSuggestedSwap(brand, suggested);
        planner.ObserveInventory(brand, afterSale);
        planner.ObserveInventory(brand, afterSale with { Items = [..kept, 223157] });
        Check(Next() == morello, "llenar el hueco con otra compra cancela el cambio sugerido");

        planner.RememberSuggestedSwap(brand, suggested);
        planner.ObserveInventory(brand, full with { Items = [..kept.Skip(1), morello] });
        planner.ObserveInventory(brand, afterSale);
        Check(Next() == morello, "vender otro objeto primero invalida el cambio original");

        planner.RememberSuggestedSwap(brand, suggested);
        planner.ObserveInventory(brand, afterSale);
        planner.ResetSwap();
        Check(Next() == morello, "una partida nueva descarta el cambio pendiente");

        int? Next() => planner.Recommend(brand, afterSale, source)?.Steps.FirstOrDefault(step => !step.Owned)?.Item.Id;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

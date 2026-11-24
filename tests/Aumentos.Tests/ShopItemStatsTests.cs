using System.IO;
using System.Text.Json;
using System.Windows;
using Aumentos;

internal static class ShopItemStatsTests
{
    internal static void Run(Catalog catalog)
    {
        var game = new GameContext(Mode.Arena, "Jax");
        var arenaHtml = $"""
            <title>Jax Arena parche {catalog.Patch}</title><link rel="canonical" href="{OpGgBuildPage.Address(game, "items")}">
            <table><thead><tr><th></th><th>% victorias</th><th>Tasa de selección</th></tr></thead><tbody>
            <tr><td>{Icon(223153)}</td><td>52,75 %</td><td><strong>44.29%</strong><span>8,076 partidas</span></td></tr>
            <tr><td>{Icon(223078)}{Icon(226333)}</td><td>60%</td><td>23%</td></tr>
            <tr><td>{Icon(223111)}</td><td>120%</td><td>8%</td></tr>
            </tbody></table>
            """;
        var arena = OpGgBuildPage.ReadArenaItems(arenaHtml, game, catalog.Patch);
        Check(arena.Items.Count == 1 && arena.Items[223153] == new ShopItemRate(44.29m, 52.75m),
            "Arena toma porcentajes de filas de un único objeto y respeta el encabezado de cada métrica");

        var mayhemHtml = $"""
            <title>ARAM: Mayhem — item win rates {catalog.Patch} · HexMayhem</title>
            <table><tbody><tr><td>1</td><td><img src="/icons/items/6655_ludensecho.webp"></td>
            <td><b>52.1<!-- -->%</b></td><td>40.9<!-- -->%</td><td>17K</td><td>A</td></tr></tbody></table>
            """;
        var mayhem = ShopItemStats.ReadMayhemItems(mayhemHtml, catalog.Patch);
        Check(mayhem.Items[6655] == new ShopItemRate(40.9m, 52.1m), "Caos lee tasas globales por ID de objeto");
        try
        {
            ShopItemStats.ReadMayhemItems(mayhemHtml.Replace(catalog.Patch, "15.01"), catalog.Patch);
            throw new InvalidOperationException("se aceptó un parche de otra temporada");
        }
        catch (InvalidDataException) { }

        using var players = JsonDocument.Parse("""
            [{"riotId":"Yo#MX","team":"ORDER","rawChampionName":"game_character_displayname_Jax","items":[],"augments":["EtherealWeapon"]},
             {"riotId":"Rival#MX","team":"CHAOS","rawChampionName":"game_character_displayname_Garen","items":[],"augments":["JeweledGauntlet"]}]
            """);
        var owned = LiveGame.ParseMatch(Mode.Caos, players.RootElement, "Yo#MX")?.OwnedAugments;
        Check(owned is ["EtherealWeapon"], "se leen los aumentos del jugador actual");
        var plan = new MayhemBuildPlan([new(new(3078, "Fuerza de la Trinidad")), new(new(3153, "Espada del Rey Arruinado"))]);
        var affinity = new ShopAugmentAffinity(catalog);
        var improved = affinity.Prioritize(Mode.Caos, owned, plan);
        Check(improved?.Steps[0].Item.Id == 3153 && improved.Steps[0].Detail?.StartsWith("Sinergia con ") == true,
            "un aumento confirmado puede reordenar compras compatibles");
        var counter = plan with { Steps = [plan.Steps[0] with { Detail = "Contra el rival con ventaja" }, plan.Steps[1]] };
        Check(ReferenceEquals(affinity.Prioritize(Mode.Caos, owned, counter), counter) &&
            ReferenceEquals(affinity.Prioritize(Mode.Caos, null, plan), plan),
            "el reordenamiento conserva los counters prioritarios y la ruta si faltan aumentos confirmados");
        var held = new HeldAugments(catalog);
        var bounds = new Int32Rect(10, 30, 1600, 900);
        var layout = new Calibration(1600, 900, new(.45, .77, .1, .04),
            [new(.25, .4, .14, .04), new(.43, .4, .14, .04), new(.61, .4, .14, .04)], new byte[720]);
        var mayhemGame = new GameContext(Mode.Caos, "Jax");
        held.ObserveOffer(mayhemGame, bounds, layout, [1029, 1048, 1087]);
        held.ObservePointer(true, 810, 695, 100);
        held.CloseOffer(300);
        Check(held.Keys.Length == 0, "un clic en el área de cambio queda fuera de la carta");
        held.ObserveOffer(mayhemGame, bounds, layout, [1029, 1048, 1087]);
        held.ObservePointer(true, 785, 410, 800);
        held.ObserveOffer(mayhemGame, bounds, layout, [1029, null, 1087]);
        held.CloseOffer(910);
        Check(held.Keys.Length == 0, "un cambio de la oferta cancela una selección pendiente");
        held.ObserveOffer(mayhemGame, bounds, layout, [1029, 1048, 1087]);
        held.ObservePointer(true, 523, 420, 1000);
        Check(held.Keys.Length == 0, "el clic no es suficiente mientras la oferta siga abierta");
        held.CloseOffer(1330);
        Check(held.Keys is ["1029"] && affinity.Prioritize(Mode.Caos, held.Keys, plan)?.Steps[0].Item.Id == 3153,
            "una elección confirmada por la captura también influye en la compra");
        Console.WriteLine("Porcentajes por objeto y prioridad por aumentos confirmados verificados.");

        string Icon(int id) => $"<img src=\"https://opgg-static.akamaized.net/meta/images/lol/{catalog.Patch}.1/item/{id}.png\" alt=\"Objeto {id}\">";
    }

    private static void Check(bool success, string message)
    {
        if (!success) throw new InvalidOperationException(message);
    }
}

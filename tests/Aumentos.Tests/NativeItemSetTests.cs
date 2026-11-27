using System.IO;
using System.Text.Json;
using Aumentos;
using static Aumentos.AramKitItemSets;

internal static class NativeItemSetTests
{
    internal static void Run(Catalog catalog, string assets)
    {
        var items = new Dictionary<int, ShopItem>();
        foreach (int id in new[] { 1028, 2031, 3084, 2502, 3083, 3118, 4646, 3089, 3003, 3157, 3020, 3111 })
            items[id] = new(new() { ["12"] = true }, new(true), null, false, 0,
                id is 3020 or 3111 ? ["Boots"] : [], null);
        items[3040] = items[3003] with { Gold = new(false), SpecialRecipe = 3003 };
        var tank = new Route(1500, Ids(3084, 2502, 3083), [new(120, Ids(1028, 2031, 2031))],
            [new(1300, new(0)), new(70, new(3111))], []);
        var ap = new Route(1000, Ids(3118, 4646, 3089), [], [new(500, new(3020))],
            [new(300, new(3040)), new(100, new(3157)), new(200, new(3118))]);
        Archetype Style(string key, double share, params Route[] routes) => new(key, share, [new(routes)]);
        var variants = Variants([Style("tank", 0.45, tank), Style("ap", 0.35, ap,
                ap with { SampleCount = 1500, PurchaseOrder = Ids(999999, 4646, 3089) }),
            Style("bruiser", 0.1, tank with { PurchaseOrder = Ids(2502, 3084, 3083) }),
            Style("crit", 0.08, ap with { SampleCount = 199 }), Style("utility", 0.01, ap)], items, "Malphite");
        Check(variants.Select(v => v.Style).SequenceEqual(["tank", "ap"]) && variants[0].Boots.SequenceEqual([3111]) &&
            variants[1].Later.SequenceEqual([3003, 3157]), "rutas distintas con muestra, botas propias y evoluciones comprables");

        foreach (var mode in Enum.GetValues<Mode>())
        {
            var bundled = NativeItemSets.Load(Path.Combine(assets, ShopItemSetCatalog.FileName(mode)), catalog, mode);
            Check(bundled is not null, $"catálogo de conjuntos de {mode} incluido y válido");
            if (catalog.Patch == "16.23")
                foreach (var key in new[] { "Malphite", "Maokai" })
                    Check(bundled!.Champions.Single(c => c.Key == key).Variants.Select(v => v.Style).ToHashSet()
                        .IsSupersetOf(["ap", "tank"]), $"AP y tanque observados para {key} en {mode}");
        }
        var stock = new LolArenaItemSets.Stock(new() { ["Heartsteel"] = 223084, ["Cruelty"] = 447109,
            ["Jak'Sho, The Protean"] = 226665 }, (catalog.ArenaLegendaries ?? []).Concat(catalog.ArenaEquipment ?? [])
                .ToDictionary(item => item.Id), [447109]);
        var page = new { championId = 54, queue = 1750, styles = new[] { new { style = "tank", games = 220, graded = true },
            new { style = "ap", games = 99, graded = true } }, initialBuild = new { core_items = new[] { new { id = 223065, games = 21 },
            new { id = 223089, games = 80 } } }, boots = new[] { new { id = 223111, games = 22 } } };
        var component = "2:" + JsonSerializer.Serialize(new object?[] { "$", "div", null, page }) + "\n";
        var html = $"""<p>Patch {LeaguePatch.Public(catalog.Patch)} · updated 2026-11-25 07:02 UTC</p>"""
            + """<article><h3>Malphite Arena Tank build</h3><p>3.4 avg · 220 games<!-- --> · Core items: Heartsteel, Jak&#x27;Sho, The Protean, Cruelty<!-- --> · Augments: Goliath</p></article>"""
            + """<article><h3>Malphite Arena AP build</h3><p>3.7 avg · 99 games · Core items: Heartsteel, Jak'Sho, The Protean</p></article>"""
            + "<script>self.__next_f.push(" + JsonSerializer.Serialize(new object[] { 1, component }) + ")</script>";
        var arenaVariant = LolArenaItemSets.Read(html, catalog.Patch, 54, stock).Single();
        Check(arenaVariant.Style == "tank" && arenaVariant.Core.SequenceEqual([223084, 226665]) &&
            arenaVariant.Later.SequenceEqual([223065]) && arenaVariant.Boots.SequenceEqual([223111]) &&
            arenaVariant.Prismatics!.SequenceEqual([447109]) && LolArenaItemSets.Read(html, catalog.Patch, 57, stock).Length == 0 &&
            LolArenaItemSets.Read(html, "15.1", 54, stock).Length == 0, "núcleo de Arena, nombres con comas, afinidad, muestra, campeón y parche");
        var general = new LolArenaItemSets.Page(54, 1750, [], new(200, false, [new(223084, 110), new(223065, 85)], [], []), []);
        Check(LolArenaItemSets.Variants("", general, stock) is [{ Style: "general" }] &&
            LolArenaItemSets.Variants("", general with { InitialBuild = general.InitialBuild! with { Limited = true } }, stock).Length == 0,
            "el conjunto general requiere compras y una muestra suficiente cuando faltan estilos");

        var install = Directory.CreateTempSubdirectory("Aumentos-Conjuntos-").FullName;
        try
        {
            ChampionItemSets champion = new("Malphite", 54, variants);
            ShopItemSetCatalog data = new(Mode.Caos, catalog.Patch, "data/" + catalog.Patch + "-test", [champion]);
            ShopItemSetCatalog arena = new(Mode.Arena, catalog.Patch, $"arena/{catalog.Patch}/1750",
                [champion with { Variants = [arenaVariant!] }]);
            var folder = Directory.CreateDirectory(Path.Combine(install, "Config", "Champions", champion.Key, "Recommended")).FullName;
            var personal = Path.Combine(folder, "Mi conjunto.json");
            var copied = Path.Combine(folder, "AumentosLoL-KIWI-personal.json");
            foreach (var filename in new[] { personal, copied }) File.WriteAllText(filename, """{"uid":"personal"}""");
            var account = Path.Combine(install, "Config", "ItemSets.json");
            File.WriteAllText(account, """{"itemSets":["personal"]}""");
            Check(NativeItemSets.Synchronize(install, catalog, data, default) == 2, "dos conjuntos independientes");
            string tankFile = Path.Combine(folder, "AumentosLoL-KIWI-tank.json");
            string apFile = Path.Combine(folder, "AumentosLoL-KIWI-ap.json");
            using var tankJson = JsonDocument.Parse(File.ReadAllText(tankFile));
            using var apJson = JsonDocument.Parse(File.ReadAllText(apFile));
            var result = tankJson.RootElement;
            var blocks = result.GetProperty("blocks");
            Check(result.GetProperty("associatedChampions")[0].GetInt32() == 54 && result.GetProperty("associatedMaps")[0].GetInt32() == 12 &&
                result.GetProperty("mode").ValueEquals("KIWI") && result.GetProperty("title").GetString()!.Contains("Tanque") &&
                blocks[0].GetProperty("items")[1].GetProperty("count").GetInt32() == 2 &&
                blocks[1].GetProperty("items")[0].GetProperty("id").ValueEquals("3084") &&
                result.GetProperty("uid").GetString() != apJson.RootElement.GetProperty("uid").GetString(),
                "formato nativo, filtro de campeón y modo, cantidad y orden de compra");
            var written = File.GetLastWriteTimeUtc(apFile);
            NativeItemSets.Synchronize(install, catalog, arena, default);
            var arenaFile = Path.Combine(folder, "AumentosLoL-CHERRY-tank.json");
            using var arenaSet = JsonDocument.Parse(File.ReadAllText(arenaFile));
            Check(arenaSet.RootElement.GetProperty("mode").ValueEquals("CHERRY") &&
                arenaSet.RootElement.GetProperty("associatedMaps")[0].GetInt32() == 30 &&
                arenaSet.RootElement.GetProperty("uid").GetString() != result.GetProperty("uid").GetString(),
                "Arena usa su mapa y su propio identificador para el mismo campeón y estilo");
            NativeItemSets.Synchronize(install, catalog, data, default);
            Check(File.GetLastWriteTimeUtc(apFile) == written, "reinstalar la misma versión conserva el archivo");
            NativeItemSets.Synchronize(install, catalog, data with { Champions = [champion with { Variants = [variants[1]] }] }, default);
            Check(!File.Exists(tankFile) && File.Exists(apFile), "retira solo la variante obsoleta");
            NativeItemSets.RemoveObsolete(install, Mode.Caos);
            Check(File.Exists(arenaFile), "limpiar Caos conserva Arena");
            NativeItemSets.RemoveObsolete(install, Mode.Arena);
            Check(!File.Exists(apFile) && !File.Exists(arenaFile) && File.ReadAllText(personal) == File.ReadAllText(copied) &&
                File.ReadAllText(account) == """{"itemSets":["personal"]}""", "la desinstalación conserva los conjuntos ajenos");
        }
        finally { Directory.Delete(install, recursive: true); }
        Console.WriteLine("Conjuntos nativos de ARAM Caos y Arena verificados.");

        static ItemId[] Ids(params int[] ids) => ids.Select(id => new ItemId(id)).ToArray();
        static void Check(bool ok, string message)
        {
            if (!ok) throw new InvalidOperationException(message);
        }
    }
}

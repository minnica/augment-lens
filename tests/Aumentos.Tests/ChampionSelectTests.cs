using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using Aumentos;

internal static class ChampionSelectTests
{
    internal static void Run()
    {
        using var lobby = JsonDocument.Parse("""{"phase":"ChampSelect","gameData":{"queue":{"id":2400}}}""");
        using var started = JsonDocument.Parse("""{"phase":"InProgress","gameData":{"queue":{"id":2400}}}""");
        using var normalAram = JsonDocument.Parse("""{"phase":"ChampSelect","gameData":{"queue":{"id":450}}}""");
        using var arena = JsonDocument.Parse("""{"phase":"ChampSelect","gameData":{"queue":{"id":1710}}}""");
        using var otherArena = JsonDocument.Parse("""{"phase":"ChampSelect","gameData":{"queue":{"gameMode":"CHERRY"}}}""");
        Check(ChampionSelectClient.SelectMode(lobby.RootElement) == Mode.Caos &&
            ChampionSelectClient.SelectMode(started.RootElement) is null &&
            ChampionSelectClient.SelectMode(normalAram.RootElement) is null &&
            ChampionSelectClient.SelectMode(arena.RootElement) == Mode.Arena &&
            ChampionSelectClient.SelectMode(otherArena.RootElement) == Mode.Arena, "fase y colas de selección");

        using var selection = JsonDocument.Parse("""
            {"localPlayerCellId":4,"myTeam":[{"cellId":0,"championId":103},
                {"cellId":4,"championId":85,"spell1Id":32,"spell2Id":4}]}
            """);
        using var swapped = JsonDocument.Parse("""
            {"localPlayerCellId":4,"myTeam":[{"cellId":0,"championId":85},
                {"cellId":4,"championId":103,"spell1Id":6,"spell2Id":4}]}
            """);
        using var empty = JsonDocument.Parse("""
            {"localPlayerCellId":4,"myTeam":[{"cellId":0,"championId":85},{"cellId":4,"championId":0}]}
            """);
        Check(ChampionSelectClient.OwnPick(selection.RootElement) == (85, 32, 4) &&
            ChampionSelectClient.OwnPick(swapped.RootElement) == (103, 6, 4) &&
            ChampionSelectClient.OwnPick(empty.RootElement) is null, "campeón y hechizos de la propia celda al hacer un intercambio");

        var page = ReadSpells();
        var main = new OpGgSpellPair("Destello", "Marca");
        Check(page.Spells is { Length: 2 } spells && spells.SequenceEqual([main, new("Destello", "Fantasma")]) &&
            SpellOverlay.Matches(main, new("Kennen", "Marca", "Destello")) &&
            !SpellOverlay.Matches(main, new("Kennen", "Fantasma", "Destello")),
            "combinaciones de OP.GG sin duplicados y comparación independiente de D y F");
        Console.WriteLine("Selección de campeón y hechizos de OP.GG verificados.");
    }

    internal static void Smoke()
    {
        var overlay = new SpellOverlay();
        try
        {
            var bounds = new Int32Rect(10, 10, 1040, 720);
            overlay.Render(new SpellDisplay(bounds, 0, "Kennen", new("Kennen", "Marca", "Destello"), new(ReadSpells(), "Prueba")));
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var handle = new WindowInteropHelper(overlay).Handle;
            var placement = Native.Bounds(handle);
            var panel = (StackPanel)((Border)((Viewbox)overlay.Content).Child).Child;
            Check(Native.IsWindowVisible(handle) && placement.X > bounds.X + bounds.Width / 2 &&
                placement.X + placement.Width <= bounds.X + bounds.Width && placement.Y >= bounds.Y &&
                placement.Y + placement.Height <= bounds.Y + bounds.Height &&
                (Native.GetWindowLongW(handle, -20) & 0x08000020) == 0x08000020 &&
                panel.Children.OfType<TextBlock>().Select(row => row.Text).SequenceEqual(
                    ["Kennen · Hechizos de OP.GG", "Destello + Marca", "Ya tienes estos hechizos",
                     "Alternativa · Destello + Fantasma", "Parche anterior 26.22"]),
                "panel dentro del cliente, sin interceptar clics y con las dos combinaciones de OP.GG");
            overlay.Render(new SpellDisplay(bounds, 0, "Aurelion Sol", new("AurelionSol", "Marca", "Destello"),
                new(ReadSpells() with { Champion = "AurelionSol" }, "Prueba"), "A", "OP.GG · Parche 26.23"));
            var header = (DockPanel)panel.Children[0];
            Check(((TextBlock)((Border)header.Children[0]).Child).Text == "A" &&
                ((StackPanel)header.Children[1]).Children.OfType<TextBlock>().Select(line => line.Text)
                    .SequenceEqual(["Aurelion Sol", "OP.GG · Parche 26.23"]) &&
                panel.Children.OfType<TextBlock>().Any(line => line.Text == "Hechizos · Parche anterior 26.22"),
                "el campeón elegido conserva su tier junto a los hechizos con parches independientes");
            overlay.Render(null);
            Check(!Native.IsWindowVisible(handle), "el panel se retira al salir de selección");
            Console.WriteLine("Panel de hechizos en WPF verificado.");
        }
        finally { overlay.Close(); }
    }

    internal static void ArenaSmoke(Catalog catalog)
    {
        var overlay = new ArenaChampionOverlay { Left = -30000, Top = -30000 };
        try
        {
            var bounds = new Int32Rect(-30000, -30000, 1040, 585);
            var data = new ArenaChampionTierList(catalog.Patch, catalog.Patch, DateTimeOffset.UtcNow, 500, new()
            {
                ["Lillia"] = new("Lillia", "S", 59, 100),
                ["Jax"] = new("Jax", "A", 53, 100),
                ["Rell"] = new("Rell", "S", 58, 100)
            });
            var otherChampions = catalog.Champions.Where(champion => champion.Key is not ("Aurora" or "Lillia" or "Jax" or "Rell"))
                .Take(13).ToArray();
            for (int i = 0; i < otherChampions.Length; i++)
            {
                var key = otherChampions[i].Key;
                data.Champions[key] = new(key, "B", 50 + i, 100);
            }
            var source = new ArenaChampionTierView(data, "Prueba");
            var offers = catalog.Champions.Where(champion => champion.Key is "Jax" or "Rell" or "Aurora")
                .Select(champion => new ArenaDraftOffer(champion, Int32Rect.Empty)).ToArray();
            var options = ArenaChampionDisplay.Create(bounds, 0, catalog, source, offers);
            Check(options.VisibleOptions && options.Rows.Select(row => row.Rating?.Champion).SequenceEqual(["Rell", "Jax", null]) &&
                options.Rows[^1].Champion == "Aurora", "ordena las opciones visibles y conserva a quien carece de tier");
            var global = ArenaChampionDisplay.Create(bounds, 0, catalog, source, null);
            var ranked = new[] { "Lillia", "Rell", "Jax" }.Concat(otherChampions.Skip(1).Reverse().Select(champion => champion.Key));
            Check(!global.VisibleOptions && global.Rows.Length == 15 &&
                global.Rows.Select(row => row.Rating?.Champion).SequenceEqual(ranked),
                "el ranking global toma hasta quince campeones por tier y resultados");

            overlay.Render(options);
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var handle = new WindowInteropHelper(overlay).Handle;
            var placement = Native.Bounds(handle);
            var panel = (StackPanel)((Border)((Viewbox)overlay.Content).Child).Child;
            Check(Native.IsWindowVisible(handle) && placement.X >= bounds.X && placement.Y >= bounds.Y &&
                placement.X + placement.Width <= bounds.X + bounds.Width &&
                placement.Y + placement.Height <= bounds.Y + bounds.Height &&
                (Native.GetWindowLongW(handle, -20) & 0x08000020) == 0x08000020 &&
                panel.Children.OfType<TextBlock>().Select(row => row.Text).SequenceEqual(
                    ["Arena · Opciones visibles", $"GGEZ.GG · Parche {LeaguePatch.Public(catalog.Patch)}",
                     "1. Rell · S · 58 % top 3", "2. Jax · A · 53 % top 3", "Aurora · Sin tier"]),
                "muestra las opciones de Arena dentro del cliente con fuente y parche");

            overlay.Render(global);
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            placement = Native.Bounds(handle);
            var header = (DockPanel)panel.Children[0];
            var table = (Grid)panel.Children[1];
            var columns = table.Children.OfType<Grid>().ToArray();
            Check(placement.X >= bounds.X + bounds.Width * 0.57 &&
                placement.X + placement.Width <= bounds.X + bounds.Width &&
                placement.Y + placement.Height <= bounds.Y + bounds.Height * 0.90 &&
                header.Children.OfType<TextBlock>().Select(text => text.Text).SequenceEqual(
                    [$"GGEZ.GG · Parche {LeaguePatch.Public(catalog.Patch)}", "Arena · Top 15 general"]) &&
                columns.Length == 2 && Ranks(columns[0]).SequenceEqual(Enumerable.Range(1, 8)) &&
                Ranks(columns[1]).SequenceEqual(Enumerable.Range(9, 7)),
                "muestra las quince posiciones en dos columnas dentro de la zona lateral");

            var narrow = new Int32Rect(bounds.X, bounds.Y, 600, 400);
            overlay.Render(global with { Bounds = narrow });
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            placement = Native.Bounds(handle);
            Check(placement.X >= narrow.X && placement.X + placement.Width <= narrow.X + narrow.Width &&
                placement.Y >= narrow.Y && placement.Y + placement.Height <= narrow.Y + narrow.Height * 0.79,
                "el ranking deja espacio para Fijar en clientes estrechos");

            overlay.Render(null);
            Check(!Native.IsWindowVisible(handle), "se oculta el panel de Arena al salir de selección");
            Console.WriteLine("Panel de campeones de Arena en WPF verificado.");

            static IEnumerable<int> Ranks(Grid column) => column.Children.OfType<TextBlock>()
                .Where(cell => Grid.GetColumn(cell) == 0).Select(cell => int.Parse(cell.Text));
        }
        finally { overlay.Close(); }
    }

    private static OpGgBuildSnapshot ReadSpells()
    {
        var game = new GameContext(Mode.Caos, "Kennen");
        var flash = Spell("SummonerFlash", "Destello");
        var mark = Spell("SummonerSnowball", "Marca");
        var ghost = Spell("SummonerHaste", "Fantasma");
        return OpGgBuildPage.Read($"""
            <title>Kennen ARAM: Caos, parche 16.22</title><link rel="canonical" href="{OpGgBuildPage.Address(game)}">
            <table><caption>SummonerSpells Table</caption><thead><tr><th>Hechizos de invocador</th></tr></thead>
            <tbody>
            <tr><td>{flash}{mark}</td></tr>
            <tr><td>{mark}{flash}</td></tr>
            <tr><td>{flash}{ghost}</td></tr>
            </tbody></table>
            """, game, "16.23");

        static string Spell(string key, string name) =>
            $"""<img alt="{name}" src="https://opgg-static.akamaized.net/meta/images/lol/16.22.1/spell/{key}.png?w=64">""";
    }

    private static void Check(bool success, string message)
    {
        if (!success) throw new InvalidOperationException($"Falló: {message}");
    }
}

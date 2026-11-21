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
        using var otherMayhem = JsonDocument.Parse("""{"phase":"ChampSelect","gameData":{"gameMode":"KIWI"}}""");
        using var arena = JsonDocument.Parse("""{"phase":"ChampSelect","gameData":{"queue":{"id":1710}}}""");
        using var otherArena = JsonDocument.Parse("""{"phase":"ChampSelect","gameData":{"queue":{"gameMode":"CHERRY"}}}""");
        Check(ChampionSelectClient.SelectMode(lobby.RootElement) == Mode.Caos &&
            ChampionSelectClient.SelectMode(started.RootElement) is null &&
            ChampionSelectClient.SelectMode(normalAram.RootElement) is null &&
            ChampionSelectClient.SelectMode(otherMayhem.RootElement) == Mode.Caos &&
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
        CheckMayhemRoster();

        var page = ReadSpells();
        var main = new OpGgSpellPair("Destello", "Marca");
        Check(page.Spells is { Length: 2 } spells && spells.SequenceEqual([main, new("Destello", "Fantasma")]) &&
            SpellOverlay.Matches(main, new("Kennen", "Marca", "Destello")) &&
            !SpellOverlay.Matches(main, new("Kennen", "Fantasma", "Destello")),
            "combinaciones de OP.GG sin duplicados y comparación independiente de D y F");
        Console.WriteLine("Selección de campeón y hechizos de OP.GG verificados.");
    }

    private static void CheckMayhemRoster()
    {
        using var offer = JsonDocument.Parse("""
            {"queueId":2400,"localPlayerCellId":4,"benchEnabled":true,
             "myTeam":[{"cellId":0,"championId":103},{"cellId":1,"championId":86},
               {"cellId":2,"championId":0,"championPickIntent":99},{"cellId":3,"championId":0},
               {"cellId":4,"championId":0}],"theirTeam":[{"cellId":5,"championId":0}],
             "benchChampions":[{"championId":103,"isPriority":false},{"championId":16,"isPriority":true},
               {"championId":45},{"championId":20},{"championId":16},{"championId":0}],
             "bans":{"myTeamBans":[45,-1],"theirTeamBans":[0]},
             "actions":[[{"championId":20,"type":"ban","completed":true},
               {"championId":16,"type":"ban","completed":false}]],"timer":{"phase":"BAN_PICK"}}
            """);
        var visible = ChampionSelectClient.MayhemDraftIds(offer.RootElement);
        Check(visible is { Teammates: [103, 86], Bench: [16], Unavailable: [45, 20], BenchKnown: true, PicksOpen: true } &&
            ChampionSelectClient.OwnPick(offer.RootElement) is null,
            "ARAM Caos lee aliados confirmados y banca aun durante la oferta propia");

        const string swap = """
            {"localPlayerCellId":4,"benchEnabled":true,
             "myTeam":[{"cellId":0,"championId":85},{"cellId":1,"championId":86},
               {"cellId":2,"championId":99},{"cellId":3,"championId":45},
               {"cellId":4,"championId":103,"spell1Id":4,"spell2Id":32}],
             "benchChampionIds":[103,16,32,32],"timer":{"phase":"FINALIZATION"}}
            """;
        using var swapped = JsonDocument.Parse(swap);
        using var loading = JsonDocument.Parse(swap.Replace("FINALIZATION", "GAME_STARTING", StringComparison.Ordinal));
        Check(ChampionSelectClient.MayhemDraftIds(swapped.RootElement) is
            { Teammates: [85, 86, 99, 45], Bench: [16, 32], BenchKnown: true, PicksOpen: true } &&
            ChampionSelectClient.OwnPick(swapped.RootElement) == (103, 4, 32) &&
            ChampionSelectClient.MayhemDraftIds(loading.RootElement) is { Teammates.Length: 4, PicksOpen: false },
            "el intercambio actualiza a cada jugador y las sugerencias siguen disponibles hasta iniciar la partida");

        using var suppressed = JsonDocument.Parse(swap.Replace("true", "false", StringComparison.Ordinal));
        using var missing = JsonDocument.Parse(swap.Replace("benchChampionIds", "unknownField", StringComparison.Ordinal));
        using var empty = JsonDocument.Parse(swap.Replace("[103,16,32,32]", "[]", StringComparison.Ordinal));
        Check(ChampionSelectClient.MayhemDraftIds(suppressed.RootElement) is { Bench: [], BenchKnown: true } &&
            ChampionSelectClient.MayhemDraftIds(empty.RootElement) is { Bench: [], BenchKnown: true } &&
            ChampionSelectClient.MayhemDraftIds(missing.RootElement) is { Bench: [], BenchKnown: false },
            "la banca ausente activa el respaldo visual, mientras la banca vacía o deshabilitada se respeta");
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
            var bounds = new Int32Rect(-30000, -30000, 2000, 1125);
            var data = new ArenaChampionTierList(catalog.Patch, catalog.Patch, DateTimeOffset.UtcNow, 500, new()
            {
                ["Lillia"] = new("Lillia", "S", 59, 100),
                ["Jax"] = new("Jax", "A", 53, 100),
                ["Rell"] = new("Rell", "S", 58, 100)
            });
            var otherChampions = catalog.Champions.Where(champion => champion.Key is not ("Aurora" or "Lillia" or "Jax" or "Rell"))
                .Take(28).ToArray();
            for (int i = 0; i < otherChampions.Length; i++)
            {
                var key = otherChampions[i].Key;
                data.Champions[key] = new(key, "B", 50 + i, 100);
            }
            var source = new ArenaChampionTierView(data, "Prueba");
            var global = ArenaChampionDisplay.Create(bounds, 0, catalog, source, null);
            var ranked = new[] { "Lillia", "Rell", "Jax" }.Concat(otherChampions.Skip(1).Reverse().Select(champion => champion.Key));
            Check(global.Selection is null && global.Rows.Length == 30 &&
                global.Rows.Select(row => row.Rating?.Champion).SequenceEqual(ranked),
                "el ranking global toma hasta treinta campeones por tier y resultados");

            overlay.Render(global);
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var handle = new WindowInteropHelper(overlay).Handle;
            var panel = (StackPanel)((Border)((Viewbox)overlay.Content).Child).Child;
            Check(Native.IsWindowVisible(handle) &&
                (Native.GetWindowLongW(handle, -20) & 0x08000020) == 0x08000020,
                "el ranking lateral se muestra sin interceptar los clics");
            Check(panel.Children.OfType<TextBlock>().Select(text => text.Text).SequenceEqual(
                    ["Arena · Top 30 general", $"GGEZ.GG · Parche {LeaguePatch.Public(catalog.Patch)}"]),
                "el Top 30 muestra la fuente y el parche");
            CheckColumn(bounds);

            var excludedChampion = otherChampions[0];
            ArenaCompositionPick[] composition = [new("Amumu", "A", "Aporta primera línea y control"),
                new("Lux", "B", "Aporta daño mágico")];
            var grade = new ArenaCompositionGrade("A", "Protege el daño sostenido", 2, 3);
            var selected = ArenaChampionDisplay.Create(bounds, 0, catalog, source, excludedChampion.Key, composition, grade);
            Check(selected.Rows.SequenceEqual(global.Rows) && selected.Selection?.Rating?.Tier == "B",
                "el campeón preseleccionado conserva su tier aunque quede fuera del Top 30");
            overlay.Render(selected);
            var summary = (StackPanel)panel.Children[0];
            var badge = (DockPanel)summary.Children[0];
            var team = (StackPanel)summary.Children[1];
            var teamBadge = (DockPanel)team.Children[0];
            Check(((TextBlock)((Border)badge.Children[0]).Child).Text == "B" &&
                ((StackPanel)badge.Children[1]).Children.OfType<TextBlock>().First().Text == excludedChampion.Name &&
                ((TextBlock)((Border)teamBadge.Children[0]).Child).Text == "A" &&
                ((StackPanel)teamBadge.Children[1]).Children.OfType<TextBlock>().Select(text => text.Text)
                    .SequenceEqual(["Composición Arena", "Tier local · 2/3 campeones"]) &&
                ((TextBlock)team.Children[1]).Text == grade.Reason &&
                panel.Children.OfType<TextBlock>().Any(text => text.Text == "Para tu equipo") &&
                panel.Children.OfType<StackPanel>().Skip(1).Select(pick => ((TextBlock)pick.Children[1]).Text)
                    .SequenceEqual(composition.Select(pick => pick.Reason)),
                "muestra el tier independiente y parcial de la composición junto a la preselección y las sugerencias");
            overlay.Render(selected with { Grade = grade with { Tier = "S", Members = 3 } });
            var updated = (DockPanel)((StackPanel)((StackPanel)panel.Children[0]).Children[1]).Children[0];
            Check(((TextBlock)((Border)updated.Children[0]).Child).Text == "S",
                "la valoración se actualiza al cambiar los aliados aunque el campeón propio siga igual");

            var medium = new Int32Rect(bounds.X, bounds.Y, 1280, 720);
            overlay.Render(selected with { Bounds = medium });
            CheckColumn(medium);
            var narrow = new Int32Rect(bounds.X, bounds.Y, 800, 450);
            overlay.Render(selected with { Bounds = narrow });
            CheckColumn(narrow);
            var unavailable = ArenaChampionDisplay.Create(bounds, 0, catalog, source, "Aurora");
            overlay.Render(unavailable);
            Check(((DockPanel)((StackPanel)panel.Children[0]).Children[0]).Children.OfType<StackPanel>().Single()
                .Children.OfType<TextBlock>().Select(text => text.Text).SequenceEqual(["Aurora", "Sin tier publicado"]),
                "muestra el campeón cuando la fuente carece de una valoración");
            overlay.Render(global);
            Check(panel.Children[0] is TextBlock, "al quitar la preselección desaparecen ambos distintivos");

            overlay.Render(null);
            Check(!Native.IsWindowVisible(handle), "se oculta el panel de Arena al salir de selección");
            Console.WriteLine("Panel de campeones de Arena en WPF verificado.");

            void CheckColumn(Int32Rect client)
            {
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                var placement = Native.Bounds(handle);
                var ranking = panel.Children.OfType<Grid>().Single();
                var cells = ranking.Children.OfType<TextBlock>().ToArray();
                var names = cells.Where(cell => Grid.GetColumn(cell) == 1 && Grid.GetRow(cell) > 0).ToArray();
                Check(placement.X >= client.X + client.Width / 2 &&
                    placement.X + placement.Width <= client.X + client.Width &&
                    placement.Y >= client.Y && placement.Y + placement.Height <= client.Y + client.Height &&
                    ranking.RowDefinitions.Count == 31 &&
                    names.Length == 30 && names.All(cell => cell.FontSize >= (client.Width >= 1600 ? 17 : 15)) &&
                    names.Select(cell => Grid.GetRow(cell)).SequenceEqual(Enumerable.Range(1, 30)) &&
                    cells.Where(cell => Grid.GetColumn(cell) == 0)
                        .Select(cell => int.Parse(cell.Text)).SequenceEqual(Enumerable.Range(1, 30)),
                    "muestra las treinta posiciones en una sola columna dentro de la ventana del cliente");
            }
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

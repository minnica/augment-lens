using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
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
        CheckMayhemRoster().GetAwaiter().GetResult();

        var page = ReadSpells();
        var main = new OpGgSpellPair("Destello", "Marca");
        Check(page.Spells is { Length: 2 } spells && spells.SequenceEqual([main, new("Destello", "Fantasma")]),
            "combinaciones de OP.GG sin duplicados");
        Console.WriteLine("Selección de campeón y hechizos de OP.GG verificados.");
    }

    private static async Task CheckMayhemRoster()
    {
        var fixture = new MayhemLobby();
        using var connection = new HttpClient(fixture) { BaseAddress = new Uri("https://127.0.0.1") };
        using var lobby = new ChampionSelectClient(Path.Combine(AppContext.BaseDirectory, "Assets", "riotgames.pem"));
        Task<ChampionSelectSession?> Read(string? session)
        {
            fixture.Session = session;
            return lobby.ReadMayhemDraft(connection, CancellationToken.None);
        }

        const string offer = """
            {"queueId":2400,"localPlayerCellId":4,"benchEnabled":true,
             "myTeam":[{"cellId":0,"championId":103},{"cellId":1,"championId":86},
               {"cellId":2,"championId":0,"championPickIntent":99},{"cellId":3,"championId":0},
               {"cellId":4,"championId":0}],"theirTeam":[{"cellId":5,"championId":0}],
             "benchChampions":[{"championId":103,"isPriority":false},{"championId":16,"isPriority":true},
               {"championId":45},{"championId":20},{"championId":16},{"championId":0}],
             "bans":{"myTeamBans":[45,-1],"theirTeamBans":[0]},
             "actions":[[{"championId":20,"type":"ban","completed":true},
               {"championId":16,"type":"ban","completed":false}]],"timer":{"phase":"BAN_PICK"}}
            """;
        var visible = await Read(offer);
        Check(visible is { Mode: Mode.Caos, Pick: null, MayhemTeam: { Teammates: ["Ahri", "Garen"],
            Bench: ["Soraka"], Unavailable: ["Veigar", "Nunu"], BenchKnown: true } },
            "ARAM Caos lee aliados confirmados y banca aun durante la oferta propia");

        const string swap = """
            {"localPlayerCellId":4,"benchEnabled":true,
             "myTeam":[{"cellId":0,"championId":85},{"cellId":1,"championId":86},
               {"cellId":2,"championId":99},{"cellId":3,"championId":45},
               {"cellId":4,"championId":103,"spell1Id":4,"spell2Id":32}],
             "benchChampionIds":[103,16,32,32],"timer":{"phase":"FINALIZATION"}}
            """;
        var own = new ChampionSelection("Ahri", "Destello", "Marca");
        var swapped = await Read(swap);
        Check(swapped is { MayhemTeam: { Teammates: ["Kennen", "Garen", "Lux", "Veigar"],
            Bench: ["Soraka", "Amumu"], BenchKnown: true } } && swapped.Pick == own,
            "el intercambio actualiza el banco y los hechizos también si FINALIZATION carece de contador");

        var suppressed = await Read(swap.Replace("true", "false", StringComparison.Ordinal));
        var empty = await Read(swap.Replace("[103,16,32,32]", "[]", StringComparison.Ordinal));
        var missing = await Read(swap.Replace("benchChampionIds", "unknownField", StringComparison.Ordinal));
        Check(suppressed?.MayhemTeam is { Bench: [], BenchKnown: true } &&
            empty?.MayhemTeam is { Bench: [], BenchKnown: true } && missing?.MayhemTeam is { Bench: [], BenchKnown: false },
            "la banca ausente activa el respaldo visual, mientras la banca vacía o deshabilitada se respeta");

        const string final = """{"phase":"FINALIZATION"}""";
        const string positive = """{"phase":"FINALIZATION","adjustedTimeLeftInPhase":200,"isInfinite":false}""";
        const string infinite = """{"phase":"FINALIZATION","adjustedTimeLeftInPhase":0,"isInfinite":true}""";
        const string expired = """{"phase":"FINALIZATION","adjustedTimeLeftInPhase":0,"isInfinite":false}""";
        const string starting = """{"phase":"GAME_STARTING"}""";
        Check((await Read(swap.Replace(final, positive, StringComparison.Ordinal)))?.Pick == own &&
            (await Read(swap.Replace(final, infinite, StringComparison.Ordinal)))?.Pick == own,
            "el banco y los hechizos siguen disponibles mientras la selección tiene tiempo o es infinita");
        Check(await Read(swap.Replace(final, expired, StringComparison.Ordinal)) is null &&
            await Read(swap.Replace(final, starting, StringComparison.Ordinal)) is null,
            "el contador agotado y GAME_STARTING retiran la sesión aunque el último equipo siga en la respuesta");
        Check(await Read("""{"timer":{"phase":"GAME_STARTING"}}""") is null &&
            await Read("""{"timer":{"phase":"FINALIZATION","adjustedTimeLeftInPhase":0}}""") is null,
            "el cierre también se reconoce cuando ya se ha retirado el equipo");
        Check(await Read(null) is null && await Read(null) is null && await Read("{}") is null,
            "los siguientes sondeos con 404 o sesión vacía mantienen ocultos los paneles");
        Check(await Read("""{"timer":{"phase":"BAN_PICK"},"myTeam":[]}""") is { Mode: Mode.Caos, Pick: null } &&
            await Read(offer) is { Pick: null, MayhemTeam.Bench: ["Soraka"] },
            "la selección siguiente vuelve a ofrecer las cartas mientras carga el equipo y recupera su banco");
    }

    private sealed class MayhemLobby : HttpMessageHandler
    {
        internal string? Session { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            string? body = request.RequestUri?.AbsolutePath switch
            {
                "/lol-champ-select/v1/session" => Session,
                "/lol-game-data/assets/v1/champion-summary.json" => """
                    [{"id":103,"alias":"Ahri"},{"id":86,"alias":"Garen"},{"id":16,"alias":"Soraka"},{"id":45,"alias":"Veigar"},
                     {"id":20,"alias":"Nunu"},{"id":85,"alias":"Kennen"},{"id":99,"alias":"Lux"},{"id":32,"alias":"Amumu"}]
                    """,
                "/lol-game-data/assets/v1/summoner-spells.json" => """[{"id":4,"name":"Destello"},{"id":32,"name":"Marca"}]""",
                _ => throw new InvalidOperationException($"Ruta de selección inesperada: {request.RequestUri?.AbsolutePath}")
            };
            return Task.FromResult(body is null ? new HttpResponseMessage(HttpStatusCode.NotFound) :
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    internal static void Smoke()
    {
        var overlay = new SpellOverlay { Left = -30000, Top = -30000 };
        try
        {
            var bounds = new Int32Rect(-30000, -30000, 1040, 720);
            var prior = ReadSpells();
            var view = new SpellDisplay(bounds, 0, "Kennen", new("Kennen", "Marca", "Destello"), new(prior, "Prueba"));
            overlay.Render(view);
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var handle = new WindowInteropHelper(overlay).Handle;
            var placement = Native.Bounds(handle);
            var panel = (StackPanel)((Border)((Viewbox)overlay.Content).Child).Child;
            Check(Native.IsWindowVisible(handle) && placement.X > bounds.X + bounds.Width / 2 &&
                placement.X + placement.Width <= bounds.X + bounds.Width && placement.Y >= bounds.Y &&
                placement.Y + placement.Height <= bounds.Y + bounds.Height &&
                (Native.GetWindowLongW(handle, -20) & 0x08000020) == 0x08000020 &&
                TextBlocks(panel).Select(row => row.Text).SequenceEqual(
                    ["Kennen", "Principal", "Destello + Marca", "Alternativa", "Destello + Fantasma", "Hechizos del parche anterior"]),
                "panel dentro del cliente, sin interceptar clics y con las dos combinaciones de OP.GG");
            var current = view with { Tier = "A", Source = new(prior with { SourcePatch = prior.ClientPatch }, "OP.GG · Parche 26.23") };
            overlay.Render(current);
            var labels = TextBlocks(panel).ToDictionary(label => label.Text);
            var header = (DockPanel)((Border)panel.Children[0]).Child;
            var badge = header.Children.OfType<Border>().Single(IsTierBadge);
            Check(DockPanel.GetDock(badge) == Dock.Right && Elements(header).OfType<Image>().Count() == 1 &&
                badge is TierHexagon { Tier: "A" } && labels.ContainsKey("Kennen") &&
                !labels.Keys.Any(text => text.Contains("Parche", StringComparison.OrdinalIgnoreCase) || text.Contains("OP.GG")) &&
                !labels.ContainsKey("Hechizos") && labels["Kennen"].FontSize > labels["Principal"].FontSize &&
                labels["Principal"].FontSize == labels["Alternativa"].FontSize &&
                labels["Principal"].FontSize > labels["Destello + Marca"].FontSize &&
                labels["Destello + Marca"].FontSize == labels["Destello + Fantasma"].FontSize &&
                !labels.ContainsKey("Ya tienes estos hechizos"),
                "los datos vigentes omiten el parche y mantienen el tier y la jerarquía tipográfica");
            overlay.Render(current with { TierIsPrevious = true });
            Check(TextBlocks(panel).Any(text => text.Text == "Tier del parche anterior"),
                "el tier anterior se identifica aunque los hechizos estén vigentes");
            overlay.Render(view with { Tier = "A", TierIsPrevious = true });
            Check(TextBlocks(panel).Where(text => text.Text.Contains("parche")).Select(text => text.Text)
                .SequenceEqual(["Datos del parche anterior"]), "una misma advertencia identifica el tier y los hechizos anteriores");
            overlay.Render(current with { Source = new(null, "Consultando OP.GG…") });
            Check(Labels(panel).SequenceEqual(["A", "Kennen", "Consultando OP.GG…"]),
                "la carga conserva el campeón y retira las combinaciones anteriores");
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
            var frame = (Border)((Viewbox)overlay.Content).Child;
            var panel = (StackPanel)frame.Child;
            Check(Native.IsWindowVisible(handle) &&
                (Native.GetWindowLongW(handle, -20) & 0x08000020) == 0x08000020,
                "el ranking lateral se muestra sin interceptar los clics");
            Check(Sections().Children.OfType<TextBlock>().Select(text => text.Text).SequenceEqual(["Top 30 general"]),
                "el Top 30 abre directamente en la tabla");
            CheckColumn(bounds);

            var excludedChampion = otherChampions[0];
            ArenaCompositionGroup[] composition =
                [new("Daño sostenido", [new("Jinx", "A"), new("Vayne", "B"), new("Diana", "B"), new("Azir", "C")]),
                 new("Daño mágico", [new("Lux", "B"), new("Annie", "C"), new("Syndra", "C"), new("Veigar", "D")])];
            var grade = new ArenaCompositionGrade("A", "Falta daño sostenido", 2, 3);
            var selected = ArenaChampionDisplay.Create(bounds, 0, catalog, source, excludedChampion.Key, composition, grade,
                new(["Lillia"], [], 3, OwnConfirmed: false, ConfirmedTeammates: ["Lillia"]));
            Check(selected.Rows.SequenceEqual(global.Rows) && selected.Selection?.Rating?.Tier == "B",
                "el campeón preseleccionado conserva su tier aunque quede fuera del Top 30");
            overlay.Render(selected);
            var evaluation = (Border)panel.Children[0];
            Check(Labels(evaluation).Take(2).SequenceEqual([grade.Reason, "A"]) &&
                RosterRow(0).SequenceEqual(["Campeón", "Estado", "Tier"]) &&
                RosterRow(1).SequenceEqual([excludedChampion.Name, "TÚ", "Preselección", "B"]) &&
                RosterRow(2).SequenceEqual(["Lillia", "Confirmado", "S"]) &&
                !TextBlocks(panel).Any(text => text.Text is "50 % top 3" or "2/3 campeones"),
                "el equipo agrupa nombre, estado y tier sin el contador ni el porcentaje individual");
            var categories = Sections().Children.OfType<StackPanel>().ToArray();
            Check(categories.Length == composition.Length &&
                categories.Zip(composition).All(pair => Labels(pair.First).SequenceEqual(
                    new[] { pair.Second.Category }.Concat(pair.Second.Options.SelectMany(option => new[] { option.Champion, option.Tier })))),
                "la sección agrupa varias opciones con sus tiers en cada categoría");
            var headers = TextBlocks(panel).Where(text => text.Text is "Para tu equipo" or "Top 30 general").ToArray();
            Check(headers.Length == 2 && headers.All(text => text.FontSize == OverlayPanelStyle.SectionSize) &&
                TextBlocks(evaluation).First().FontSize == OverlayPanelStyle.TitleSize &&
                categories.SelectMany(category => category.Children.OfType<Grid>()).All(row => Grid.GetColumn(row.Children[1]) == 1),
                "la evaluación encabeza el panel, las secciones comparten jerarquía y el tier se alinea con el nombre");
            overlay.Render(selected with { Grade = grade with { Tier = "S", Members = 3, Reason = "Equipo equilibrado" } });
            Check(Labels((Border)panel.Children[0]).Take(2).SequenceEqual(["Equipo equilibrado", "S"]),
                "la valoración se actualiza al cambiar los aliados aunque el campeón propio siga igual");
            var observed = new ArenaTeamResult(catalog.Patch, 80, 2.30m);
            overlay.Render(selected with { Grade = new(observed.Tier, grade.Reason, 3, 3, observed) });
            Check(Labels((Border)panel.Children[0]).Take(3).SequenceEqual(
                [grade.Reason, "Puesto medio 2.30 · 80 partidas", observed.Tier]),
                "el puesto observado y su muestra quedan debajo de la necesidad");

            overlay.Render(selected with { Roster = [selected.Roster[0] with { Confirmed = true }, selected.Roster[1]] });
            Check(RosterRow(1).SequenceEqual([excludedChampion.Name, "TÚ", "Confirmado", "B"]),
                "confirmar actualiza los estados aunque sigan iguales los campeones y las sugerencias");
            CheckColumn(bounds);
            overlay.Render(selected with { Selection = null, Grade = null, Roster = [selected.Roster[1]] });
            Check(TextBlocks((Border)panel.Children[0]).First().Text == "Esperando tu elección" &&
                RosterRow(1).SequenceEqual(["Lillia", "Confirmado", "S"]), "los aliados se muestran antes de elegir tu campeón");

            CheckRankingSelection("Jax", 3);
            CheckRankingSelection("Lillia", 1);

            var medium = new Int32Rect(bounds.X, bounds.Y, 1280, 720);
            overlay.Render(selected with { Bounds = medium });
            CheckColumn(medium);
            var narrow = new Int32Rect(bounds.X, bounds.Y, 800, 450);
            overlay.Render(selected with { Bounds = narrow });
            CheckColumn(narrow);
            var unavailable = ArenaChampionDisplay.Create(bounds, 0, catalog, source, "Aurora");
            overlay.Render(unavailable);
            Check(RosterRow(1).SequenceEqual(["Aurora", "TÚ", "—", "—"]) &&
                TextBlocks((Border)panel.Children[0]).First().Text == "Esperando un aliado",
                "muestra el campeón cuando la fuente carece de una valoración");
            overlay.Render(global);
            Check(panel.Children.Count == 1 && Sections().Children[0] is TextBlock { Text: "Top 30 general" },
                "al quitar la preselección el ranking pasa al frente");
            var previous = LeaguePatch.Previous(catalog.Patch) ?? catalog.Patch;
            overlay.Render(ArenaChampionDisplay.Create(bounds, 0, catalog,
                new(data with { SourcePatch = previous }, "Prueba"), null));
            Check(Sections().Children.OfType<TextBlock>().Any(text => text.Text == "Parche anterior"),
                "la procedencia del ranking avisa cuando usa el parche anterior");

            overlay.Render(null);
            Check(!Native.IsWindowVisible(handle), "se oculta el panel de Arena al salir de selección");
            Console.WriteLine("Panel de campeones de Arena en WPF verificado.");

            StackPanel Sections() => panel.Children.OfType<StackPanel>().Single();
            IEnumerable<string> RosterRow(int row) => ((StackPanel)((Border)panel.Children[0]).Child)
                .Children.OfType<Grid>().Last().Children.OfType<FrameworkElement>()
                .Where(cell => Grid.GetRow(cell) == row).OrderBy(Grid.GetColumn).SelectMany(Labels);

            void CheckRankingSelection(string champion, int row)
            {
                overlay.Render(ArenaChampionDisplay.Create(bounds, 0, catalog, source, champion));
                var ranking = Sections().Children.OfType<Grid>().Last();
                var highlighted = ranking.Children.OfType<FrameworkElement>().Where(cell => Grid.GetRow(cell) == row).ToArray();
                Check(highlighted.SelectMany(TextBlocks).Any(text => text.Text == champion) &&
                    highlighted.SelectMany(TextBlocks).Count(text => text.Text == "TÚ") == 1 &&
                    highlighted.OfType<Border>().Any(border => Grid.GetColumnSpan(border) == ranking.ColumnDefinitions.Count &&
                        border.BorderBrush is not null && border.Background is not null) &&
                    TextBlocks(ranking).Count(text => text.Text == "TÚ") == 1,
                    "la fila de tu campeón resalta en el ranking y cambia con la preselección");
            }

            void CheckColumn(Int32Rect client)
            {
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                var placement = Native.Bounds(handle);
                var ranking = Sections().Children.OfType<Grid>().Last();
                var cells = ranking.Children.OfType<FrameworkElement>().ToArray();
                var names = cells.Where(cell => Grid.GetColumn(cell) == 1 && Grid.GetRow(cell) > 0).ToArray();
                Check(placement.X >= client.X + client.Width / 2 &&
                    placement.X + placement.Width <= client.X + client.Width &&
                    placement.Y >= client.Y && placement.Y + placement.Height <= client.Y + client.Height &&
                    Math.Abs(placement.Width - placement.Height * frame.ActualWidth / frame.ActualHeight) < 2 &&
                    ranking.RowDefinitions.Count == 31 &&
                    Math.Abs(ranking.ActualHeight - ranking.RowDefinitions.Sum(row => row.Height.Value)) < 1 &&
                    names.Length == 30 && names.All(cell => TextBlocks(cell).First().FontSize == OverlayPanelStyle.BodySize &&
                        Elements(cell).OfType<Image>().Count() == 1) &&
                    names.Select(cell => Grid.GetRow(cell)).SequenceEqual(Enumerable.Range(1, 30)) &&
                    cells.OfType<Border>().Count(IsTierBadge) == 30 &&
                    cells.OfType<TextBlock>().Where(cell => Grid.GetColumn(cell) == 0)
                        .Select(cell => int.Parse(cell.Text)).SequenceEqual(Enumerable.Range(1, 30)),
                    "muestra las treinta posiciones en una sola columna dentro de la ventana del cliente");
            }
        }
        finally { overlay.Close(); }
    }

    internal static bool IsTierBadge(Border border) => border is TierHexagon { Background: ImageBrush };

    internal static IEnumerable<string> Labels(FrameworkElement element) => Elements(element).Select(child => child switch
    {
        TierHexagon { Background: ImageBrush, Tier: { } tier } => tier,
        TextBlock text => text.Text,
        _ => null
    }).OfType<string>();

    private static IEnumerable<TextBlock> TextBlocks(FrameworkElement element) => Elements(element).OfType<TextBlock>();

    internal static IEnumerable<FrameworkElement> Elements(FrameworkElement element)
    {
        yield return element;
        IEnumerable<FrameworkElement> nested = element switch
        {
            Panel panel => panel.Children.OfType<FrameworkElement>(),
            Decorator { Child: FrameworkElement child } => [child],
            _ => []
        };
        foreach (var descendant in nested.SelectMany(Elements))
            yield return descendant;
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

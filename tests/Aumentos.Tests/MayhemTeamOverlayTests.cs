using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using Aumentos;

internal static class MayhemTeamOverlayTests
{
    internal static void Smoke(Catalog catalog)
    {
        var bounds = new Int32Rect(-30000, -30000, 2000, 1125);
        string[] available = ["Sona", "Shyvana", "Renekton", "Amumu", "Anivia", "Leona", "Talon", "Taric", "Galio", "Braum"];
        var ranks = new Dictionary<string, int> { ["twistedfate"] = 1, ["sivir"] = 34, ["anivia"] = 68,
            ["leona"] = 39, ["amumu"] = 45 };
        var tiers = new Dictionary<string, int> { ["twistedfate"] = 1, ["sivir"] = 3, ["anivia"] = 1, ["leona"] = 2,
            ["amumu"] = 2, ["sona"] = 3 };
        var data = new MayhemChampionTierList(catalog.Patch, DateTimeOffset.UtcNow, tiers, ranks);
        const string tradeoff = "El equipo pierde primera línea";
        MayhemCompositionPick[] picks =
            [new("Anivia", "S", "Daño mágico a distancia", tradeoff), new("Amumu", "A", "Primera línea"), new("Sona", "B", "Protección")];
        var grade = new MayhemCompositionGrade("B", "Falta daño sostenido", 3, 5);
        var view = MayhemTeamDisplay.Create(bounds, 0, catalog, new(data, "Datos de prueba"), "Sivir",
            available, "El banco está vacío.", picks, grade);
        Check(view.Rows.Length == available.Length && view.Rows.Take(4).Select(row => row.Champion)
            .SequenceEqual(["Anivia", "Leona", "Amumu", "Sona"]) &&
            view.Rows.Skip(4).All(row => row.Tier is null) && view.Selection is { Champion: "Sivir", Position: 34, Tier: "B" },
            "Caos ordena los campeones disponibles por tier, con el puesto de OP.GG como desempate");
        CheckAvailability(catalog);

        var overlay = new MayhemTeamOverlay { Left = -30000, Top = -30000 };
        try
        {
            overlay.Render(view);
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var handle = new WindowInteropHelper(overlay).Handle;
            var panel = (StackPanel)((Border)((Viewbox)overlay.Content).Child).Child;
            var labels = Labels(panel).ToArray();
            Check(Native.IsWindowVisible(handle) && (Native.GetWindowLongW(handle, -20) & 0x08000020) == 0x08000020 &&
                labels.Contains("Campeones disponibles") && labels.Contains("Tu campeón") && labels.Contains("Sivir") &&
                labels.Contains("3/5 campeones") && !labels.Any(IsMetadata),
                "tier, evaluación y sugerencias se muestran juntos sin interceptar clics");
            var evaluation = (Grid)((Border)panel.Children[0]).Child;
            var composition = evaluation.Children.OfType<StackPanel>().Single();
            var status = (TextBlock)composition.Children[0];
            var detail = (TextBlock)composition.Children[1];
            Check(status.Text == grade.Reason && status.FontWeight == FontWeights.Bold &&
                status.FontSize > detail.FontSize && status.TextWrapping == TextWrapping.Wrap,
                "la necesidad abre el panel y la cobertura parcial queda en segundo plano");
            CheckPlacement(bounds, .73);
            var recommendations = panel.Children.OfType<StackPanel>().Single().Children.OfType<Grid>()
                .Where(row => row.RowDefinitions.Count == 2).ToArray();
            Check(recommendations.Select(row => Labels(row).ToArray()).Zip(picks)
                .All(pair => pair.First.SequenceEqual(new[] { pair.Second.Champion, pair.Second.Tier, pair.Second.Reason, pair.Second.Tradeoff }
                    .OfType<string>())) && recommendations.Length == picks.Length,
                "las sugerencias muestran nombre, letra, aporte y el costo solo donde aplica");
            CheckTypeHierarchy(panel);
            foreach (var row in recommendations)
            {
                var name = (TextBlock)row.Children[0];
                var badge = (Border)row.Children[1];
                var nameCenter = name.TranslatePoint(new Point(0, name.ActualHeight / 2), panel);
                var badgeCenter = badge.TranslatePoint(new Point(badge.ActualWidth / 2, badge.ActualHeight / 2), panel);
                var teamTier = (Border)evaluation.Children[1];
                var teamCenter = teamTier.TranslatePoint(new Point(teamTier.ActualWidth / 2, 0), panel);
                Check(Grid.GetColumn(badge) == 1 && Math.Abs(nameCenter.Y - badgeCenter.Y) <= 1 &&
                    Math.Abs(teamCenter.X - badgeCenter.X) <= 1, "cada letra se alinea con el nombre y con la columna del equipo");
            }
            Check(BankRows(panel).SelectMany(Labels).Where((_, index) => index % 2 == 1)
                .SequenceEqual(["S", "A", "A", "B", "—", "—", "—", "—", "—", "—"]) &&
                !labels.Contains("Twisted Fate"), "la lista conserva los candidatos y sus tiers disponibles");

            overlay.Render(view with { Composition = picks.Select(pick => pick with { Tradeoff = tradeoff }).ToArray() });
            Check(Labels(panel).Count(text => text == tradeoff) == 1, "un costo común aparece una sola vez");
            var compact = new Int32Rect(bounds.X, bounds.Y, 1280, 720);
            overlay.Render(view with { Bounds = compact, Grade = grade with { Members = 5 } });
            CheckPlacement(compact, .64);
            CheckTypeHierarchy(panel);
            Check(Labels(panel).Contains("Para tu equipo") && !Labels(panel).Contains("5/5 campeones"),
                "la composición completa omite el recuento y conserva las sugerencias cuando hay carencias");
            overlay.Render(view with { Bounds = compact, Grade = grade with { Members = 5, Tier = "S" } });
            CheckPlacement(compact, .64);
            var balanced = Labels(panel).ToArray();
            Check(balanced.Contains("Equipo equilibrado") && !balanced.Contains("5/5 campeones") &&
                !balanced.Contains("Para tu equipo") && !balanced.Contains(grade.Reason) &&
                BankRows(panel).Count() == available.Length,
                "un equipo completo de tier S conserva el banco y confirma que la composición está equilibrada");

            overlay.Render(view with { Selection = null });
            Check(Labels(panel).Contains("3/5 campeones") &&
                Labels(panel).Contains("Para tu equipo") && Labels(panel).Contains(grade.Reason) &&
                !Labels(panel).Contains("Equipo equilibrado") && !Labels(panel).Contains("Tu campeón"),
                "la composición provisional recupera el aviso y las sugerencias al cambiar el equipo");

            var previousPatch = LeaguePatch.Previous(catalog.Patch) ?? catalog.Patch;
            var legacy = MayhemTeamDisplay.Create(bounds, 0, catalog, new(data with { SourcePatch = previousPatch, Ranks = null }, ""),
                "Sivir", available, "El banco está vacío.", picks, grade);
            overlay.Render(legacy);
            Check(legacy.Selection?.Tier == "B" && BankRows(panel).Count() == available.Length &&
                legacy.Rows[0] is { Champion: "Anivia", Position: null, Tier: "S" } &&
                Labels(panel).Contains("Parche anterior") && !Labels(panel).Any(IsMetadata),
                "una copia anterior conserva el tier de las opciones aunque no traiga puestos");

            overlay.Render(view with { Rows = [], EmptyMessage = "El banco está vacío." });
            Check(!BankRows(panel).Any() && Labels(panel).Contains("El banco está vacío."),
                "un banco vacío no muestra campeones ni deja filas del banco anterior");
            var loading = MayhemTeamDisplay.Create(bounds, 0, catalog, new(null, "Consultando tiers de OP.GG…"),
                null, ["Shyvana", "Renekton"], "Esperando tus cartas…", [], null);
            overlay.Render(loading);
            Check(Labels(panel).Contains("Consultando tiers de OP.GG…") && Labels(panel).Contains("Shyvana") &&
                Labels(panel).Contains("Renekton") && BankRows(panel).Count() == 2,
                "las opciones identificadas aparecen mientras llegan sus tiers");

            overlay.Render(null);
            overlay.Render(null);
            Check(!Native.IsWindowVisible(handle), "el panel de Caos se retira al salir de selección");
            overlay.Render(view);
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Check(Native.IsWindowVisible(handle) && Labels(panel).Contains(grade.Reason) && !Labels(panel).Any(IsMetadata) &&
                !Labels(panel).Contains("Consultando tiers de OP.GG…"), "la siguiente selección vuelve a mostrar sus propios datos");
            Console.WriteLine("Panel de composición de ARAM Caos en WPF verificado.");

            void CheckPlacement(Int32Rect client, double bottom)
            {
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                var position = Native.Bounds(handle);
                Check(position.X >= client.X + (int)(client.Width * .77) && position.X + position.Width <= client.X + client.Width &&
                    position.Y > client.Y && position.Y + position.Height <= client.Y + (int)(client.Height * bottom),
                    "el panel reserva las cartas, el banco y el área de hechizos");
            }
        }
        finally { overlay.Close(); }
    }

    private static bool IsMetadata(string text) => text.Contains("Composición") || text.Contains("Tier local") ||
        text.Contains("Puesto") || text.StartsWith("OP.GG");

    private static IEnumerable<Grid> BankRows(StackPanel panel) => panel.Children.OfType<StackPanel>().Single()
        .Children.OfType<Grid>().SkipWhile(row => !Labels(row).Contains("Campeones disponibles")).Skip(1);

    private static void CheckAvailability(Catalog catalog)
    {
        var team = new MayhemDraftTeam(["Leona"], ["Talon", "Sivir", "Leona", "Zed", "talon"], ["Zed"], BenchKnown: true);
        var session = new ChampionSelectSession(Mode.Caos, MayhemTeam: team);
        var cards = new[] { new MayhemDraftChoice(20, Champion("Shyvana")), new MayhemDraftChoice(40, Champion("Zed")),
            new MayhemDraftChoice(60, Champion("Renekton")) };
        var portraits = new[] { new MayhemBenchChoice(0, Champion("Trundle")) };
        Check(ChampionSelectAdvice.MayhemOptions(session, cards, portraits).Keys.SequenceEqual(["Shyvana", "Renekton"]),
            "la oferta inicial usa las cartas reconocidas y excluye los campeones bloqueados");
        var chosen = session with { Pick = new("Sivir", null, null) };
        Check(ChampionSelectAdvice.MayhemOptions(chosen, cards, portraits).Keys.SequenceEqual(["Talon"]),
            "tras elegir, el banco del cliente excluye la elección propia, aliados, bloqueos y duplicados");
        Check(ChampionSelectAdvice.MayhemOptions(chosen with { MayhemTeam = team with { Bench = [] } }, cards, portraits).Keys.Length == 0 &&
            ChampionSelectAdvice.MayhemOptions(chosen with { MayhemTeam = team with { BenchKnown = false } }, cards, portraits).Keys
                .SequenceEqual(["Trundle"]), "un banco confirmado vacío prevalece sobre el OCR y el OCR cubre la falta de datos");
        Champion Champion(string key) => catalog.Champions.Single(champion => champion.Key == key);
    }

    private static void CheckTypeHierarchy(StackPanel panel)
    {
        var text = TextBlocks(panel).ToLookup(block => block.Text);
        var title = text["Falta daño sostenido"].Single();
        var headings = new[] { "Tu campeón", "Para tu equipo", "Campeones disponibles" }
            .Select(heading => text[heading].Single()).ToArray();
        var names = text["Sivir"].Concat(text["Anivia"]).ToArray();
        Check(headings.All(heading => heading.FontSize == headings[0].FontSize && heading.FontFamily.Equals(headings[0].FontFamily)) &&
            names.All(name => name.FontSize == names[0].FontSize) &&
            title.FontSize > headings[0].FontSize && headings[0].FontSize > names[0].FontSize &&
            names[0].FontSize > text["Daño mágico a distancia"].Single().FontSize,
            "la jerarquía mantiene tamaños consistentes para el título, las secciones, los nombres y los detalles");
    }

    private static IEnumerable<string> Labels(FrameworkElement element) => TextBlocks(element).Select(label => label.Text);

    private static IEnumerable<TextBlock> TextBlocks(FrameworkElement element) => element switch
    {
        TextBlock label => [label],
        Panel panel => panel.Children.OfType<FrameworkElement>().SelectMany(TextBlocks),
        Decorator { Child: FrameworkElement child } => TextBlocks(child),
        _ => []
    };

    private static void Check(bool success, string message)
    {
        if (!success)
            throw new InvalidOperationException(message);
    }
}

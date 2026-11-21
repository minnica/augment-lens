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
        var picks = new MayhemCompositionPick[] { new("Amumu", "A", "Aporta primera línea y control"),
            new("Anivia", "S", "Aporta daño mágico y limpieza de oleadas"), new("Sona", "B", "Aporta protección al equipo") };
        var grade = new MayhemCompositionGrade("A", "Daño mixto con primera línea", 3, 5);
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
            Check(Native.IsWindowVisible(handle) && (Native.GetWindowLongW(handle, -20) & 0x08000020) == 0x08000020 &&
                Labels(panel).Contains("Campeones disponibles") && Labels(panel).Contains("Composición ARAM Caos") &&
                Labels(panel).Contains("Puesto 34 en OP.GG") && Labels(panel).Contains("Tier local · 3/5 campeones") &&
                Labels(panel).Contains(grade.Reason) &&
                picks.All(pick => Labels(panel).Contains(pick.Champion) && Labels(panel).Contains(pick.Reason)),
                "tier, evaluación y sugerencias se muestran juntos sin interceptar clics");
            CheckPlacement(bounds, .73);
            var ranking = panel.Children.OfType<Grid>().Single();
            Check(ranking.ColumnDefinitions.Count == 2 && ranking.RowDefinitions.Count == 11 &&
                ranking.Children.OfType<TextBlock>().Where(text => Grid.GetColumn(text) == 1 && Grid.GetRow(text) > 0)
                    .Select(text => text.Text).SequenceEqual(["S", "A", "A", "B", "—", "—", "—", "—", "—", "—"]) &&
                !Labels(panel).Contains("Twisted Fate"), "la tabla muestra los candidatos y sus tiers disponibles");

            var compact = new Int32Rect(bounds.X, bounds.Y, 1280, 720);
            overlay.Render(view with { Bounds = compact, Grade = grade with { Members = 5, Tier = "S" } });
            CheckPlacement(compact, .64);
            Check(Labels(panel).Contains("Tier local · 5/5 campeones") && panel.Children.OfType<Grid>().Single().RowDefinitions.Count == 11,
                "una ventana pequeña conserva la nota y las diez opciones");

            overlay.Render(view with { Selection = null });
            Check(Labels(panel).Contains("Composición ARAM Caos") && Labels(panel).Contains("Tier local · 3/5 campeones") &&
                !Labels(panel).Contains("Puesto 34 en OP.GG"), "la composición provisional aparece antes de la elección propia");

            var previousPatch = LeaguePatch.Previous(catalog.Patch) ?? catalog.Patch;
            var legacy = MayhemTeamDisplay.Create(bounds, 0, catalog, new(data with { SourcePatch = previousPatch, Ranks = null }, ""),
                "Sivir", available, "El banco está vacío.", picks, grade);
            overlay.Render(legacy);
            Check(legacy.Selection?.Tier == "B" && panel.Children.OfType<Grid>().Single().RowDefinitions.Count == 11 &&
                legacy.Rows[0] is { Champion: "Anivia", Position: null, Tier: "S" } &&
                Labels(panel).Any(line => line.Contains($"Parche anterior {LeaguePatch.Public(previousPatch)}")),
                "una copia anterior conserva el tier de las opciones aunque no traiga puestos");

            overlay.Render(view with { Rows = [], EmptyMessage = "El banco está vacío." });
            Check(!panel.Children.OfType<Grid>().Any() && Labels(panel).Contains("El banco está vacío."),
                "un banco vacío no muestra campeones ni deja filas del banco anterior");
            var loading = MayhemTeamDisplay.Create(bounds, 0, catalog, new(null, "Consultando tiers de OP.GG…"),
                null, ["Shyvana", "Renekton"], "Esperando tus cartas…", [], null);
            overlay.Render(loading);
            Check(Labels(panel).Contains("Consultando tiers de OP.GG…") && Labels(panel).Contains("Shyvana") &&
                Labels(panel).Contains("Renekton") && panel.Children.OfType<Grid>().Single().RowDefinitions.Count == 3,
                "las opciones identificadas aparecen mientras llegan sus tiers");

            overlay.Render(null);
            Check(!Native.IsWindowVisible(handle), "el panel de Caos se retira al salir de selección");
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
        var closed = ChampionSelectAdvice.MayhemOptions(chosen with { MayhemTeam = team with { PicksOpen = false } }, cards, portraits);
        Check(closed.Keys.Length == 0 && closed.EmptyMessage == "Selección cerrada.", "al cerrar los cambios se retiran las opciones");

        Champion Champion(string key) => catalog.Champions.Single(champion => champion.Key == key);
    }

    private static IEnumerable<string> Labels(FrameworkElement element) => element switch
    {
        TextBlock label => [label.Text],
        Panel panel => panel.Children.OfType<FrameworkElement>().SelectMany(Labels),
        Decorator { Child: FrameworkElement child } => Labels(child),
        _ => []
    };

    private static void Check(bool success, string message)
    {
        if (!success)
            throw new InvalidOperationException(message);
    }
}

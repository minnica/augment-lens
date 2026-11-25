using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Aumentos;

internal static class MayhemTeamOverlayTests
{
    internal static void Smoke(Catalog catalog)
    {
        var bounds = new Int32Rect(-30000, -30000, 2000, 1125);
        string[] available = ["Sona", "Shyvana", "Renekton", "Amumu", "Anivia", "Leona", "Talon", "Taric", "Galio", "MonkeyKing"];
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
        Check(view.Rows.Length == available.Length + 1 && view.Rows.Take(5).Select(row => row.Champion)
            .SequenceEqual(["Anivia", "Leona", "Amumu", "Sivir", "Sona"]) &&
            view.Rows.Skip(5).All(row => row.Tier is null) && view.Selection is { Champion: "Sivir", Position: 34, Tier: "B" },
            "Caos ordena los campeones disponibles por tier, con el puesto de OP.GG como desempate");
        CheckAvailability(catalog);

        var settings = Directory.CreateTempSubdirectory("aumentos-portrait-test-");
        var handler = new Portraits();
        using var http = new HttpClient(handler);
        var portraitCatalog = catalog with { Champions = catalog.Champions.Where(champion => champion.Key is "Sivir" or "MonkeyKing").ToArray() };
        var portraits = new ChampionSelectPortraits(portraitCatalog, settings.FullName, http);
        var overlay = new MayhemTeamOverlay(portraits) { Left = -30000, Top = -30000 };
        try
        {
            Dispatcher.CurrentDispatcher.Invoke(() => overlay.Render(view), DispatcherPriority.Normal);
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var handle = new WindowInteropHelper(overlay).Handle;
            var panel = (StackPanel)((Border)((Viewbox)overlay.Content).Child).Child;
            var labels = Labels(panel).ToArray();
            Check(Native.IsWindowVisible(handle) && (Native.GetWindowLongW(handle, -20) & 0x08000020) == 0x08000020 &&
                labels.Contains("Campeones disponibles") && !labels.Contains("Tu campeón") && labels.Count(text => text == "Sivir") == 1 &&
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
                var identity = (FrameworkElement)row.Children[0];
                var name = TextBlocks(identity).First();
                var badge = row.Children.OfType<Border>().Single(ChampionSelectTests.IsTierBadge);
                var nameCenter = name.TranslatePoint(new Point(0, name.ActualHeight / 2), panel);
                var badgeCenter = badge.TranslatePoint(new Point(badge.ActualWidth / 2, badge.ActualHeight / 2), panel);
                var teamTier = (Border)evaluation.Children[1];
                var teamCenter = teamTier.TranslatePoint(new Point(teamTier.ActualWidth / 2, 0), panel);
                Check(ChampionSelectTests.Elements(identity).OfType<Image>().Count() == 1 &&
                    Grid.GetColumn(badge) == 1 && Math.Abs(nameCenter.Y - badgeCenter.Y) <= 1 &&
                    Math.Abs(teamCenter.X - badgeCenter.X) <= 1, "cada letra se alinea con el nombre y con la columna del equipo");
            }
            var own = BankRows(panel).Single(row => Labels(row).Contains("TÚ"));
            var selectedPortrait = ChampionSelectTests.Elements(own).OfType<Image>().Single();
            var wukongPortrait = ChampionSelectTests.Elements(BankRows(panel).Single(row => Labels(row).Contains("Wukong")))
                .OfType<Image>().Single();
            WaitForPortraits(selectedPortrait, wukongPortrait);
            Check(Labels(own).Contains("Sivir") && ReadPortrait(selectedPortrait) > ReadPortrait(wukongPortrait) &&
                own.Children[0] is Border { Background: SolidColorBrush, BorderBrush: not null, BorderThickness.Left: > 0 },
                "tu campeón se distingue dentro de la tabla y los retratos corresponden al campeón aunque el alias interno sea distinto");
            handler.Offline = true;
            var cached = new ChampionSelectPortraits(portraitCatalog, settings.FullName, http);
            Check(cached.Read("Wukong").GetAwaiter().GetResult() is { IsFrozen: true } && handler.Requests == 2,
                "una sesión nueva reutiliza el retrato guardado cuando la red está desconectada");
            Check(BankRows(panel).Select(row => Labels(row).Last())
                .SequenceEqual(["S", "A", "A", "B", "B", "—", "—", "—", "—", "—", "—"]) &&
                BankRows(panel).All(row => ChampionSelectTests.Elements(row).OfType<Image>().Count() == 1) &&
                !labels.Contains("Twisted Fate"), "la lista conserva los candidatos, sus retratos y sus tiers disponibles");

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
                BankRows(panel).Count() == available.Length + 1,
                "un equipo completo de tier S conserva el banco y confirma que la composición está equilibrada");

            overlay.Render(MayhemTeamDisplay.Create(bounds, 0, catalog, new(data, ""), null,
                available, "Esperando tus cartas…", picks, grade));
            Check(Labels(panel).Contains("3/5 campeones") &&
                Labels(panel).Contains("Para tu equipo") && Labels(panel).Contains(grade.Reason) &&
                !Labels(panel).Contains("Equipo equilibrado") && !Labels(panel).Contains("TÚ"),
                "la composición provisional recupera el aviso y las sugerencias al cambiar el equipo");

            var previousPatch = LeaguePatch.Previous(catalog.Patch) ?? catalog.Patch;
            var legacy = MayhemTeamDisplay.Create(bounds, 0, catalog, new(data with { SourcePatch = previousPatch, Ranks = null }, ""),
                "Sivir", available, "El banco está vacío.", picks, grade);
            overlay.Render(legacy);
            Check(legacy.Selection?.Tier == "B" && BankRows(panel).Count() == available.Length + 1 &&
                legacy.Rows[0] is { Champion: "Anivia", Position: null, Tier: "S" } &&
                Labels(panel).Contains("Parche anterior") && !Labels(panel).Any(IsMetadata),
                "una copia anterior conserva el tier de las opciones aunque no traiga puestos");

            overlay.Render(MayhemTeamDisplay.Create(bounds, 0, catalog, new(data, ""), "Sivir", [],
                "El banco está vacío.", picks, grade));
            Check(Labels(BankRows(panel).Single()).SequenceEqual(["Sivir", "TÚ", "B"]) && Labels(panel).Contains("El banco está vacío."),
                "un banco vacío conserva solo a tu campeón en la tabla");

            overlay.Render(MayhemTeamDisplay.Create(bounds, 0, catalog, new(data, ""), "Amumu", [.. available, "Sivir"],
                "El banco está vacío.", picks, grade));
            var swapped = BankRows(panel).Select(row => Labels(row).ToArray()).ToArray();
            Check(swapped.Single(row => row.Contains("TÚ")).SequenceEqual(["Amumu", "TÚ", "A"]) &&
                swapped.Count(row => row[0] == "Amumu") == 1 && swapped.Single(row => row[0] == "Sivir").SequenceEqual(["Sivir", "B"]),
                "un intercambio actualiza el resalte y evita duplicar la nueva elección si aún figura en el banco");
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
        finally
        {
            overlay.Close();
            settings.Delete(recursive: true);
        }
    }

    private static void WaitForPortraits(params Image[] images)
    {
        var frame = new DispatcherFrame();
        long deadline = Environment.TickCount64 + 2000;
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(10), DispatcherPriority.Background, (_, _) =>
        {
            if (images.All(image => image.Source is not null) || Environment.TickCount64 >= deadline)
                frame.Continue = false;
        }, Dispatcher.CurrentDispatcher);
        Dispatcher.PushFrame(frame);
        timer.Stop();
    }

    private static byte ReadPortrait(Image portrait)
    {
        if (portrait.Source is not BitmapSource source)
            throw new InvalidOperationException("El retrato aparece después de cargar la imagen");
        var bitmap = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var pixel = new byte[4];
        bitmap.CopyPixels(new Int32Rect(24, 24, 1, 1), pixel, 4, 0);
        return pixel[0];
    }

    private sealed class Portraits : HttpMessageHandler
    {
        private readonly byte[] sprite = CreateSprite();
        internal bool Offline { get; set; }
        internal int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            if (Offline)
                throw new HttpRequestException("Sin conexión durante la lectura de la copia guardada");
            var response = request.RequestUri?.AbsolutePath switch
            {
                { } path when path.EndsWith("/champion.json", StringComparison.Ordinal) => new StringContent("""
                    {"data":{"Sivir":{"image":{"sprite":"champion0.png","x":0,"y":0,"w":48,"h":48}},
                             "MonkeyKing":{"image":{"sprite":"champion0.png","x":48,"y":0,"w":48,"h":48}}}}
                    """, Encoding.UTF8, "application/json"),
                { } path when path.EndsWith("/champion0.png", StringComparison.Ordinal) => (HttpContent)new ByteArrayContent(sprite),
                _ => throw new InvalidOperationException("Ruta de retrato inesperada")
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = response });
        }

        private static byte[] CreateSprite()
        {
            const int size = 48;
            const int width = size * 2;
            var pixels = new byte[width * size * 4];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < width; x++)
            {
                int offset = (y * width + x) * 4;
                pixels[offset] = x < size ? (byte)230 : (byte)40;
                pixels[offset + 3] = 255;
            }
            var bitmap = BitmapSource.Create(width, size, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = new MemoryStream();
            encoder.Save(output);
            return output.ToArray();
        }
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
        var headings = new[] { "Para tu equipo", "Campeones disponibles" }
            .Select(heading => text[heading].Single()).ToArray();
        var names = text["Sivir"].Concat(text["Anivia"]).ToArray();
        Check(headings.All(heading => heading.FontSize == headings[0].FontSize && heading.FontFamily.Equals(headings[0].FontFamily)) &&
            names.All(name => name.FontSize == names[0].FontSize) &&
            title.FontSize > headings[0].FontSize && headings[0].FontSize > names[0].FontSize &&
            names[0].FontSize > text["Daño mágico a distancia"].Single().FontSize,
            "la jerarquía mantiene tamaños consistentes para el título, las secciones, los nombres y los detalles");
    }

    private static IEnumerable<string> Labels(FrameworkElement element) => ChampionSelectTests.Labels(element);

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

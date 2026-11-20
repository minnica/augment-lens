using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Aumentos;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        Native.SetProcessDpiAwarenessContext(-4);
        var assets = Path.Combine(AppContext.BaseDirectory, "Assets");
        var ratings = new Ratings(assets);
        CheckHistory(ratings, legacyColumns: 11);
        CheckHistory(ratings, legacyColumns: 12);
        CheckHistory(ratings, legacyColumns: 14);
        CheckHistory(ratings, legacyColumns: 18);
        ComparisonTests.Run(ratings).GetAwaiter().GetResult();
        ArenaWikiMechanicsTests.Run(ratings, assets);
        ArenaAugmentAffinityTests.Run(ratings);
        ArenaContextReadTests.Run(ratings, assets, Option("--arena-level-shot"), Option("--arena-jax-upgrade-shot"));
        ArenaJaxOfferTests.Run(ratings);
        ArenaPrismaticTests.Run(ratings, assets, Option("--arena-prismatic-shot"), Option("--arena-prismatic-top-shot"),
            Option("--arena-prismatic-rank-shot"));
        StatShardTests.Run(ratings, assets, Option("--arena-shard-shot"));
        ArenaLegendaryTests.Run(ratings, assets, Option("--arena-legendary-shot"));
        BuildTests.Run(ratings).GetAwaiter().GetResult();
        MayhemBuildTests.Run(ratings, assets);
        ArenaLiveMatchTests.Run();
        ArenaBuildTests.Run(ratings);
        BuildVisibilityTests.Run(ratings, assets, Option("--shop-shot"), Option("--shop-closed-shot"));
        ChampionSelectTests.Run();
        MayhemChampionTests.Source(ratings.Catalog).GetAwaiter().GetResult();
        if (Option("--mayhem-draft-shot") is { } mayhemDraft)
            MayhemChampionTests.ReadOffer(ratings, assets, mayhemDraft, Option("--mayhem-locked-shot"));
        if (Option("--mayhem-pair-shot") is { } mayhemPair)
            MayhemChampionTests.ReadOffer(ratings, assets, mayhemPair, Option("--mayhem-locked-shot"), pair: true);
        if (Option("--mayhem-bench-templates") is { } benchTemplates && Option("--mayhem-locked-shot") is { } benchFirst &&
            Option("--mayhem-bench-swap-shot") is { } benchSwap)
            MayhemChampionTests.ReadBench(ratings.Catalog, benchTemplates, benchFirst, benchSwap);
        ArenaChampionTierTests.Run(ratings, Option("--arena-champion-tier-html"));
        ArenaChampionSelectionTests.Run();
        ArenaCompositionTests.Run(ratings);
        if (Option("--arena-jax-upgrade-shot") is { } fireworksShot)
        {
            var image = Diagnostics.OpenBitmap(fireworksShot);
            using var detector = new Detection(ratings, Path.Combine(assets, "tessdata"));
            var candidates = new LayoutStore(Path.Combine(assets, "layouts.json"), "").Find(Mode.Arena,
                image.PixelWidth, image.PixelHeight);
            var read = detector.ReadPanel(image, new Int32Rect(0, 0, image.PixelWidth, image.PixelHeight),
                candidates, Mode.Arena, null);
            Check(read?.Result is { } upgradeOffer && upgradeOffer.Cards[1] == ArenaUpgradeRatings.LightEmUp &&
                upgradeOffer.UpgradeLevels?[1] == 2, "reconoce el nivel 2 de ¡Enciéndelos! en el centro de la captura");
            Console.WriteLine("Nivel 2 de ¡Enciéndelos! leído de la captura de Arena.");
        }
        Check(LeaguePatch.FindInstallPath(["product_install_root: \"C:/Riot Games\"",
            "product_install_full_path: \"C:/Riot Games/League of Legends\""]) == "C:/Riot Games/League of Legends",
            "ruta de instalación según Riot");
        var defaultLayouts = new LayoutStore(Path.Combine(assets, "layouts.json"), "");
        var anvilOptions = ratings.FindTitle(Mode.Caos, "¡Estadilcas sobre Estadísticas!");
        Check(anvilOptions.Length == 2 && ratings.Recognize(Mode.Caos, "¡Estadísticas sobre Estadísticas!") is null,
            "el título compartido conserva las dos variantes posibles");
        Check(Detection.ChooseByAnvils(anvilOptions, "¡Obtienes 3 Yunques de Estadísticas!")?.Id == 1403 &&
            Detection.ChooseByAnvils(anvilOptions, "¡Obtienes 4 Yunques de Estadísticas!")?.Id == 1402 &&
            Detection.ChooseByAnvils(anvilOptions, "Yunques de Estadísticas") is null &&
            Detection.ChooseByAnvils(anvilOptions, "3 Yunques de Estadísticas y 4 Yunques de Estadísticas") is null,
            "la cantidad de yunques distingue las variantes y una lectura incierta sigue sin asignación");
        if (Option("--caos-anvil-shot") is { } anvilShot)
            CheckAnvilScreenshot(anvilShot, assets, ratings, defaultLayouts);
        var fullScreen = defaultLayouts.Find(Mode.Arena, 2560, 1440).Single();
        Check(fullScreen.Titles.Length == 3 && fullScreen.Titles[1].Pixels(2560, 1440).Y is >= 530 and <= 570,
            "calibración adaptada a 2560 × 1440");
        Check(ReferenceEquals(fullScreen, defaultLayouts.Find(Mode.Arena, 2560, 1440).Single()),
            "la calibración adaptada conserva el estado entre lecturas");
        Check(LiveGame.ParseMode("KIWI") == Mode.Caos && LiveGame.ParseMode("CHERRY") == Mode.Arena
            && LiveGame.ParseMode("ARAM") is null, "detección de modos");
        using var players = JsonDocument.Parse("""
            [{"riotId":"other#000","rawChampionName":"game_character_displayname_Brand"},
             {"riotId":"player#000","rawChampionName":"game_character_displayname_DrMundo"}]
            """);
        Check(LiveGame.ParseMatch(Mode.Caos, players.RootElement, "player#000")?.Game.Champion == "DrMundo",
            "campeón del jugador activo");
        var arena = ratings.Recognize(Mode.Arena, "Misión: Endurece tu corazón");
        var reading = ratings.Recognize(Mode.Caos, " ENDURECE   TU CORAZÓN ");
        Check(arena is not null && reading is not null && reading.Mode == "Caos", "catálogo y normalización por modo");
        Check(ratings.Recognize(Mode.Caos, "Qxzblw") is null, "texto ambiguo o desconocido");
        Check(ratings.Recognize(Mode.Caos, "Pan y") is null, "títulos parciales");
        Check(ratings.Rate(Mode.Caos, "DrMundo", reading!.Id) == Tier.S, "prioridad de la excepción editorial");
        var bees = ratings.Recognize(Mode.Caos, "¡¡¡ABEJAS!!!");
        Check(bees is not null && ratings.Rate(Mode.Caos, "Lux", bees.Id) == Tier.A, "Abejas con Lux en ARAM Caos");
        Check(ratings.Rate(Mode.Arena, "DrMundo", -1) is null, "aumento sin valoración");
        var cards = ratings.Catalog.Augments.Where(card => card.Mode == "Arena").ToArray();
        Check(cards.GroupBy(card => ratings.Rate(Mode.Arena, "Jinx", card.Id)).Any(group => group.Count() >= 3),
            "empates permitidos");
        Check(ratings.Rate(Mode.Caos, "Lux", 3071) is null, "carta sin regla queda sin valorar");

        var offer = new OfferState();
        offer.Set(0, 2, null);
        offer.Set(1, 3, null);
        offer.Set(0, null, null);
        Check(offer[0] is null && offer[1] == 3, "retiro de tier durante reroll individual");
        offer.Clear();
        Check(offer[1] is null, "limpieza del panel");
        Console.WriteLine($"Catálogo {ratings.Catalog.Patch}: {ratings.Catalog.Champions.Length} campeones, "
            + $"{ratings.Catalog.Augments.Length} aumentos.");

        if (args.Contains("--live"))
        {
            using var game = new LiveGame(Path.Combine(assets, "riotgames.pem"));
            var data = game.Read(CancellationToken.None).GetAwaiter().GetResult();
            Check(data is not null, "conexión TLS a la API de partida");
            Console.WriteLine($"API en partida: {data!.Mode} · {data.Champion}");
            Console.WriteLine($"Parche del ejecutable instalado: {LeaguePatch.Installed()}");
            foreach (var process in Process.GetProcessesByName("League of Legends"))
            {
                using (process)
                {
                    if (process.MainWindowHandle == 0) continue;
                    var frame = Native.Bounds(process.MainWindowHandle);
                    var client = Native.ClientBounds(process.MainWindowHandle, frame);
                    Console.WriteLine($"Tamaño de captura: {frame.Width} × {frame.Height}. Área del juego: {client.Width} × {client.Height}.");
                    CheckGameFrame(process.MainWindowHandle, frame, client, data.Mode, assets, ratings);
                }
            }
        }
        if (args.Contains("--capture-smoke"))
        {
            CaptureSmoke();
            BuildOverlaySmoke();
            BuildSizeControlSmoke(ratings.Catalog.Patch);
            BuildSizeButtonSmoke(ratings.Catalog.Patch);
            ChampionSelectTests.Smoke();
            MayhemChampionTests.Smoke(ratings.Catalog);
        }
        if (args.Contains("--capture-smoke") || args.Contains("--card-overlay-smoke"))
        {
            OverlaySmoke(assets);
            ArenaItemTierSizeTests.Smoke();
        }
        if (args.Contains("--capture-smoke") || args.Contains("--arena-prismatic-top-smoke"))
            PrismaticOverlaySmoke(ratings, assets);
        if (args.Contains("--capture-smoke") || args.Contains("--arena-build-overlay-smoke"))
        {
            ArenaBuildOverlayTests.Run(ratings);
            ArenaBuildOverlaySmoke(ratings);
        }
        if (args.Contains("--capture-smoke") || args.Contains("--arena-champion-overlay-smoke"))
            ChampionSelectTests.ArenaSmoke(ratings.Catalog);
        if (Option("--arena-build-snapshot") is { } arenaSnapshot)
        {
            using var data = JsonDocument.Parse(File.ReadAllText(arenaSnapshot));
            var match = LiveGame.ParseArenaMatch(data.RootElement);
            var plan = match is null ? null : new ArenaBuilds(ratings).Recommend(match.Game, match.Arena, null);
            Check(match?.Arena?.Opponents.Length > 0 && plan?.Steps.Length == 6,
                "un snapshot real de Arena genera la ruta con inventario y rivales reconocidos");
            Console.WriteLine(string.Join(" | ", plan!.Steps.Select(step => $"{step.Item.Name} ({step.Detail})")));
        }
        var samples = new List<SavedLayout>();
        ArenaFlickerTests.Run(ratings, assets, Option("--arena-ivern-shot"));
        if (Option("--caos") is { } caosPath)
            samples.Add(CheckScreenshot(caosPath, Mode.Caos, assets, ratings));
        if (Option("--caos-live-shot") is { } caosLiveShot)
        {
            var image = Diagnostics.OpenBitmap(caosLiveShot);
            using var detector = new Detection(ratings, Path.Combine(assets, "tessdata"));
            var candidates = defaultLayouts.Find(Mode.Caos, image.PixelWidth, image.PixelHeight);
            var read = detector.ReadPanel(image, new Int32Rect(0, 0, image.PixelWidth, image.PixelHeight), candidates, Mode.Caos, null);
            Check(read is { Layout.Titles.Length: 3 } && read.Value.Result.Cards.SequenceEqual(new int?[] { 1005, 1384, 1149 }),
                "selección de la distribución correcta para image5.png");
            Console.WriteLine($"Tres cartas de ARAM Caos: {string.Join(" / ", read!.Value.Result.Texts)}");
            var visual = new DrawingVisual();
            using (var drawing = visual.RenderOpen())
                drawing.DrawImage(image, new Rect(0, 0, 2560, 1440));
            var gameFrame = new RenderTargetBitmap(2560, 1440, 96, 96, PixelFormats.Pbgra32);
            gameFrame.Render(visual);
            var liveFormat = new FormatConvertedBitmap(gameFrame, PixelFormats.Bgr32, null, 0);
            detector.Clear();
            var full = detector.ReadPanel(liveFormat, new Int32Rect(0, 0, 2560, 1440),
                defaultLayouts.Find(Mode.Caos, 2560, 1440), Mode.Caos, null);
            Check(full is { Layout.Titles.Length: 3 } && full.Value.Result.Cards.SequenceEqual(read.Value.Result.Cards),
                "reproducción del panel de tres cartas con el formato de captura en vivo");
            var labels = full!.Value.Result.Cards.Select(card => ratings.Rate(Mode.Caos, "Janna", card!.Value)?.ToString()).ToArray();
            Console.WriteLine($"Tiers de Janna: {string.Join(" / ", labels)}");
        }
        if (Option("--caos-four-live-shot") is { } fourLiveShot)
        {
            var image = Diagnostics.OpenBitmap(fourLiveShot);
            var candidates = defaultLayouts.Find(Mode.Caos, 2560, 1440);
            var visual = new DrawingVisual();
            using (var drawing = visual.RenderOpen())
            {
                drawing.DrawImage(image, new Rect(0, 0, 2560, 1440));
                // A missing button must not stop the live reader from recognizing the card titles.
                drawing.DrawRectangle(Brushes.Black, null, new Rect(1080, 1080, 400, 145));
            }
            var frame = new RenderTargetBitmap(2560, 1440, 96, 96, PixelFormats.Pbgra32);
            frame.Render(visual);
            var capture = new FormatConvertedBitmap(frame, PixelFormats.Bgr32, null, 0);
            using var detector = new Detection(ratings, Path.Combine(assets, "tessdata"));
            var area = new Int32Rect(0, 0, 2560, 1440);
            Check(candidates.All(layout => !detector.PanelVisible(capture, area, layout)), "señal del botón ausente");
            var read = detector.ReadPanel(capture, area, candidates, Mode.Caos, null);
            Check(read is { Layout.Titles.Length: 4 } && read.Value.Result.Cards.SequenceEqual(new int?[] { 2098, 1088, 2087, 2062 }),
                "cuatro cartas con la señal del botón ausente");
            var tiers = read!.Value.Result.Cards.Select(id => ratings.Rate(Mode.Caos, "Lux", id!.Value)).ToArray();
            Check(tiers.SequenceEqual(new Tier?[] { null, null, Tier.A, Tier.S }), "tiers de Lux");
            Console.WriteLine($"Cuatro cartas de Lux sin depender del botón: {string.Join(" / ", tiers)}");
        }
        if (Option("--arena") is { } arenaPath)
        {
            samples.Add(CheckScreenshot(arenaPath, Mode.Arena, assets, ratings));
            CheckFullscreenScreenshot(arenaPath, fullScreen, assets, ratings);
        }
        if (Option("--arena-live-shot") is { } liveShot)
        {
            var evidence = Diagnostics.OpenBitmap(liveShot);
            using var detector = new Detection(ratings, Path.Combine(assets, "tessdata"));
            var scan = fullScreen.ScanArea();
            var visual = new DrawingVisual();
            using (var drawing = visual.RenderOpen())
                drawing.DrawImage(evidence, new Rect(0, 0, fullScreen.Width, fullScreen.Height));
            var resized = new RenderTargetBitmap(fullScreen.Width, fullScreen.Height, 96, 96, PixelFormats.Pbgra32);
            resized.Render(visual);
            var result = detector.Scan(new CroppedBitmap(resized, scan), scan, fullScreen, Mode.Arena);
            Check(result.Cards.SequenceEqual(new int?[] { 97, 1034, 120 }), "tres aumentos de la ronda real a 2560 × 1440");
            var labels = result.Cards.Select(card => ratings.Rate(Mode.Arena, "Elise", card!.Value)?.ToString()).ToArray();
            Console.WriteLine($"Ronda de image3.png: {string.Join(" / ", labels)}");
        }
        if (Option("--arena-upgrade") is { } upgradePath)
        {
            var image = Diagnostics.OpenBitmap(upgradePath);
            var layout = defaultLayouts.Find(Mode.Arena, image.PixelWidth, image.PixelHeight).Single();
            using var detector = new Detection(ratings, Path.Combine(assets, "tessdata"));
            var scan = layout.ScanArea();
            var result = detector.Scan(new CroppedBitmap(image, scan), scan, layout, Mode.Arena);
            for (int i = 0; i < result.Texts.Length; i++)
                Console.WriteLine($"Mejora de Arena: {result.Texts[i]} · {result.Cards[i]}");
            Check(result.CanShow(false) && result.Cards.SequenceEqual(new int?[] { 364, 1034, 180 }), "lectura del panel de mejora");
            var options = CardComparison.ForOffer(ratings, new GameContext(Mode.Arena, "Elise"),
                result.Cards, result.Items, null);
            Check(options.Select(item => item?.Local).SequenceEqual(["Variable", "Sin dato", "Sin dato"]),
                "se distinguen reemplazo y mejoras sin valoración");
        }
        if (Option("--items") is { } itemsPath)
        {
            var arenaLayout = samples.Single(item => item.Mode == Mode.Arena).Calibration;
            CheckItemScreenshot(itemsPath, arenaLayout, assets, ratings);
        }
        if (Option("--save-layouts") is { } savePath && samples.Count == 2)
        {
            var fourCards = samples.Single(item => item.Mode == Mode.Caos).Calibration;
            var spacing = fourCards.Titles[1].X - fourCards.Titles[0].X;
            var threeCards = fourCards.Titles.Take(3).Select(title => title with { X = title.X + spacing / 2 }).ToArray();
            samples.Add(new SavedLayout(Mode.Caos, fourCards with { Titles = threeCards }));
            File.WriteAllText(savePath, JsonSerializer.Serialize(samples, Ratings.JsonOptions));
        }
        Console.WriteLine("Pruebas completadas.");
        return;

        string? Option(string key)
        {
            int index = Array.IndexOf(args, key);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }
    }

    private static void CheckAnvilScreenshot(string file, string assets, Ratings ratings, LayoutStore layouts)
    {
        var image = Diagnostics.OpenBitmap(file);
        var bounds = new Int32Rect(0, 0, image.PixelWidth, image.PixelHeight);
        var layout = layouts.Find(Mode.Caos, bounds.Width, bounds.Height).Single(item => item.Titles.Length == 3);
        using var detector = new Detection(ratings, Path.Combine(assets, "tessdata"));
        var original = detector.Scan(image, bounds, layout, Mode.Caos, panelConfirmed: true);
        Check(original.Cards[0] == 1403 && original.Cards.All(card => card.HasValue), "variante dorada en la captura original");

        var title = layout.Titles[0].Pixels(bounds.Width, bounds.Height);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawImage(image, new Rect(0, 0, bounds.Width, bounds.Height));
            var description = new Rect(title.X, title.Y + 2 * title.Height, title.Width, 2 * title.Height);
            drawing.DrawRectangle(Brushes.Black, null, description);
            var text = new FormattedText("4 Yunques de Estadísticas", CultureInfo.GetCultureInfo("es-MX"),
                FlowDirection.LeftToRight, new Typeface("Georgia"), title.Height / 3.0, Brushes.White, 1);
            drawing.DrawText(text, new Point(description.X + (description.Width - text.Width) / 2, description.Y + 6));
        }
        var rerolled = new RenderTargetBitmap(bounds.Width, bounds.Height, 96, 96, PixelFormats.Pbgra32);
        rerolled.Render(visual);
        var changed = detector.Scan(rerolled, bounds, layout, Mode.Caos, panelConfirmed: true);
        Check(changed.Cards[0] == 1402 && changed.Cards.Skip(1).SequenceEqual(original.Cards.Skip(1)),
            "un reroll con el mismo título cambia solo la carta cuya descripción cambió");
        Console.WriteLine("Captura de Estadísticas sobre Estadísticas y reroll verificados.");
    }

    private static void CheckHistory(Ratings ratings, int legacyColumns)
    {
        var folder = Path.Combine(Path.GetTempPath(), $"AumentosHistory-{Guid.NewGuid():N}");
        var history = new CardHistory(folder, ratings);
        try
        {
            Directory.CreateDirectory(folder);
            var headerSuffix = legacyColumns >= 12 ? ",opgg" : "";
            var rowSuffix = legacyColumns >= 12 ? ",A" : "";
            var savedSuffix = legacyColumns >= 12 ? ",\"A\"" : "";
            if (legacyColumns >= 14)
            {
                headerSuffix += ",arammayhem_popularidad_global_pct,arammayhem_parche";
                rowSuffix += ",24.6,26.21";
                savedSuffix += ",\"24.6\",\"26.21\"";
            }
            if (legacyColumns == 18)
            {
                headerSuffix += ",ggez_eleccion_pct,ggez_veces_elegido,ggez_partidas,ggez_parche";
                rowSuffix += ",2.5,25,1000,26.22";
                savedSuffix += ",\"2.5\",\"25\",\"1000\",\"26.22\"";
            }
            File.WriteAllLines(history.FilePath, [
                "fecha,modo,campeon,parche,partida,seleccion,posicion,id_aumento,aumento,tier,origen" + headerSuffix,
                "2026-11-18,Arena,Elise,16.22,,1,2,57,\"Texto, con coma\",B,Partida" + rowSuffix
            ]);
            CardComparison?[] original = [new("B"), new("B"), CardComparison.Unread, new("S")];
            CardComparison?[] reroll = [new("B"), new("A", new OpGgCard("S"), Mayhem: new(12.5m, "26.22", true)),
                CardComparison.Unread, new("S")];
            history.Observe(4, Mode.Caos, "Lux", [2098, 1088, null, 2062], original, 0);
            history.Observe(4, Mode.Caos, "Lux", [2098, 1088, null, 2062], original, 300);
            history.Observe(4, Mode.Caos, "Lux", [null, 1088, null, 2062], original, 500);
            history.Observe(4, Mode.Caos, "Lux", [2098, 2087, null, 2062], reroll, 1000);
            history.ObserveClosed(4, 2000);
            history.Observe(4, Mode.Caos, "Lux", [2098, 2087, null, 2062], reroll, 3400);
            var ggez = new GgezArenaSnapshot("Elise", ratings.Catalog.Patch, ratings.Catalog.Patch,
                DateTimeOffset.UtcNow, 100, new() { [1034] = new(10, 10) });
            history.Import(DateTimeOffset.Now, Mode.Arena, "Elise", "image4.png", [364, 1034, 180], ggez: ggez,
                upgradeLevels: [null, 2, 2]);
            history.Flush();
            history.Flush();
            var lines = File.ReadAllLines(history.FilePath);
            Check(lines[0].EndsWith(",ggez_parche,arena_nivel_aumento,arena_fuente_mecanica,arena_motivo") &&
                lines[1].EndsWith("\"Texto, con coma\",\"B\",\"Partida\"" + savedSuffix + new string(',', 21 - legacyColumns)),
                "la columna nueva conserva los valores del historial anterior");
            Check(lines.Length == 12, "historial sin duplicados entre fotogramas y con reroll y segunda selección");
            Check(lines.Single(row => row.Contains("\"1\",\"2\",\"2087\""))
                .EndsWith("\"Archimago\",\"A\",\"Partida\",\"S\",\"12.5\",\"26.22\",\"\",\"\",\"\",\"\",\"\",\"\",\"\""),
                "se guardan las valoraciones y el parche de la popularidad global");
            Check(lines.Single(row => row.Contains("\"1034\",\"Velo del Centinela\""))
                .Contains($"\"10\",\"10\",\"100\",\"{LeaguePatch.Public(ratings.Catalog.Patch)}\",\"2\""),
                "se guardan porcentaje, muestra y nivel de Arena");
            Check(lines.Any(row => row.Contains("\"2\",\"1\",\"2098\"")) &&
                lines.Any(row => row.Contains("\"Reemplazar Aumento\",\"Variable\",\"Captura · tier recalculado · image4.png\"")),
                "selecciones nuevas y origen de captura");
            history.ResetMatch();
            history.Observe(4, Mode.Arena, "Jax", [87], [new("A", IsArenaAugment: true,
                LocalReason: "Efectos al impacto", UsesWiki: true)], 4000);
            history.Flush();
            var last = File.ReadLines(history.FilePath).Last();
            Check(last.Contains("\"1\",\"1\",\"87\"") &&
                last.EndsWith("\"\",\"League of Legends Wiki\",\"Efectos al impacto\""),
                "la partida nueva reinicia la selección y guarda el fundamento de la valoración");
            Console.WriteLine("Historial de cartas verificado.");
        }
        finally
        {
            if (File.Exists(history.FilePath)) File.Delete(history.FilePath);
            if (Directory.Exists(folder)) Directory.Delete(folder);
        }
    }

    private static SavedLayout CheckScreenshot(string file, Mode mode, string assets, Ratings ratings)
    {
        var image = Diagnostics.OpenBitmap(file);
        var isCaos = mode == Mode.Caos;
        Check(isCaos ? image.PixelWidth == 2512 && image.PixelHeight == 1310
            : image.PixelWidth == 2525 && image.PixelHeight == 1317, "dimensiones de la captura de referencia");
        Region Box(int x, int y, int width, int height) =>
            new(x / (double)image.PixelWidth, y / (double)image.PixelHeight,
                width / (double)image.PixelWidth, height / (double)image.PixelHeight);
        var marker = isCaos ? Box(1142, 1018, 238, 52) : Box(1147, 1018, 238, 52);
        Region[] titles = isCaos
            ? [Box(413, 510, 332, 49), Box(868, 510, 332, 49), Box(1321, 510, 332, 49), Box(1774, 510, 332, 49)]
            : [Box(661, 493, 332, 49), Box(1097, 493, 332, 49), Box(1535, 493, 332, 49)];
        var calibration = new Calibration(image.PixelWidth, image.PixelHeight, marker, titles,
            Detection.Signature(image, marker.Pixels(image.PixelWidth, image.PixelHeight)));
        var scan = calibration.ScanArea();
        using var detector = new Detection(ratings, Path.Combine(assets, "tessdata"));
        var timer = Stopwatch.StartNew();
        var result = detector.Scan(new CroppedBitmap(image, scan), scan, calibration, mode);
        timer.Stop();
        string[] expected = isCaos ? ["Conducto Infernal", "Universidad de Payasos", "Pacto Ominoso", "Maestro de la Dualidad"]
            : ["Ruleta Definitiva", "Batido de Proteínas", "Yunque de Gamba"];
        for (int index = 0; index < expected.Length; index++)
        {
            var card = ratings.Catalog.Augments.FirstOrDefault(card => card.Mode == mode.ToString() && card.Id == result.Cards[index]);
            Console.WriteLine($"{result.Texts[index]} -> {card?.Name}");
            Check(card?.Name == expected[index], $"OCR de {expected[index]}");
        }
        Console.WriteLine($"OCR inicial: {timer.ElapsedMilliseconds} ms.");

        var original = new FormatConvertedBitmap(image, PixelFormats.Bgr32, null, 0);
        var replacement = titles[1].Pixels(image.PixelWidth, image.PixelHeight);
        var first = titles[0].Pixels(image.PixelWidth, image.PixelHeight);
        var stride = replacement.Width * 4;
        var textPixels = new byte[stride * replacement.Height];
        original.CopyPixels(replacement, textPixels, stride, 0);
        var rerolled = new WriteableBitmap(original);
        rerolled.WritePixels(first, textPixels, stride, 0);
        var after = detector.Scan(new CroppedBitmap(rerolled, scan), scan, calibration, mode);
        Check(after.Cards[0] == result.Cards[1] && after.Cards.Skip(1).SequenceEqual(result.Cards.Skip(1)),
            "reconocimiento de un solo reroll sobre la captura");

        var blank = BitmapSource.Create(scan.Width, scan.Height, 96, 96,
            PixelFormats.Gray8, null, new byte[scan.Width * scan.Height], scan.Width);
        Check(!detector.Scan(blank, scan, calibration, mode).PanelVisible, "panel ausente");
        return new SavedLayout(mode, calibration);
    }

    private static void CheckItemScreenshot(string file, Calibration reference, string assets, Ratings ratings)
    {
        var image = Diagnostics.OpenBitmap(file);
        var layout = reference with { Width = image.PixelWidth, Height = image.PixelHeight };
        var scan = layout.ScanArea();
        using var detector = new Detection(ratings, Path.Combine(assets, "tessdata"));
        var result = detector.Scan(new CroppedBitmap(image, scan), scan, layout, Mode.Arena);
        Check(result.PanelVisible, "mismo diseño del panel para objetos y aumentos de Arena");
        foreach (var (title, index) in result.Texts.Select((title, index) => (title, index)))
            Console.WriteLine($"Panel de objetos: {title} · {(result.Items[index] ? "objeto" : "sin coincidencia")}");
        Check(result.Items.All(item => item), "identificación de las tres cartas de objetos");
        Check(!result.CanShow(false) && !result.CanShow(true) &&
            ratings.Prismatics.ForOffer(new GameContext(Mode.Arena, "Elise"), result)?.Badges.All(badge => badge?.ArenaChoice is not null) == true,
            "las cartas de objetos reciben tiers prismáticos separados del overlay de aumentos");
    }

    private static void CheckFullscreenScreenshot(string file, Calibration layout, string assets, Ratings ratings)
    {
        var original = Diagnostics.OpenBitmap(file);
        var width = original.PixelWidth / (double)original.PixelHeight * layout.Height;
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
            drawing.DrawImage(original, new Rect((layout.Width - width) / 2, 0, width, layout.Height));
        var scaled = new RenderTargetBitmap(layout.Width, layout.Height, 96, 96, PixelFormats.Pbgra32);
        scaled.Render(visual);
        using var detector = new Detection(ratings, Path.Combine(assets, "tessdata"));
        var scan = layout.ScanArea();
        var result = detector.Scan(new CroppedBitmap(scaled, scan), scan, layout, Mode.Arena);
        Check(result.PanelVisible && result.Cards.All(card => card.HasValue), "OCR de Arena a 2560 × 1440");
        Console.WriteLine("Calibración de Arena verificada a 2560 × 1440 con la captura de referencia adaptada.");
    }

    private static void CheckGameFrame(nint window, Int32Rect frame, Int32Rect client, Mode mode, string assets, Ratings ratings)
    {
        var candidates = new LayoutStore(Path.Combine(assets, "layouts.json"), "").Find(mode, client.Width, client.Height);
        if (candidates.Length == 0)
            return;
        using var source = new WindowCapture(window);
        using var detector = new Detection(ratings, Path.Combine(assets, "tessdata"));
        var timer = Stopwatch.StartNew();
        var all = Read(new Int32Rect(client.X - frame.X, client.Y - frame.Y, client.Width, client.Height));
        foreach (var layout in candidates)
        {
            detector.Clear();
            var scan = layout.ScanArea();
            var result = detector.Scan(new CroppedBitmap(all, scan), scan, layout, mode);
            Console.WriteLine($"Fotograma completo · {layout.Titles.Length} huecos · panel: {result.PanelVisible} · {string.Join(" / ", result.Texts)}");
            detector.Clear();
            var region = new Int32Rect(scan.X + client.X - frame.X, scan.Y + client.Y - frame.Y, scan.Width, scan.Height);
            var patch = Read(region);
            var cropped = detector.Scan(patch, scan, layout, mode);
            Console.WriteLine($"Recorte directo · {layout.Titles.Length} huecos · panel: {cropped.PanelVisible} · {string.Join(" / ", cropped.Texts)}");
        }
        return;

        BitmapSource Read(Int32Rect rectangle)
        {
            timer.Restart();
            while (timer.ElapsedMilliseconds < 2500)
            {
                if (source.Read(rectangle) is { } bitmap)
                    return bitmap;
                Thread.Sleep(50);
            }
            throw new InvalidOperationException("No se recibió el recorte de la ventana de League.");
        }
    }

    private static void CaptureSmoke()
    {
        var window = new Window
        {
            WindowStyle = WindowStyle.None, Width = 240, Height = 160, Left = 10, Top = 10,
            ShowActivated = false, ShowInTaskbar = false, Background = Brushes.Red
        };
        window.Show();
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            using var capture = new WindowCapture(handle);
            ReadColor(() => capture.Read(new Int32Rect(15, 15, 20, 25)), 2);
            window.Background = Brushes.Blue;
            ReadColor(() => capture.ReadProbe(new Int32Rect(15, 15, 40, 5)), 0);
            window.Background = Brushes.Red;
            ReadColor(() => capture.Read(new Int32Rect(15, 15, 20, 25)), 2);
            Console.WriteLine("Captura de ventana y recortes D3D11 verificados.");

            static void ReadColor(Func<BitmapSource?> read, int channel)
            {
                var timer = Stopwatch.StartNew();
                while (timer.ElapsedMilliseconds < 3000)
                {
                    Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                    if (read() is { } pixels)
                    {
                        var color = new byte[4];
                        pixels.CopyPixels(new Int32Rect(2, 2, 1, 1), color, 4, 0);
                        if (color[channel] > 200 && color[2 - channel] < 50 && color[1] < 50)
                            return;
                    }
                    Thread.Sleep(30);
                }
                throw new InvalidOperationException("No se recibió el color esperado en el recorte D3D11.");
            }
        }
        finally { window.Close(); }
    }

    private static void Check(bool result, string description)
    {
        if (!result)
            throw new InvalidOperationException($"Falló: {description}");
    }

    private static void BuildOverlaySmoke()
    {
        using var http = new System.Net.Http.HttpClient(new TestIcons());
        var icons = new BuildItemIcons(http);
        var overlay = new BuildOverlay(icons);
        try
        {
            var bounds = new Int32Rect(10, 10, 1040, 720);
            var data = new OpGgBuildSnapshot(Mode.Caos, "Zyra", "16.23", "16.23", DateTimeOffset.UtcNow,
                [new(OpGgBuildSection.Core, [[new(6653, "El Tormento de Liandry"), new(3116, "Cetro de Cristal de Rylai")]])]);
            foreach (var item in data.Groups[0].Choices[0])
                Check(icons.Read(data.SourcePatch, item.Id).GetAwaiter().GetResult() is { PixelWidth: 1, PixelHeight: 1 },
                    "icono PNG descargado");
            overlay.Render(new BuildDisplay(bounds, 0, "Zyra", Mode.Caos, new(data, "Prueba")));
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var handle = new WindowInteropHelper(overlay).Handle;
            var placement = Native.Bounds(handle);
            Check(Native.IsWindowVisible(handle) &&
                placement.X > bounds.X + bounds.Width / 2 && placement.X + placement.Width <= bounds.X + bounds.Width &&
                placement.Y >= bounds.Y && placement.Y + placement.Height <= bounds.Y + bounds.Height &&
                (Native.GetWindowLongW(handle, -20) & 0x08000020) == 0x08000020,
                "build visible dentro del juego, sin tomar el foco ni bloquear clics");
            Check(BuildPanel.ChoiceText(OpGgBuildSection.Core, data.Groups[0].Choices[0])
                .StartsWith("El Tormento de Liandry", StringComparison.Ordinal), "orden de objetos en la ruta");
            var shopPanel = (Border)((Viewbox)overlay.Content).Child;
            var rows = (StackPanel)shopPanel.Child;
            var route = rows.Children.OfType<UniformGrid>().Single();
            var displayedIcons = BuildElements<Image>(route).ToArray();
            Check(displayedIcons.Length == 2 && displayedIcons.All(icon => icon.Source is BitmapSource) &&
                displayedIcons.Select(icon => System.Windows.Automation.AutomationProperties.GetName(icon))
                    .SequenceEqual(["El Tormento de Liandry", "Cetro de Cristal de Rylai"]) &&
                BuildElements<TextBlock>(route).Select(text => text.Text)
                    .SequenceEqual(["1.", "El Tormento de Liandry", "2.", "Cetro de Cristal de Rylai"]),
                "iconos y pasos numerados en el orden de compra");

            var plan = new MayhemBuildPlan([
                new(new(6653, "El Tormento de Liandry"), "Comprado", Owned: true), new(new(3020, "Botas del Hechicero")),
                new(new(3116, "Cetro de Cristal de Rylai")), new(new(3089, "Sombrero Mortífero de Rabadon")),
                new(new(3135, "Báculo del Vacío"), "Contra 2 rivales con objetos de resistencia mágica"),
                new(new(3165, "Morellonomicón"), "Para reducir la curación rival")
            ]);
            var build = data with { Groups = [new(OpGgBuildSection.Start, [[new(1056, "Sortija de Doran")]]),
                new(OpGgBuildSection.Boots, [[new(3020, "Botas del Hechicero")]]), ..data.Groups] };
            var view = new BuildDisplay(bounds, 0, "Zyra", Mode.Caos, new(build, "Prueba"), Plan: plan, EnemyDamage: new(78));
            overlay.Render(view);
            var next = rows.Children.OfType<Border>().Single();
            var nextRow = (DockPanel)next.Child;
            var nextLabels = PurchaseText(nextRow);
            var after = rows.Children.OfType<DockPanel>().ToArray();
            var boughtRow = rows.Children.OfType<UniformGrid>().First();
            var bought = boughtRow.Children.OfType<StackPanel>().Single();
            Check(nextRow.Children.OfType<Border>().Single().Width > after[0].Children.OfType<Border>().Single().Width &&
                nextLabels.Select(text => text.Text).SequenceEqual(["Siguiente compra", "Botas del Hechicero"]) &&
                nextLabels[1].FontSize > PurchaseText(after[0])[0].FontSize &&
                rows.Children[0] == next &&
                rows.Children.IndexOf(next) < rows.Children.IndexOf(boughtRow) &&
                bought.Children.OfType<TextBlock>().Single().Text == "El Tormento de Liandry" &&
                after.Select(row => PurchaseText(row).Single().Text).SequenceEqual(plan.Steps.Skip(2).Select(step => step.Item.Name)) &&
                rows.Children.OfType<TextBlock>().Any(text => text.Text == "Equipados · 1 de 6"),
                "ARAM Caos destaca la primera compra pendiente por encima del inventario y las referencias");
            CheckDamageBar(rows, 78);
            Check(rows.Children.OfType<TextBlock>().Any(text => text.Text == "Objetos iniciales"),
                "las referencias iniciales aparecen antes de comprar");
            view = view with { HideStartingItems = true };
            overlay.Render(view);
            Check(rows.Children.OfType<TextBlock>().All(text => text.Text != "Objetos iniciales") &&
                rows.Children.OfType<TextBlock>().Any(text => text.Text == "Botas") && rows.Children.OfType<UniformGrid>().Count() == 2 &&
                PurchaseText((DockPanel)rows.Children.OfType<Border>().Single().Child)[1].Text == "Botas del Hechicero",
                "al terminar las compras iniciales desaparece solo esa sección y se mantiene la siguiente compra");
            overlay.Render(view with { EnemyDamage = new(60) });
            CheckDamageBar(rows, 60);
            overlay.Render(view with { EnemyDamage = new(0) });
            CheckDamageBar(rows, 0);
            overlay.Render(view with { EnemyDamage = new(100) });
            CheckDamageBar(rows, 100);
            overlay.Render(view with { Plan = new MayhemBuildPlan([plan.Steps[0], plan.Steps[1] with { Owned = true }, ..plan.Steps.Skip(2)]) });
            var updatedNext = (DockPanel)rows.Children.OfType<Border>().Single().Child;
            Check(PurchaseText(updatedNext)[1].Text == "Cetro de Cristal de Rylai" &&
                rows.Children.OfType<TextBlock>().Any(text => text.Text == "Equipados · 2 de 6"),
                "la siguiente compra avanza cuando el inventario confirma la recomendación anterior");

            double dpi = VisualTreeHelper.GetDpi(overlay).DpiScaleX;
            int Pixels(int dips) => (int)Math.Round(dips * dpi);
            var compactBounds = new Int32Rect(-30000, -30000, Pixels(1040), Pixels(640));
            var shop = new Int32Rect(Pixels(220), Pixels(50), Pixels(560), Pixels(520));
            var core = plan.Steps.Select(step => step.Item).ToArray();
            var alternative = new[] { core[0], new OpGgBuildItem(3003, "Báculo del Arcángel"), core[2] };
            var withAlternative = view with { Source = new(build with
            {
                Groups = [build.Groups[1], new(OpGgBuildSection.Core, [core, alternative])]
            }, "Prueba") };
            overlay.Render(withAlternative);
            Check(HasRouteLabel(), "la ruta alternativa aparece mientras falten objetos de esa ruta");
            overlay.Render(withAlternative with { Plan = new MayhemBuildPlan([
                plan.Steps[0], new(new(3040, "Abrazo del Serafín"), Owned: true),
                plan.Steps[2] with { Owned = true }, ..plan.Steps.Skip(3)
            ]) });
            Check(!HasRouteLabel(), "la ruta alternativa se oculta al tener sus objetos, incluidas sus mejoras");

            var equipped = plan.Steps.Select(step => step with { Owned = true }).ToArray();
            overlay.Render(withAlternative with { Plan = new MayhemBuildPlan(equipped) });
            Check(!HasRouteLabel() && rows.Children.OfType<TextBlock>().Any(text => text.Text == "Build completa"),
                "una build completa oculta la ruta alternativa aunque falten objetos de esa ruta");
            overlay.Render(withAlternative with { Plan = new MayhemBuildPlan(equipped,
                new(equipped[^1].Item, alternative[1], "Contra la composición rival")) });
            Check(!HasRouteLabel() && BuildElements<TextBlock>(rows).Any(text => text.Text == "Cambio sugerido"),
                "la ruta alternativa también se oculta con seis objetos y un cambio sugerido");
            overlay.Render(withAlternative with { Plan = new MayhemBuildPlan([..equipped.Take(5), plan.Steps[5]]) });
            Check(HasRouteLabel(), "la ruta alternativa vuelve a aparecer si se libera un espacio y falta algún objeto");

            bool HasRouteLabel() => rows.Children.OfType<TextBlock>().Any(text => text.Text == "Ruta alternativa");

            var reference = data with { Groups = [new(OpGgBuildSection.Core, [core])] };
            overlay.Render(view with { Bounds = compactBounds, Shop = shop, Plan = null, Source = new(reference, "Prueba") });
            var compactRows = rows.Children.OfType<DockPanel>().ToArray();
            placement = Native.Bounds(handle);
            Check(placement.X >= compactBounds.X + shop.X + shop.Width &&
                placement.X + placement.Width <= compactBounds.X + compactBounds.Width &&
                compactRows.SelectMany(row => row.Children.OfType<TextBlock>().Select(text => text.Text))
                    .SequenceEqual(core.SelectMany((item, index) => new[] { $"{index + 1}.", item.Name })),
                "la ruta de seis objetos conserva nombres y orden en el formato estrecho junto a la tienda");
            overlay.Render(view with { Plan = null, Source = new(data with { Groups = [] }, "Prueba"), EnemyDamage = null });
            Check(rows.Children.OfType<TextBlock>().Any(text => text.Text == "OP.GG no tiene recomendaciones de objetos disponibles."),
                "una respuesta con solo hechizos muestra un estado vacío de objetos");

            var wide = new Int32Rect(-30000, -30000, Pixels(1450), Pixels(900));
            var sample = new BuildDisplay(wide, 0, "Zyra", Mode.Caos, new(data, "Prueba"));
            overlay.Render(sample);
            var normal = Native.Bounds(handle);
            overlay.SetSize(BuildSizePreference.Minimum);
            var small = Native.Bounds(handle);
            overlay.SetSize(BuildSizePreference.Maximum);
            var big = Native.Bounds(handle);
            Check(small.Width < normal.Width && small.Height < normal.Height &&
                big.Width > normal.Width && big.Height > normal.Height &&
                big.X + big.Width == normal.X + normal.Width && big.Y + big.Height <= wide.Y + wide.Height,
                "el tamaño cambia de inmediato y el panel conserva el anclaje y cabe en la partida");
            overlay.Render(sample with { Bounds = compactBounds, Shop = shop });
            var fit = Native.Bounds(handle);
            Check(fit.X >= compactBounds.X + shop.X + shop.Width &&
                fit.X + fit.Width <= compactBounds.X + compactBounds.Width &&
                fit.Y >= compactBounds.Y && fit.Y + fit.Height <= compactBounds.Y + compactBounds.Height,
                "la escala máxima respeta el espacio junto a la tienda estrecha");

            overlay.Render(null);
            Check(!Native.IsWindowVisible(handle), "la build se oculta al desactivarla");
            Console.WriteLine("Panel de builds de OP.GG verificado.");
        }
        finally { overlay.Close(); }

        static TextBlock[] PurchaseText(DockPanel row) =>
            row.Children.OfType<StackPanel>().Single().Children.OfType<TextBlock>().ToArray();

        static void CheckDamageBar(StackPanel rows, int physical)
        {
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var distribution = rows.Children.OfType<StackPanel>().Single();
            var labels = (Grid)distribution.Children[0];
            var bar = (Grid)distribution.Children[1];
            var segments = bar.Children.OfType<Border>().ToArray();
            Check(rows.Children[^1] == distribution &&
                labels.Children.OfType<TextBlock>().Select(label => label.Text)
                    .SequenceEqual([$"{physical}% físico", $"{100 - physical}% mágico"]) &&
                bar.ActualWidth > 0 && segments.Length == 2 &&
                Math.Abs(segments[0].ActualWidth - bar.ActualWidth * physical / 100) <= 1 &&
                Math.Abs(segments[1].ActualWidth - bar.ActualWidth * (100 - physical) / 100) <= 1,
                "la barra muestra ambos porcentajes y actualiza las proporciones aunque la ruta siga igual");
        }
    }

    private static void BuildSizeControlSmoke(string patch)
    {
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var file = Path.Combine(folder, "build-size.json");
        var preference = new BuildSizePreference(file);
        Check(preference.Load() == 100, "tamaño normal al usar el ajuste por primera vez");
        var window = new BuildSizeWindow(preference.Load(), patch, null, preference.Save);
        try
        {
            var slider = (Slider)window.FindName("SizeSlider");
            var preview = (BuildPanel)((ContentPresenter)window.FindName("PreviewHost")).Content;
            var percent = (TextBlock)window.FindName("PercentText");
            var reset = (Button)window.FindName("ResetButton");
            slider.Value = 135;
            Check(preference.Load() == 135 && percent.Text == "135 %" &&
                preview.LayoutTransform is ScaleTransform { ScaleX: 1.35, ScaleY: 1.35 } && reset.IsEnabled,
                "el deslizador actualiza la vista previa y guarda el porcentaje elegido");
            reset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(slider.Value == 100 && preference.Load() == 100 && !reset.IsEnabled,
                "restablecer recupera el tamaño normal y lo guarda");
            File.WriteAllText(file, "175");
            Check(preference.Load() == 100, "una preferencia fuera de rango vuelve al tamaño normal");
        }
        finally
        {
            window.Close();
            File.Delete(file);
            if (Directory.Exists(folder))
                Directory.Delete(folder);
        }
    }

    private static void BuildSizeButtonSmoke(string patch)
    {
        var overlay = new BuildOverlay { Left = -30000, Top = -30000 };
        BuildSizeWindow? settings = null;
        overlay.SizeRequested += () =>
        {
            settings = new BuildSizeWindow(100, patch, null, _ => true)
            {
                WindowStartupLocation = WindowStartupLocation.Manual, ShowActivated = false,
                Left = -30000, Top = -30000
            };
            settings.Show();
        };
        try
        {
            var bounds = new Int32Rect(-30000, -30000, 1600, 900);
            var shop = new Int32Rect(350, 75, 870, 690);
            overlay.Render(new BuildDisplay(bounds, 0, "Lillia", Mode.Caos, null, shop));
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var sizeControl = overlay.OwnedWindows.OfType<BuildSizeButton>().Single();
            var button = (Button)sizeControl.Content;
            var handle = new WindowInteropHelper(sizeControl).Handle;
            var position = Native.Bounds(handle);
            var overlayPosition = Native.Bounds(new WindowInteropHelper(overlay).Handle);
            Check(sizeControl.IsVisible && position.Y >= bounds.Y && position.Y + position.Height < overlayPosition.Y &&
                position.X + position.Width <= bounds.X + bounds.Width &&
                (Native.GetWindowLongW(handle, -20) & 0x08000020) == 0x08000000,
                "el botón queda encima de la build y acepta clics sin tomar el foco");
            var peer = new System.Windows.Automation.Peers.ButtonAutomationPeer(button);
            var action = (System.Windows.Automation.Provider.IInvokeProvider)peer.GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke);
            action.Invoke();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Check(settings is { IsVisible: true } && settings.FindName("SizeSlider") is Slider, "el botón abre el ajuste de tamaño");
            overlay.Render(null);
            Check(!sizeControl.IsVisible, "el botón se oculta con la build");
        }
        finally
        {
            settings?.Close();
            overlay.Close();
        }
    }

    private static IEnumerable<T> BuildElements<T>(UIElement element) where T : UIElement
    {
        if (element is T match)
            yield return match;
        var children = element switch
        {
            Panel panel => panel.Children.Cast<UIElement>(),
            Decorator { Child: { } child } => new[] { child },
            _ => []
        };
        foreach (var child in children)
        foreach (var descendant in BuildElements<T>(child))
            yield return descendant;
    }

    private static void ArenaBuildOverlaySmoke(Ratings ratings)
    {
        var overlay = new BuildOverlay { Left = -30000, Top = -30000 };
        try
        {
            var bounds = new Int32Rect(-30000, -30000, 1600, 900);
            var shop = new Int32Rect(350, 75, 870, 690);
            var match = new ArenaMatch([443083, 223111, 444644, 223084, 223068],
                [new("Annie", [223089]), new("Brand", [226653]), new("Veigar", [223089])]);
            var plan = new ArenaBuilds(ratings).Recommend(new(Mode.Arena, "Chogath"), match, null);
            Check(plan?.Steps is { Length: 6 }, "el panel de Arena puede mostrar la ruta con el catálogo local");
            overlay.Render(new BuildDisplay(bounds, 0, "Cho'Gath", Mode.Arena, null, shop, plan,
                ClientPatch: ratings.Catalog.Patch, ArenaRivalsDetected: true));
            var handle = new WindowInteropHelper(overlay).Handle;
            var placement = Native.Bounds(handle);
            var panel = (StackPanel)((Border)((Viewbox)overlay.Content).Child).Child;
            var equipped = panel.Children.Cast<UIElement>().SkipWhile(element => element is not TextBlock { Text: "Equipados · 5 de 6" })
                .TakeWhile(element => element is not System.Windows.Shapes.Rectangle).SelectMany(BuildElements<Image>).ToArray();
            Check(Native.IsWindowVisible(handle) && placement.X >= bounds.X + shop.X + shop.Width &&
                placement.X + placement.Width <= bounds.X + bounds.Width && equipped.Length == 5 &&
                panel.Children.OfType<TextBlock>().Any(text => text.Text == "Faltan datos para estimar el daño rival."),
                "la ruta de Arena distingue la compra pendiente y cinco equipadas junto a la tienda");
            overlay.Render(null);
            Check(!Native.IsWindowVisible(handle), "la ruta de Arena se oculta al cerrar el panel");
            Console.WriteLine("Panel de la ruta situacional de Arena verificado en WPF.");
        }
        finally { overlay.Close(); }
    }

    private static void PrismaticOverlaySmoke(Ratings ratings, string assets)
    {
        var overlay = new BuildOverlay { Left = -30000, Top = -30000, ShowInTaskbar = false };
        try
        {
            var bounds = new Int32Rect(-30000, -30000, 1280, 720);
            var layout = new LayoutStore(Path.Combine(assets, "layouts.json"), "")
                .Find(Mode.Arena, bounds.Width, bounds.Height)[0];
            var offer = new DetectionResult(true, new int?[3],
                ["Cuota del Segador", "Escultor de Runas", "Decapitador"], [true, true, true]);
            var top = ratings.Prismatics.ForOffer(new(Mode.Arena, "Tristana"), offer)!.Top;
            var view = new BuildDisplay(bounds, 0, "Tristana", Mode.Arena, null,
                Prismatics: new(top, layout, ratings.Catalog.Patch));
            overlay.Render(view);
            var handle = new WindowInteropHelper(overlay).Handle;
            var placement = Native.Bounds(handle);
            var panel = (StackPanel)((Border)((Viewbox)overlay.Content).Child).Child;
            var rows = panel.Children.OfType<DockPanel>().ToArray();
            Check(Native.IsWindowVisible(handle) && placement.X >= bounds.X &&
                placement.X + placement.Width < bounds.X + layout.Titles[0].Pixels(bounds.Width, bounds.Height).X &&
                rows.Length == 5 && rows[0].Children.OfType<StackPanel>().Single().Children.OfType<TextBlock>()
                    .Select(text => text.Text).SequenceEqual(["Cuota del Segador · S+", "Velocidad de ataque y daño al impacto", "En oferta"]),
                "panel prismático de cinco filas a un lado de las cartas, con tier y disponibilidad");

            overlay.Render(view with { Prismatics = view.Prismatics! with { Top = top with { OfferedMask = 0 } } });
            Check(panel.Children.OfType<DockPanel>().First().Children.OfType<StackPanel>().Single().Children.Count == 2,
                "la marca de oferta se actualiza al cambiar las cartas");
            overlay.Render(null);
            Check(!Native.IsWindowVisible(handle), "el Top 5 se oculta al cerrar la selección");
            Console.WriteLine("Panel lateral de prismáticos verificado en WPF.");
        }
        finally { overlay.Close(); }
    }

    private sealed class TestIcons : System.Net.Http.HttpMessageHandler
    {
        private readonly byte[] png;

        internal TestIcons()
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null,
                new byte[] { 72, 89, 198, 255 }, 4)));
            using var output = new MemoryStream();
            encoder.Save(output);
            png = output.ToArray();
        }

        protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Check(request.RequestUri is { Host: "opgg-static.akamaized.net" } address &&
                address.AbsolutePath.StartsWith("/meta/images/lol/16.23.1/item/", StringComparison.Ordinal),
                "los iconos usan el CDN y el parche de la build");
            var content = new System.Net.Http.ByteArrayContent(png);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
            return Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = content });
        }
    }

    private static void OverlaySmoke(string assets)
    {
        var overlay = new Overlay();
        try
        {
            var bounds = new Int32Rect(10, 10, 1280, 720);
            var marker = new Region(.1, .1, .1, .1);
            var titles = new[] { new Region(.1, .5, .15, .1), new Region(.4, .5, .15, .1), new Region(.7, .5, .15, .1) };
            var layout = new Calibration(bounds.Width, bounds.Height, marker, titles, new byte[720]);
            var comparison = new OverlayUpdate("Prueba", bounds, layout,
                [new("A", new OpGgCard("S")), new("A", new OpGgCard("A")), CardComparison.Unread]);
            overlay.Render(comparison);
            var first = overlay.Windows[0];
            var handle = new WindowInteropHelper(first).Handle;
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var placement = Native.Bounds(handle);
            var titleCenter = bounds.X + (titles[0].X + titles[0].Width / 2) * bounds.Width;
            Check(overlay.IsVisible && Native.IsWindowVisible(handle) && placement.Width < bounds.Width &&
                Math.Abs(placement.X + placement.Width / 2.0 - titleCenter) <= 2 &&
                placement.Y + placement.Height <= bounds.Y + titles[0].Y * bounds.Height &&
                (Native.GetWindowLongW(handle, -20) & 0x08000020) == 0x08000020,
                "letra en una ventana nativa visible, alineada y sin capturar foco o clics");
            var firstBadge = Frame(first);
            Check(overlay.Windows.Count(window => window.IsVisible) == 3 && firstBadge.ActualWidth > 0 && firstBadge.ActualHeight > 0,
                "una ventana por letra");
            var rendered = new RenderTargetBitmap((int)Math.Ceiling(firstBadge.ActualWidth), (int)Math.Ceiling(firstBadge.ActualHeight),
                96, 96, PixelFormats.Pbgra32);
            rendered.Render(firstBadge);
            var pixel = new byte[4];
            rendered.CopyPixels(new Int32Rect(6, 8, 1, 1), pixel, 4, 0);
            Check(pixel[3] > 100, "píxeles visibles del badge");
            static Border Frame(Window window) => (Border)((Viewbox)window.Content).Child;
            static string[] VisibleLines(Window window) => BuildElements<TextBlock>(Frame(window))
                .Where(text => text.Visibility == Visibility.Visible).Select(text => text.Text).ToArray();
            var texts = BuildElements<TextBlock>(firstBadge).ToArray();
            Check(VisibleLines(first).SequenceEqual(["S", "OP.GG", "Local", "A"]) &&
                texts[0].FontSize >= 2 * texts[1].FontSize &&
                texts[3].FontSize < texts[0].FontSize &&
                VisibleLines(overlay.Windows[1]).SequenceEqual(["A", "OP.GG", "Local", "A"]) &&
                VisibleLines(overlay.Windows[2]).SequenceEqual(["Leyendo carta…"]), "Abejas muestra sus dos fuentes y el estado de lectura es explícito");
            CheckTierLayout(first);
            overlay.ShowDetails = true;
            overlay.Render(comparison);
            Check(VisibleLines(overlay.Windows[1]).SequenceEqual(["A", "OP.GG", "Local", "A"]),
                "la vista detallada también muestra ambas letras una sola vez aunque coincidan");
            overlay.ShowDetails = false;
            var reroll = new OverlayUpdate("Reroll", bounds, layout,
                [new("Sin dato", new OpGgCard("B"), Mayhem: new(18.81m, "26.22", true)), null, new("A")]);
            overlay.Render(reroll);
            Check(VisibleLines(first).SequenceEqual(["B", "OP.GG"]) &&
                !overlay.Windows[1].IsVisible && VisibleLines(overlay.Windows[2]).SequenceEqual(["A", "Local"]),
                "la vista compacta prioriza el tier y omite las estadísticas y valoraciones ausentes");
            overlay.ShowDetails = true;
            overlay.Render(reroll);
            Check(VisibleLines(first).SequenceEqual(
                ["B", "OP.GG", "Elección global", "18.81 %", "ARAMMayhem.com", "Parche anterior 26.22"]) &&
                !overlay.Windows[1].IsVisible && VisibleLines(overlay.Windows[2]).SequenceEqual(["A", "Local"]),
                "la vista detallada conserva la elección global y el parche");
            var updated = Native.Bounds(handle);
            var localOnly = Native.Bounds(new WindowInteropHelper(overlay.Windows[2]).Handle);
            Check(updated.Y + updated.Height == localOnly.Y + localOnly.Height && updated.Width == localOnly.Width &&
                updated.Height > localOnly.Height &&
                updated.Width <= titles[0].Pixels(bounds.Width, bounds.Height).Width,
                "los tiers se alinean aunque una carta tenga más datos que otra, dentro del ancho calibrado");
            overlay.Render(new OverlayUpdate("Parche vigente", bounds, layout,
                [new("A", Mayhem: new(18.81m, "26.23", false))]));
            Check(VisibleLines(first).SequenceEqual(["A", "Local", "Elección global", "18.81 %", "ARAMMayhem.com"]),
                "la anotación del parche anterior desaparece al llegar los datos vigentes");
            var arenaOffer = new OverlayUpdate("Arena", bounds, layout,
                [new("A", Arena: new(7.76m, 59, 760, "26.23", false), IsArenaAugment: true),
                 new("S", new OpGgCard(PickPercent: 4.82m), ExternalPreviousPatch: "26.22", IsArenaAugment: true,
                     RecommendedUpgrade: true, UpgradeLevel: 2),
                 new("B", Loading: true, IsArenaAugment: true)]);
            overlay.Render(arenaOffer);
            Check(VisibleLines(first).SequenceEqual(
                ["A", "Local", "Frecuencia de elección", "GGEZ.GG", "7.76 %", "59 de 760 partidas · 26.23"]) &&
                VisibleLines(overlay.Windows[1]).SequenceEqual(
                    ["S+", "Local", "Nv. 2", "Mejora recomendada", "Frecuencia de elección", "OP.GG", "4.82 %", "Parche anterior 26.22"]) &&
                VisibleLines(overlay.Windows[2]).SequenceEqual(["B", "Local", "Consultando OP.GG…"]),
                "Arena destaca la estimación disponible y conserva los datos externos publicados y el estado de consulta");
            CheckTierLayout(overlay.Windows[1]);
            overlay.ShowDetails = false;
            overlay.Render(arenaOffer);
            Check(VisibleLines(first).SequenceEqual(["A", "Local"]) &&
                VisibleLines(overlay.Windows[1]).SequenceEqual(["S+", "Local", "Nv. 2", "Recomendado"]),
                "Arena conserva la recomendación y el nivel en la vista compacta");
            var sPlusAugment = Frame(overlay.Windows[1]);
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var recommendation = BuildElements<TextBlock>(sPlusAugment).Single(text => text.Text == "Recomendado");
            Check(recommendation.ActualHeight < recommendation.FontSize * 2,
                "Recomendado cabe en una línea en una tarjeta S+ compacta");
            var goldSurface = sPlusAugment.Background;
            var goldOutline = sPlusAugment.BorderBrush;
            var regularSurface = firstBadge.Background;
            var prismatics = new OverlayUpdate("Prismáticos", bounds, layout,
                [new("S", ArenaChoice: new("Objeto prismático", Tier.S, "Se adapta a tu campeón", TopPosition: 5)),
                 new("S", new OpGgCard("S")),
                 new("A", ArenaChoice: new("Otro objeto", Tier.A, "Afín a tu campeón", Recommended: true))]);
            foreach (var details in new[] { false, true })
            {
                overlay.ShowDetails = details;
                overlay.Render(prismatics);
                Check(firstBadge.Background == goldSurface && firstBadge.BorderBrush == goldOutline &&
                    firstBadge.BorderThickness == new Thickness(2) && VisibleLines(first).Contains("S+") &&
                    !VisibleLines(first).Contains("Recomendado") &&
                    overlay.Windows.Skip(1).All(window => Frame(window).Background == regularSurface &&
                        Frame(window).BorderBrush != goldOutline),
                    "todo S+ lleva el fondo y el marco dorados sin depender de la recomendación y se distingue del S y del A");
            }
            overlay.ShowDetails = false;
            overlay.Render(prismatics with { Labels = [CardComparison.Unread] });
            Check(firstBadge.Background == regularSurface && firstBadge.BorderBrush != goldOutline &&
                firstBadge.BorderThickness == new Thickness(1), "el dorado se retira al releer una carta");
            overlay.Render(new OverlayUpdate("Arena variable", bounds, layout,
                [new("Variable", IsArenaAugment: true, LocalReason: "Copia un aumento rival cada ronda")]));
            Check(VisibleLines(first).SequenceEqual(["Copia un aumento rival cada ronda", "Afinidad variable"]),
                "una carta variable explica su condición aunque falten porcentajes");

            var shardBounds = new Int32Rect(10, 10, 1000, 560);
            var shardOffer = new OverlayUpdate("Fragmentos", shardBounds, layout,
                [new("B", ArenaChoice: new("Vida", Tier.B, "Más vida para resistir el daño de los rivales durante un combate prolongado", Recommended: true)),
                 new("C", ArenaChoice: new("Velocidad de ataque", Tier.C, "Más ataques básicos por segundo")),
                 new("C", ArenaChoice: new("Letalidad", Tier.C, "Penetración para daño físico"))]);
            overlay.ShowDetails = true;
            overlay.Render(shardOffer);
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var shardPlacements = overlay.Windows.Select(window => Native.Bounds(new WindowInteropHelper(window).Handle)).ToArray();
            for (int i = 0; i < shardOffer.Labels!.Length; i++)
            {
                var frame = Frame(overlay.Windows[i]);
                var reason = BuildElements<TextBlock>(frame).Single(text => text.Text == shardOffer.Labels[i]!.ArenaChoice!.Reason);
                var reasonBounds = reason.TransformToAncestor(frame).TransformBounds(new Rect(reason.RenderSize));
                Check(reason.ActualHeight > (i == 0 ? reason.FontSize * 2 : 0) &&
                    reasonBounds.Bottom <= frame.ActualHeight - frame.Padding.Bottom + 1 &&
                    shardPlacements[i].Width <= titles[i].Width * shardBounds.Width &&
                    shardPlacements[i].Y + shardPlacements[i].Height < shardBounds.Y + titles[i].Y * shardBounds.Height - 12,
                    "el motivo de varias líneas cabe completo por encima del título del fragmento");
            }
            overlay.Render(shardOffer);
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Check(overlay.Windows.Select(window => Native.Bounds(new WindowInteropHelper(window).Handle)).SequenceEqual(shardPlacements),
                "los fragmentos conservan sus dimensiones entre fotogramas de la misma oferta");
            CheckTierLayout(first);

            static void CheckTierLayout(Window window)
            {
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                var frame = Frame(window);
                var lines = BuildElements<TextBlock>(frame).ToArray();
                var grade = lines[0];
                Rect LayoutOf(TextBlock block) => block.TransformToAncestor(frame).TransformBounds(new Rect(block.RenderSize));
                var gradeBounds = LayoutOf(grade);
                Check(grade.TextAlignment == TextAlignment.Center && gradeBounds.Width > 0 &&
                    Math.Abs(gradeBounds.X + gradeBounds.Width / 2 - frame.ActualWidth / 2) <= 1 &&
                    lines.Skip(1).All(line => LayoutOf(line).Top >= gradeBounds.Bottom - 1),
                    "la letra se centra respecto al marco y todas las etiquetas y detalles se colocan debajo");
            }

            overlay.Render(new OverlayUpdate("Panel cerrado"));
            Check(!overlay.IsVisible, "overlay oculto tras cerrar el panel");

            CheckMayhemCrown(2560, 1440);
            CheckMayhemCrown(1600, 900);
            CheckArenaCardBadges(2560, 1440);
            CheckArenaCardBadges(1600, 900);
            Console.WriteLine("Overlay de WPF verificado.");

            void CheckMayhemCrown(int width, int height)
            {
                var store = new LayoutStore(Path.Combine(assets, "layouts.json"), "");
                var viewport = new Int32Rect(-30000, -30000, width, height);
                var fits = store.Find(Mode.Caos, width, height);
                Check(fits.Length == 2, "hay calibraciones de tres y cuatro cartas");
                CardComparison?[] cards = [
                    new("A", new OpGgCard("D"), Mayhem: new(10m, "26.22", true)),
                    new("A", new OpGgCard("S")), new(CardComparison.Missing, new OpGgCard("A")),
                    new("S", ArenaChoice: new("Fragmento", Tier.S, "Afinidad con el campeón", Recommended: true))
                ];
                // At 16:9 the reference frame begins at 17.85%, and its artwork starts below 21.5%.
                int cardEdge = viewport.Y + (int)Math.Round(height * .1785);
                int artTop = viewport.Y + (int)(height * .215);
                foreach (var fit in fits)
                foreach (var details in new[] { false, true })
                {
                    overlay.ShowDetails = details;
                    overlay.Render(new OverlayUpdate("ARAM Caos", viewport, fit, cards, Mode: Mode.Caos));
                    var placements = overlay.Windows.Take(fit.Titles.Length).Select(window =>
                        (Window: window, Bounds: Native.Bounds(new WindowInteropHelper(window).Handle))).ToArray();
                    Check(placements.Select((placement, index) =>
                        {
                            var title = fit.Titles[index].Pixels(width, height);
                            int bottom = placement.Bounds.Y + placement.Bounds.Height;
                            return placement.Window.IsVisible && placement.Bounds.Y >= viewport.Y &&
                                placement.Bounds.Y < cardEdge && bottom > cardEdge && bottom <= artTop &&
                                Math.Abs(placement.Bounds.X + placement.Bounds.Width / 2.0 - (viewport.X + title.X + title.Width / 2)) <= 1;
                        }).All(ok => ok), "los distintivos se apoyan en el marco sin tapar la zona de habilidad");
                    if (!details && width == 2560)
                    {
                        var shortBadge = placements[2].Bounds;
                        int overlap = shortBadge.Y + shortBadge.Height - cardEdge;
                        Check(overlap >= Math.Min(shortBadge.Height * .4, height * .03) && overlap <= shortBadge.Height * .55,
                            "el distintivo compacto cruza el borde aproximadamente por la mitad");
                    }
                }
            }

            void CheckArenaCardBadges(int width, int height)
            {
                var store = new LayoutStore(Path.Combine(assets, "layouts.json"), "");
                var viewport = new Int32Rect(-30000, -30000, width, height);
                var fit = store.Find(Mode.Arena, width, height).Single();
                var titlesInPixels = fit.Titles.Select(title => title.Pixels(width, height)).ToArray();
                int regularTop = (int)Math.Round(height * .1674);
                int upgradeTop = (int)Math.Round(height * .1582);
                CardComparison?[] cards = [
                    CardComparison.Unread,
                    new("S", IsArenaAugment: true, LocalReason: "Afín a las habilidades del campeón", RecommendedAugment: true),
                    new("B", IsArenaAugment: true, UpgradeLevel: 2)
                ];
                CardComparison?[] choices = [
                    new("B", ArenaChoice: new("Fragmento de tenacidad", Tier.B, "Reduce el control recibido", Recommended: true)),
                    new("A", ArenaChoice: new("Objeto legendario", Tier.A, "Refuerza la supervivencia")),
                    new("S", ArenaChoice: new("Objeto prismático", Tier.S, "Se adapta a tu campeón", TopPosition: 5))
                ];
                foreach (bool details in new[] { false, true })
                {
                    overlay.ShowDetails = details;
                    var offer = new OverlayUpdate("Arena", viewport, fit, cards, Mode: Mode.Arena, ArenaCardTop: regularTop);
                    CheckCentered(offer, regularTop, .09, details);
                    var middleUpgrade = offer with { Labels = [cards[0], cards[1]! with { UpgradeLevel = 2 },
                        cards[2]! with { UpgradeLevel = null }] };
                    CheckCentered(middleUpgrade, regularTop, .09, details);
                    CheckCentered(middleUpgrade with { ArenaCardTop = upgradeTop, ArenaUpgradeHeading = true },
                        upgradeTop, .146, details);
                    CheckCentered(offer with { Labels = choices }, regularTop, .09, details);
                    Check(VisibleLines(overlay.Windows[2]).Contains(details ? "Top #5" : "Local · Top #5"),
                        "el distintivo de objetos conserva la posición del ranking de Arena");
                    if (!details)
                    {
                        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                        var shardBadge = overlay.Windows[0];
                        var text = BuildElements<TextBlock>(Frame(shardBadge)).ToArray();
                        var grade = text[0];
                        var scaled = grade.TransformToAncestor(shardBadge).TransformBounds(new Rect(0, 0, 1, grade.FontSize));
                        var pixelSize = scaled.Height * VisualTreeHelper.GetDpi(shardBadge).DpiScaleY;
                        var metadata = string.Concat(text[1].Inlines.OfType<System.Windows.Documents.Run>().Select(run => run.Text));
                        Check(pixelSize >= 36 && text.Length == 2 && grade.Text == "B" && metadata == "Local · Recomendado",
                            $"la letra grande y la recomendación siguen legibles a {width}×{height} ({pixelSize:F1} px)");
                    }
                }

                void CheckCentered(OverlayUpdate offer, int cardTop, double headerBottom, bool details)
                {
                    overlay.Render(offer);
                    var placements = overlay.Windows.Take(3).Select(window =>
                        Native.Bounds(new WindowInteropHelper(window).Handle)).ToArray();
                    for (int i = 0; i < placements.Length; i++)
                    {
                        var position = placements[i];
                        var title = titlesInPixels[i];
                        Check(position.Height > 0 &&
                            Math.Abs(position.Y + position.Height / 2.0 - (viewport.Y + cardTop)) <= 1 &&
                            Math.Abs(position.X + position.Width / 2.0 - (viewport.X + title.X + title.Width / 2)) <= 1,
                            "el borde de cada carta de Arena cruza el centro de su distintivo aunque cambie el nivel");
                        if (!details)
                        {
                            bool augment = offer.Labels!.Any(label => label?.IsArenaAugment == true);
                            double clearance = augment ? .022 : .036;
                            Check(position.Y + position.Height <= viewport.Y + cardTop + height * clearance,
                                "los distintivos compactos dejan visibles el icono y las estrellas cuando las hay");
                        }
                    }
                    Check(placements[1].Y >= viewport.Y + height * headerBottom,
                        "el distintivo central cabe debajo del reloj o del encabezado de mejora");
                }
            }
        }
        finally { overlay.Close(); }
    }
}

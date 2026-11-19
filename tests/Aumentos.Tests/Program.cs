using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Controls;
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
        CheckHistory(ratings);
        Check(LeaguePatch.FindInstallPath(["product_install_root: \"C:/Riot Games\"",
            "product_install_full_path: \"C:/Riot Games/League of Legends\""]) == "C:/Riot Games/League of Legends",
            "ruta de instalación según Riot");
        var defaultLayouts = new LayoutStore(Path.Combine(assets, "layouts.json"), "");
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
        Check(LiveGame.ChampionKey(players.RootElement, "player#000") == "DrMundo", "campeón del jugador activo");
        var arena = ratings.Recognize(Mode.Arena, "Misión: Endurece tu corazón");
        var reading = ratings.Recognize(Mode.Caos, " ENDURECE   TU CORAZÓN ");
        Check(arena is not null && reading is not null && reading.Mode == "Caos", "catálogo y normalización por modo");
        Check(ratings.Recognize(Mode.Caos, "Qxzblw") is null, "texto ambiguo o desconocido");
        Check(ratings.Recognize(Mode.Caos, "Pan y") is null, "títulos parciales");
        Check(ratings.Rate(Mode.Caos, "DrMundo", reading!.Id) == Tier.S, "prioridad de la excepción editorial");
        Check(ratings.Rate(Mode.Arena, "DrMundo", -1) is null, "aumento sin valoración");
        var cards = ratings.Catalog.Augments.Where(card => card.Mode == "Arena").ToArray();
        Check(cards.GroupBy(card => ratings.Rate(Mode.Arena, "Jinx", card.Id)).Any(group => group.Count() >= 3),
            "empates permitidos");
        var deterministicCards = ratings.Catalog.Augments.Where(card => card.Mode != "Arena" ||
            card.Key is not ("ReplaceAugment" or "UnstableTransmutation") && !card.Key.StartsWith("Transmute", StringComparison.Ordinal)).ToArray();
        foreach (var champion in ratings.Catalog.Champions)
            Check(deterministicCards.All(card =>
                ratings.Rate(Enum.Parse<Mode>(card.Mode), champion.Key, card.Id).HasValue), "cobertura del catálogo");

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
            OverlaySmoke();
        }
        var samples = new List<SavedLayout>();
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
            Check(tiers.SequenceEqual(new Tier?[] { Tier.B, Tier.B, Tier.A, Tier.S }), "tiers de Lux");
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
            Check(ratings.Rate(Mode.Arena, "Elise", 364) is null &&
                ratings.Rate(Mode.Arena, "Elise", 1034).HasValue &&
                ratings.Rate(Mode.Arena, "Elise", 180).HasValue, "tiers de mejoras y opción aleatoria sin tier");
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

    private static void CheckHistory(Ratings ratings)
    {
        var folder = Path.Combine(Path.GetTempPath(), $"AumentosHistory-{Guid.NewGuid():N}");
        var history = new CardHistory(folder, ratings);
        try
        {
            history.Observe(4, Mode.Caos, "Lux", [2098, 1088, null, 2062], ["B", "B", "?", "S"], 0);
            history.Observe(4, Mode.Caos, "Lux", [2098, 1088, null, 2062], ["B", "B", "?", "S"], 300);
            history.Observe(4, Mode.Caos, "Lux", [null, 1088, null, 2062], ["?", "B", "?", "S"], 500);
            history.Observe(4, Mode.Caos, "Lux", [2098, 2087, null, 2062], ["B", "A", "?", "S"], 1000);
            history.ObserveClosed(4, 2000);
            history.Observe(4, Mode.Caos, "Lux", [2098, 2087, null, 2062], ["B", "A", "?", "S"], 3400);
            history.Import(DateTimeOffset.Now, Mode.Arena, "Elise", "image4.png", [364, 1034, 180]);
            history.Flush();
            history.Flush();
            var lines = File.ReadAllLines(history.FilePath);
            Check(lines.Length == 11, "historial sin duplicados entre fotogramas y con reroll y segunda selección");
            Check(lines.Single(row => row.Contains("\"1\",\"2\",\"2087\"")).Contains("\"Archimago\",\"A\",\"Partida\""),
                "se guarda exactamente el tier de la carta cambiada");
            Check(lines.Any(row => row.Contains("\"2\",\"1\",\"2098\"")) &&
                lines.Any(row => row.Contains("\"Reemplazar Aumento\",\"Sin tier\",\"Captura · tier recalculado · image4.png\"")),
                "selecciones nuevas y origen de captura");
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
        Check(!result.CanShow(false) && !result.CanShow(true), "los objetos de Arena dejan el overlay oculto");
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
            var timer = Stopwatch.StartNew();
            while (timer.ElapsedMilliseconds < 3000)
            {
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                var pixels = capture.Read(new Int32Rect(15, 15, 20, 25));
                if (pixels is not null)
                {
                    var color = new byte[4];
                    pixels.CopyPixels(new Int32Rect(5, 5, 1, 1), color, 4, 0);
                    Check(color[2] > 200 && color[1] < 50 && color[0] < 50, "píxeles del recorte D3D11");
                    Console.WriteLine("Captura de ventana D3D11 verificada.");
                    return;
                }
                Thread.Sleep(30);
            }
            throw new InvalidOperationException("No se recibió un cuadro de Windows Graphics Capture.");
        }
        finally { window.Close(); }
    }

    private static void Check(bool result, string description)
    {
        if (!result)
            throw new InvalidOperationException($"Falló: {description}");
    }

    private static void OverlaySmoke()
    {
        var overlay = new Overlay();
        try
        {
            var bounds = new Int32Rect(10, 10, 540, 260);
            var marker = new Region(.1, .1, .1, .1);
            var titles = new[] { new Region(.1, .5, .15, .1), new Region(.4, .5, .15, .1), new Region(.7, .5, .15, .1) };
            var layout = new Calibration(bounds.Width, bounds.Height, marker, titles, new byte[720]);
            overlay.Render(new OverlayUpdate("Prueba", bounds, layout, ["S", "A", "?"]));
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
            var firstBadge = (Border)first.Content;
            Check(overlay.Windows.Count(window => window.IsVisible) == 3 && firstBadge.ActualWidth > 0 && firstBadge.ActualHeight > 0,
                "una ventana por letra");
            var rendered = new RenderTargetBitmap((int)Math.Ceiling(firstBadge.ActualWidth), (int)Math.Ceiling(firstBadge.ActualHeight),
                96, 96, PixelFormats.Pbgra32);
            rendered.Render(firstBadge);
            var pixel = new byte[4];
            rendered.CopyPixels(new Int32Rect(6, 8, 1, 1), pixel, 4, 0);
            Check(pixel[3] > 100, "píxeles visibles del badge");
            overlay.Render(new OverlayUpdate("Panel cerrado"));
            Check(!overlay.IsVisible, "overlay oculto tras cerrar el panel");
            Console.WriteLine("Overlay de WPF verificado.");
        }
        finally { overlay.Close(); }
    }
}

using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Aumentos;

internal static class MayhemChampionTests
{
    internal static async Task Source(Catalog catalog)
    {
        using var response = new TierPage(catalog.Patch);
        using var http = new HttpClient(response);
        var folder = Directory.CreateTempSubdirectory("aumentos-mayhem-tiers");
        try
        {
            var tiers = new MayhemChampionTiers(catalog, folder.FullName, http);
            var first = tiers.ForCaptureAsync();
            var second = tiers.ForCaptureAsync();
            var data = (await first).Data;
            Check(data is not null && data.SourcePatch == catalog.Patch &&
                data.Letter("AurelionSol") == "A" && data.Letter("Jinx") == "A" && data.Letter("Kaisa") == "C" &&
                data.Letter("Lux") is null && data.Rank("aurelionsol") == 54 && data.Rank("Jinx") == 43 &&
                data.Rank("Kaisa") is null && (await second).Data == data && response.Requests == 1,
                "una consulta global lee tiers y posiciones publicados sin inventar los datos ausentes");
            using var offline = new TierPage(catalog.Patch) { Offline = true };
            using var offlineHttp = new HttpClient(offline);
            var cached = await new MayhemChampionTiers(catalog, folder.FullName, offlineHttp).ForCaptureAsync();
            Check(cached.Data?.Letter("Kaisa") == "C" && cached.Data.Rank("Jinx") == 43 && offline.Requests == 0,
                "reutiliza la copia de tiers y posiciones al reiniciar");

            var cache = Path.Combine(folder.FullName, "opgg-mayhem-tiers", catalog.Patch, "champion-tiers.json");
            StatsCache.Write(cache, data! with { Ranks = null });
            var legacy = await new MayhemChampionTiers(catalog, folder.FullName, offlineHttp).ForCaptureAsync();
            Check(legacy.Data?.Letter("Kaisa") == "C" && legacy.Data.Rank("Jinx") is null && offline.Requests == 1,
                "la copia anterior sigue dando letras y consulta las posiciones sin esperar doce horas");

            if (LeaguePatch.Previous(catalog.Patch) is { } previous)
            {
                using var earlier = new TierPage(previous);
                using var earlierHttp = new HttpClient(earlier);
                var old = await new MayhemChampionTiers(catalog, Path.Combine(folder.FullName, "anterior"),
                    earlierHttp).ForCaptureAsync();
                Check(old.Data?.SourcePatch == previous && old.Status.Contains("Parche anterior"),
                    "distingue el parche anterior de los datos vigentes");
            }
            var unrelated = Html(catalog.Patch).Replace("/aram-mayhem", "/aram-mayhem-classic", StringComparison.Ordinal);
            try
            {
                MayhemChampionTiers.Read(unrelated, catalog.Patch, new HashSet<string> { "jinx" });
                throw new InvalidOperationException("Aceptó una cola distinta de ARAM Caos.");
            }
            catch (InvalidDataException) { }
        }
        finally { folder.Delete(recursive: true); }
        Console.WriteLine("Tiers de campeones de OP.GG verificados con fuente global, caché y parche.");
    }

    internal static void ReadOffer(Ratings ratings, string assets, string offerPath, string? lockedPath, bool pair = false)
    {
        using var ocr = new Detection(ratings, Path.Combine(assets, "tessdata"));
        var reader = new MayhemDraftReader(ratings.Catalog);
        var image = Diagnostics.OpenBitmap(offerPath);
        foreach (var scale in new[] { 1.0, 0.6, 0.4 })
        {
            var resized = scale == 1 ? image : new TransformedBitmap(image, new ScaleTransform(scale, scale));
            var choices = reader.Read(resized, ocr);
            var recognized = choices?.Select(choice => (choice.Center, choice.Champion.Key, choice.Bottom)).ToArray();
            (int, string, int)[] expected = pair ? [(831, "Camille", 752), (1169, "Belveth", 780)] :
                [(662, "Kaisa", 752), (1000, "Jinx", 752), (1338, "AurelionSol", 752)];
            Check(recognized?.SequenceEqual(expected) == true,
                $"oferta de {expected.Length} cartas a {resized.PixelWidth}px. Lectura: " +
                (recognized is null ? "sin oferta" : string.Join(", ", recognized)));
        }
        if (lockedPath is not null)
            Check(reader.Read(Diagnostics.OpenBitmap(lockedPath), ocr) is null, "la oferta se cierra al elegir");
        Console.WriteLine($"Oferta de {(pair ? "dos" : "tres")} cartas de ARAM Caos leída en tres tamaños.");
    }

    internal static void ReadBench(Catalog catalog, string signatures, string camillePath, string gragasPath)
    {
        ReadBench(catalog, signatures, camillePath, [(2, "Karma"), (3, "Gragas"), (4, "Pyke"), (5, "Katarina"), (6, "Belveth")]);
        ReadBench(catalog, signatures, gragasPath, [(3, "Camille"), (4, "Pyke"), (5, "Katarina"), (6, "Belveth")]);
    }

    internal static void ReadVelkozBench(Catalog catalog, string signatures, string path) =>
        ReadBench(catalog, signatures, path, [(0, "Teemo"), (1, "Leblanc"), (2, "Ashe"), (3, "Janna"), (5, "Illaoi")]);

    internal static void ReadLeonaBench(Catalog catalog, string signatures, string path) =>
        ReadBench(catalog, signatures, path, [(0, "Braum"), (1, "Zoe"), (2, "Lulu"), (3, "Smolder"), (5, "Zed")]);

    private static void ReadBench(Catalog catalog, string signatures, string path, (int, string)[] expected)
    {
        using var http = new HttpClient();
        var reader = new MayhemBenchPortraits(catalog, "", http);
        var templates = Ratings.Read<Dictionary<string, byte[]>>(signatures);
        var image = Diagnostics.OpenBitmap(path);
        foreach (var scale in new[] { 1.0, 0.6, 0.4 })
        {
            var resized = scale == 1 ? image : new TransformedBitmap(image, new ScaleTransform(scale, scale));
            var found = reader.Match(resized, templates).Select(choice => (choice.Slot, choice.Champion.Key)).ToArray();
            Check(found.SequenceEqual(expected),
                $"banco a {resized.PixelWidth}px. Lectura: {string.Join(", ", found)}");
        }
        Console.WriteLine($"Banca de ARAM Caos verificada en tres tamaños · {string.Join(", ", expected.Select(entry => entry.Item2))}.");
    }

    internal static void Smoke(Catalog catalog)
    {
        var overlay = new MayhemChampionOverlay { Left = -30000, Top = -30000 };
        try
        {
            var bounds = new Int32Rect(-30000, -30000, 1200, 675);
            var data = new MayhemChampionTierList(catalog.Patch, DateTimeOffset.UtcNow, new()
                { ["jinx"] = 2, ["kaisa"] = 4, ["aurelionsol"] = 2, ["camille"] = 3, ["belveth"] = 2, ["gragas"] = 3 });
            var source = new MayhemChampionTierView(data, MayhemChampionTiers.Caption(data, catalog.Patch));
            MayhemDraftChoice[] choices = [new(662, Champion("Kaisa")), new(1000, Champion("Jinx")), new(1338, Champion("AurelionSol"))];
            overlay.Render(new MayhemChampionDisplay(bounds, 0, source, choices));
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var handle = new WindowInteropHelper(overlay).Handle;
            var placement = Native.Bounds(handle);
            var badges = (Canvas)((Viewbox)overlay.Content).Child;
            var displayed = badges.Children.Cast<Border>().Select(row => ((TextBlock)row.Child).Text);
            Check(Native.IsWindowVisible(handle) && placement.Y >= bounds.Y + (int)(bounds.Height * 0.667) &&
                placement.Y + placement.Height < bounds.Y + (int)(bounds.Height * 0.784) &&
                (Native.GetWindowLongW(handle, -20) & 0x08000020) == 0x08000020 &&
                displayed.SequenceEqual(["C", "A", "A", source.Status]),
                "muestra los tiers en orden bajo sus cartas con clics transparentes");
            overlay.Render(new MayhemChampionDisplay(bounds, 0, source, [choices[2]]));
            Check(badges.Children.Count == 2 && Canvas.GetLeft(badges.Children[0]) > 700,
                "una lectura parcial conserva la posición del campeón reconocido");
            overlay.Render(new MayhemChampionDisplay(bounds, 0, source,
                [new(831, Champion("Camille")), new(1169, Champion("Belveth"), 780)]));
            placement = Native.Bounds(handle);
            Check(placement.Y >= bounds.Y + (int)(bounds.Height * 0.693) &&
                displayed.SequenceEqual(["B", "A", source.Status]) &&
                Canvas.GetLeft(badges.Children[0]) > 275 && Canvas.GetLeft(badges.Children[1]) > 610,
                "dos tiers centrados y colocados debajo del borde de la carta ampliada");
            overlay.Render(new MayhemChampionDisplay(bounds, 0, source, [],
                [new(3, Champion("Gragas")), new(6, Champion("Belveth"))]));
            placement = Native.Bounds(handle);
            Check(placement.Y == bounds.Y + 10 && displayed.SequenceEqual(["B", "A"]) &&
                Canvas.GetLeft(badges.Children[0]) < Canvas.GetLeft(badges.Children[1]),
                "el tier queda sobre cada retrato disponible y conserva los huecos de la fila");
            overlay.Render(null);
            Check(!Native.IsWindowVisible(handle), "el tier bajo las cartas se oculta al elegir o salir");
        }
        finally { overlay.Close(); }
        Console.WriteLine("Tiers de ARAM Caos debajo de las cartas verificados en WPF.");

        Champion Champion(string key) => catalog.Champions.Single(champion => champion.Key == key);
    }

    private static string Html(string patch)
    {
        const string list = """
            4:{"champions":[{"key":"aurelionsol","tier":2,"rank":54},{"key":"jinx","tier":2,"rank":43},{"key":"kaisa","tier":4,"rank":0},{"key":"lux","tier":null}]}
            """;
        var flight = JsonSerializer.Serialize<object[]>([1, list]);
        return $"""
            <link rel="canonical" href="{MayhemChampionTiers.Address}"><title>ARAM: Caos parche {patch}</title>
            <script>self.__next_f.push({flight})</script>
            """;
    }

    private sealed class TierPage(string patch) : HttpMessageHandler
    {
        internal int Requests { get; private set; }
        internal bool Offline { get; init; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Check(request.RequestUri == MayhemChampionTiers.Address, "se consulta la página de ARAM Caos");
            Requests++;
            return Task.FromResult(Offline ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) :
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Html(patch)) });
        }
    }

    private static void Check(bool success, string message)
    {
        if (!success) throw new InvalidOperationException(message);
    }
}

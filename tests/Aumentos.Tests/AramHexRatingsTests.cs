using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aumentos;

internal static class AramHexRatingsTests
{
    internal static async Task Run(Catalog catalog)
    {
        var previous = LeaguePatch.Previous(catalog.Patch);
        if (previous is null) return;
        var cards = catalog.Augments.Where(card => card.Mode == nameof(Mode.Caos)).Take(2).ToArray();
        var renata = catalog.Champions.Single(champion => champion.Key == "Renata");
        var fixture = catalog with { Champions = [renata], Augments = cards };
        var allowed = new AramHexRatings.Compatibility(catalog.Patch, previous, [cards[0].Id]);
        var directory = Directory.CreateTempSubdirectory("aumentos-aramhex-test-");
        try
        {
            using var raw = new ExportHandler(catalog.Patch, previous, cards);
            using var http = new HttpClient(raw);
            var source = new AramHexRatings(fixture, directory.FullName, http, allowed);
            var result = await source.ForCaptureAsync(CancellationToken.None);
            Check(result.Data?.SourcePatch == previous && result.Data.Find("Renata", cards[0].Id) is { Tier: Tier.A, Games: 4000 }
                && result.Data.Find("Renata", cards[1].Id) is null && result.Status.Contains("China"),
                "el parche anterior exige mecánicas compatibles y conserva la muestra del campeón");

            using var offline = new ExportHandler(catalog.Patch, previous, cards) { Fail = true };
            using var offlineClient = new HttpClient(offline);
            var reopened = new AramHexRatings(fixture, directory.FullName, offlineClient, allowed);
            var cached = await reopened.ForCaptureAsync(CancellationToken.None);
            Check(cached.Data?.Find("Renata", cards[0].Id) == result.Data!.Find("Renata", cards[0].Id) && offline.Requests == 0,
                "la caché conserva solo la letra y la muestra sin consultar de nuevo");
            var read = File.ReadAllText(Path.Combine(directory.FullName, "aramhex", $"{catalog.Patch}.json"));
            Check(!read.Contains("wins", StringComparison.OrdinalIgnoreCase) && !read.Contains("win_rate", StringComparison.Ordinal),
                "las tasas de victoria de aumentos no quedan en el caché");

            using var currentResponse = new ExportHandler(catalog.Patch, catalog.Patch, cards);
            using var currentClient = new HttpClient(currentResponse);
            var current = new AramHexRatings(fixture, Path.Combine(directory.FullName, "current"), currentClient, null);
            var latest = await current.ForCaptureAsync(CancellationToken.None);
            Check(latest.Data is { IsPrevious: false } &&
                latest.Data.Find("Renata", cards[1].Id) is { Tier: Tier.C, Games: 2000 } &&
                latest.Data.Find("Lux", cards[1].Id) is null,
                "el parche exacto acepta el aumento y conserva el ámbito por campeón");

            using var corrupt = new ExportHandler(catalog.Patch, catalog.Patch, cards) { BrokenDigest = true };
            using var corruptClient = new HttpClient(corrupt);
            var invalid = new AramHexRatings(fixture, Path.Combine(directory.FullName, "invalid"), corruptClient, null);
            Check((await invalid.ForCaptureAsync(CancellationToken.None)).Data is null,
                "una exportación con hash distinto no se publica");
        }
        finally { directory.Delete(recursive: true); }

        var championCounts = new AramHexRatings.MatchCounts(10000, 5000, false);
        Check(AramHexRatings.Rate(championCounts, new(299, 299, false)) is null
            && AramHexRatings.Rate(championCounts, new(500, 600, false)) is null
            && AramHexRatings.Rate(championCounts, new(300, 155, false)) is { Tier: Tier.B }
            && AramHexRatings.Rate(championCounts, new(4000, 1000, false)) is { Tier: Tier.C }
            && AramHexRatings.Rate(championCounts, new(4000, 1000, true)) is null,
            "la muestra, la incertidumbre y los conteos inconsistentes delimitan el tier");
        Console.WriteLine("AramHex verificado con muestra, atribución, parches, compatibilidad, caché y hash.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class ExportHandler : HttpMessageHandler
    {
        private readonly string patch;
        private readonly string export;
        private readonly string heroes = """{"888":{"games":10000,"wins":5000,"low_sample":false}}""";
        private readonly string augments;
        internal bool Fail { get; init; }
        internal bool BrokenDigest { get; init; }
        internal int Requests;

        internal ExportHandler(string patch, string export, Augment[] cards)
        {
            this.patch = patch;
            this.export = export;
            augments = JsonSerializer.Serialize(new Dictionary<int, Dictionary<int, AramHexRatings.MatchCounts>>
            {
                [888] = new() { [cards[0].Id] = new(4000, 2600, false), [cards[1].Id] = new(2000, 600, false) }
            });
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Requests);
            if (Fail) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            string path = request.RequestUri!.AbsolutePath;
            string? json = path.EndsWith("/main/latest.json", StringComparison.Ordinal) ? JsonSerializer.Serialize(new
            {
                schema_version = 1, patch = export, mode = "normal", path = $"patches/{export}/normal", seq = 19,
                files = new Dictionary<string, object>
                {
                    ["heroes.json"] = Digest(heroes),
                    ["hero_augments.json"] = Digest(BrokenDigest ? augments.Replace("2600", "1600", StringComparison.Ordinal) : augments)
                }
            }) : path == $"/cdn/{patch}.1/data/en_US/champion.json" ? """{"data":{"Renata":{"key":"888"}}}""" :
                path.EndsWith($"/data-v19/patches/{export}/normal/heroes.json", StringComparison.Ordinal) ? heroes :
                path.EndsWith($"/data-v19/patches/{export}/normal/hero_augments.json", StringComparison.Ordinal) ? augments : null;
            return Task.FromResult(json is null ? new HttpResponseMessage(HttpStatusCode.NotFound) :
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }

        private static object Digest(string body)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(body);
            return new { size = bytes.Length, sha256 = Convert.ToHexString(SHA256.HashData(bytes)) };
        }
    }
}

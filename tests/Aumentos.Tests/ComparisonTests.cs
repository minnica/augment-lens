using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using Aumentos;

internal static class ComparisonTests
{
    internal static async Task Run(Ratings ratings)
    {
        var patch = ratings.Catalog.Patch;
        var lux = new GameContext(Mode.Caos, "Lux");
        var elise = new GameContext(Mode.Arena, "Elise");
        var catalog = ratings.Catalog.Augments.Where(card => card.Mode == "Arena").ToDictionary(card => card.Id);
        const string arenaRows = """{"data":{"4":[{"id":97,"pick_rate":6.25,"win_rate":60},{"id":1034,"pick_rate":0}]},"mode":"arena"}""";
        var arena = OpGgPage.Read(Page(elise, patch, arenaRows), elise, patch, catalog);
        var labels = CardComparison.ForOffer(ratings, elise, [97, 1034, 120, 364], new bool[4], arena);
        Check(labels[0]?.External == new OpGgCard(PickPercent: 6.25m) && labels[0]?.ExternalText.Contains("Elección") == true &&
            labels[1]?.External?.PickPercent == 0 && labels[2]?.ExternalText == "Sin dato" && labels[3]?.Local == "Variable",
            "Arena conserva elección, cero, dato ausente y reemplazo");

        const string mayhemRows = """{"data":[{"key":"BEES","tier":0,"id":3086},{"id":1211,"tier":1,"key":"ARAM_ItsKillingTime"},{"id":3071,"key":"BrassBlast","tier":8},{"id":3150,"tier":1,"key":"Otro"}]}""";
        using var folder = new TemporaryFolder();
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = new PageHandler((_, token) => pending.Task.WaitAsync(token));
        using var http = new HttpClient(server);
        var source = new OpGgRatings(ratings.Catalog, folder.Path, http);
        var loading = source.Current(lux, default);
        var initial = CardComparison.ForOffer(ratings, lux, [3086], [false], loading.Data, loading.Loading);
        Check(initial[0]?.ExternalText == "Consultando…" && initial[0]?.Local == "A" &&
            ratings.Rate(Mode.Caos, "lux", 3086) == Tier.A, "la estimación de Abejas aparece mientras llega su letra externa");
        var wait = source.ForCaptureAsync(lux, default);
        var second = source.ForCaptureAsync(lux, default);
        pending.SetResult(Ok(Page(lux, patch, mayhemRows)));
        var loaded = await wait;
        Check((await second).Data == loaded.Data && server.Requests == 1, "una sola descarga por campeón");
        labels = CardComparison.ForOffer(ratings, lux, [1211, 3086, 3071, 3150], new bool[4], loaded.Data);
        Check(labels[0] == new CardComparison("Sin dato", new OpGgCard("A")) &&
            labels[1] == new CardComparison("A", new OpGgCard("S")) && labels[2]?.External is null &&
            labels[3]?.External is null, "identidad, letras nativas, tier desconocido y sin B automático");
        var reroll = CardComparison.ForOffer(ratings, lux, [3086, null, 3071, null], [false, false, false, true], loaded.Data);
        Check(reroll[0] == labels[1] && reroll[1] == CardComparison.Unread && reroll[3] is null,
            "reroll, OCR incierto y hueco de objeto");
        Check(CardComparison.ForOffer(ratings, lux, [97], [false], arena)[0]?.External is null,
            "aislamiento del modo y campeón");

        var oldPage = new PageHandler((_, _) => Task.FromResult(Ok(Page(elise, "15.1", arenaRows))));
        using var offlineHttp = new HttpClient(oldPage);
        var restarted = new OpGgRatings(ratings.Catalog, folder.Path, offlineHttp);
        Check((await restarted.ForCaptureAsync(lux, default)).Data?.Cards[3086].Tier == "S" && oldPage.Requests == 0,
            "la caché vigente funciona al reiniciar sin red");
        var unavailable = await restarted.ForCaptureAsync(elise, default);
        Check(unavailable.Data is null && unavailable.Status.Contains("15.1") && oldPage.Requests == 1,
            "un parche distinto informa la causa y no puntúa");
        await restarted.ForCaptureAsync(elise, default);
        Check(oldPage.Requests == 1, "la fuente no se vuelve a consultar por cada carta");

        var cacheFile = Directory.GetFiles(folder.Path, "*.json", SearchOption.AllDirectories).Single();
        var stale = loaded.Data! with { FetchedAt = DateTimeOffset.UtcNow.AddDays(-1) };
        File.WriteAllText(cacheFile, JsonSerializer.Serialize(stale));
        var failed = new PageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        using var failedHttp = new HttpClient(failed);
        var fallback = await new OpGgRatings(ratings.Catalog, folder.Path, failedHttp).ForCaptureAsync(lux, default);
        Check(fallback.Data?.Cards[3086].Tier == "S" && failed.Requests == 1 && fallback.Status.Contains("503"),
            "una caída conserva la última copia del mismo parche");
        File.WriteAllText(cacheFile, JsonSerializer.Serialize(stale with { Patch = "15.1" }));
        Check((await new OpGgRatings(ratings.Catalog, folder.Path, failedHttp).ForCaptureAsync(lux, default)).Data is null,
            "un archivo de otro parche tampoco sirve como respaldo");
        Console.WriteLine("Comparación OP.GG, rerolls, caché y aislamiento de parches verificados.");
    }

    private static string Page(GameContext game, string patch, string props)
    {
        var record = $"1:[\"$\",\"$L1\",null,{props}]\n";
        var mid = record.Length / 2;
        static string Chunk(string text) =>
            $"<script>self.__next_f.push({JsonSerializer.Serialize(new object[] { 1, text })})</script>";
        return $"<title>Build Patch {patch}: Augments | OP.GG</title><link rel=\"canonical\" href=\"{OpGgPage.Address(game)}\"/>" +
            Chunk(record[..mid]) + Chunk(record[mid..]);
    }

    private static HttpResponseMessage Ok(string html) => new(HttpStatusCode.OK) { Content = new StringContent(html) };

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException(message);
    }

    private sealed class PageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        internal int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Requests);
            return send(request, cancellationToken);
        }
    }

    private sealed class TemporaryFolder : IDisposable
    {
        internal string Path { get; } = Directory.CreateTempSubdirectory("Aumentos-OPGG-").FullName;
        public void Dispose() => Directory.Delete(Path, true);
    }
}

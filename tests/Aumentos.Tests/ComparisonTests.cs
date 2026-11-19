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
        await CheckAramMayhem(ratings, lux, elise);
    }

    private static async Task CheckAramMayhem(Ratings ratings, GameContext lux, GameContext elise)
    {
        var current = AramMayhemRatings.SitePatch(ratings.Catalog.Patch);
        var version = Version.Parse(current);
        var prior = $"{version.Major}.{version.Minor - 1}";
        var older = $"{version.Major}.{version.Minor - 2}";
        using var folder = new TemporaryFolder();
        var historical = new PageHandler((request, _) => Task.FromResult(Ok(
            request.RequestUri!.AbsolutePath.EndsWith("/index.json") ? Index(prior, prior) : Export(prior, 18.81m))));
        using var http = new HttpClient(historical);
        var source = new AramMayhemRatings(ratings.Catalog, folder.Path, http);
        Check(source.Current(Mode.Arena, default) is null && await source.ForCaptureAsync(Mode.Arena, default) is null &&
            historical.Requests == 0, "Arena no consulta el archivo de Caos");
        var work = source.ForCaptureAsync(Mode.Caos, default);
        var second = source.ForCaptureAsync(Mode.Caos, default);
        var published = await work;
        var previous = published?.Data;
        Check(previous is { IsPrevious: true } && (await second)?.Data == previous && historical.Requests == 2,
            "el parche anterior se carga una vez y se identifica");
        Check(previous is not null && previous.Picks[3086] == 0 && previous.Picks[3004] == 18.81m &&
            previous.Picks[1152] == 43.5m && previous.Picks[1402] != previous.Picks[1403] &&
            !previous.Picks.ContainsKey(2135) && !previous.Picks.ContainsKey(2139),
            "popularidad cero, claves localizadas, variantes y nombres en conflicto");
        var labels = CardComparison.ForOffer(ratings, lux, [3004, 3086], new bool[2], null, mayhem: previous);
        var reroll = CardComparison.ForOffer(ratings, lux, [3086, 3086], new bool[2], null, mayhem: previous);
        Check(labels[0]?.Mayhem == new MayhemPopularity(18.81m, prior, true) &&
            reroll[0] == labels[1] && reroll[1] == labels[1] &&
            CardComparison.ForOffer(ratings, elise, [3004], [false], null, mayhem: previous)[0]?.Mayhem is null,
            "reroll con datos globales y aislamiento de Arena");

        var upgradeServer = new PageHandler((request, _) => Task.FromResult(Ok(
            request.RequestUri!.AbsolutePath.EndsWith("/index.json") ? Index(current, current, prior) : Export(current, 20m))));
        using var upgradeHttp = new HttpClient(upgradeServer);
        var restarted = new AramMayhemRatings(ratings.Catalog, folder.Path, upgradeHttp);
        Check((await restarted.ForCaptureAsync(Mode.Caos, default))?.Data is { IsPrevious: true } &&
            upgradeServer.Requests == 0, "lectura de la copia válida al reiniciar");
        var cacheFile = Directory.GetFiles(folder.Path, "*.json", SearchOption.AllDirectories).Single();
        File.WriteAllText(cacheFile, JsonSerializer.Serialize(previous! with { FetchedAt = DateTimeOffset.UtcNow.AddHours(-1) }));
        var upgraded = await new AramMayhemRatings(ratings.Catalog, folder.Path, upgradeHttp).ForCaptureAsync(Mode.Caos, default);
        Check(upgraded?.Data is { IsPrevious: false } currentData && currentData.Picks[3004] == 20m &&
            upgradeServer.Requests == 2, "el parche vigente sustituye la copia anterior tras la actualización");

        using var emptyFolder = new TemporaryFolder();
        var outdated = new PageHandler((_, _) => Task.FromResult(Ok(Index(older, older))));
        using var outdatedHttp = new HttpClient(outdated);
        var rejected = await new AramMayhemRatings(ratings.Catalog, emptyFolder.Path, outdatedHttp)
            .ForCaptureAsync(Mode.Caos, default);
        Check(rejected is { Data: null } && outdated.Requests == 1, "una diferencia mayor a un parche queda sin datos");
        Console.WriteLine("ARAMMayhem.com verificado con porcentaje global, caché y transición al parche vigente.");

        static string Index(string latest, params string[] patches) => JsonSerializer.Serialize(
            new { schemaVersion = 1, latestPatch = latest, patches = patches.Select(patch => new { patch }) });
        static string Export(string patch, decimal jinx) => JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            meta = new { patch, statsDate = "2026-11-17" },
            rows = new[]
            {
                Row("bees", "¡¡¡ABEJAS!!!", 0), Row("get_jinxed", "Jinxéate", jinx),
                Row("quest_steel_your_heart", "Misión: Endurece tu corazón", 43.5m),
                Row("stats_on_stats!", "¡Estadísticas sobre Estadísticas!", 11),
                Row("stats_on_stats_on_stats!", "¡Estadísticas sobre Estadísticas!", 8),
                Row("pin_cushion", "Alfiletero", 17), Row("double_time", "Paso Redoblado", 101)
            }
        });
        static object Row(string augmentId, string name, decimal pickRate) =>
            new { augmentId, name = new Dictionary<string, string> { ["es-MX"] = name }, pickRate };
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

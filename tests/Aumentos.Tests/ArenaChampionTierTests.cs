using System.IO;
using System.Net;
using System.Net.Http;
using Aumentos;

internal static class ArenaChampionTierTests
{
    internal static void Run(Ratings ratings, string? liveHtmlPath = null) => RunAsync(ratings, liveHtmlPath).GetAwaiter().GetResult();

    private static async Task RunAsync(Ratings ratings, string? liveHtmlPath)
    {
        var catalog = ratings.Catalog;
        var patch = catalog.Patch;
        var aliases = ArenaChampionTierPage.Aliases(catalog);
        var html = Page(patch);
        var list = ArenaChampionTierPage.Read(html, patch, aliases);
        Check(list is { SampleMatches: 8047, IsPrevious: false, Champions.Count: 2 } &&
            list.Champions["Rell"] == new ArenaChampionTier("Rell", "S", 56.62650602409639m, 830) &&
            list.Champions["MonkeyKing"] == new ArenaChampionTier("MonkeyKing", "D", 40.12638m, 633) &&
            !list.Champions.ContainsKey("Lux"), "se conservan los tiers publicados, su muestra y el identificador canónico de Wukong");
        Reject(html.Replace("window:\"patch\"", "window:\"recent\""), patch, aliases);
        Reject(html.Replace("region:\"all\"", "region:\"na\""), patch, aliases);
        Reject(html.Replace("/tierlist/arena", "/tierlist/arena/augments"), patch, aliases);
        Reject(html.Replace("{id:526,games:830,top3Rate:56.62650602409639,firstRate:23.49,tier:\"S\"}",
            "{id:526,games:830,top3Rate:56.62650602409639,firstRate:23.49,tier:\"S\"},{id:526,games:830,top3Rate:56,tier:\"A\"}"), patch, aliases);

        var folder = Directory.CreateTempSubdirectory("Aumentos-Arena-Champions-");
        try
        {
            var reply = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var server = new Handler(_ => reply.Task);
            using var http = new HttpClient(server);
            var source = new ArenaChampionTierSource(catalog, folder.FullName, http);
            Check(source.Current().Data is null, "la lectura inicial no bloquea mientras el servidor sigue pendiente");
            var concurrent = source.ForCaptureAsync();
            reply.SetResult(html);
            var loaded = await concurrent;
            Check(server.Requests == 1 && loaded.Data is { IsPrevious: false } && source.Find(loaded.Data, "Wukong")?.Champion == "MonkeyKing" &&
                source.Find(loaded.Data, "rell")?.Tier == "S" && source.Find(loaded.Data, "Desconocido") is null,
                "la vista indexa alias conocidos y nunca asigna un tier a un campeón desconocido");

            var offline = new Handler(_ => throw new HttpRequestException("sin red"));
            using var offlineHttp = new HttpClient(offline);
            var restarted = await new ArenaChampionTierSource(catalog, folder.FullName, offlineHttp).ForCaptureAsync();
            Check(restarted.Data?.Champions["Rell"].Tier == "S" && offline.Requests == 0,
                "se recupera la copia vigente tras reiniciar sin conectarse");

            var cache = Path.Combine(folder.FullName, "ggez-arena", patch, "champion-tiers.json");
            StatsCache.Write(cache, list with { FetchedAt = DateTimeOffset.UtcNow.AddDays(-1) });
            var stale = await new ArenaChampionTierSource(catalog, folder.FullName, offlineHttp).ForCaptureAsync();
            Check(stale.Data?.Champions["Rell"].Games == 830 && stale.Status.Contains("Copia guardada") && offline.Requests == 1,
                "si falla la red se conserva la última copia validada");

            if (LeaguePatch.Previous(patch) is { } previous)
            {
                StatsCache.Write(Path.Combine(folder.FullName, "ggez-arena", previous, "champion-tiers.json"),
                    list with { ClientPatch = previous, SourcePatch = previous, FetchedAt = DateTimeOffset.UtcNow.AddDays(-1) });
                File.WriteAllText(cache, "null");
                var lastPatch = await new ArenaChampionTierSource(catalog, folder.FullName, offlineHttp).ForCaptureAsync();
                Check(lastPatch.Data is { IsPrevious: true } && lastPatch.Data.SourcePatch == previous &&
                    lastPatch.Status.Contains("Parche anterior"), "se etiqueta el respaldo offline del parche anterior");
                Reject(Page("0.0"), patch, aliases);
            }
        }
        finally { folder.Delete(recursive: true); }

        if (liveHtmlPath is not null)
        {
            var live = ArenaChampionTierPage.Read(File.ReadAllText(liveHtmlPath), patch, aliases);
            Check(live.Champions.Count >= 100 && live.Champions.ContainsKey("Rell") && live.Champions.ContainsKey("MonkeyKing"),
                "registros canónicos de la página primaria de Arena");
            var rell = live.Champions["Rell"];
            Console.WriteLine($"GGEZ Arena real · {live.Champions.Count} campeones · Rell {rell.Tier} · {rell.TopThreePercent:F1}% top 3 · {rell.Games} partidas");
        }
        Console.WriteLine("Tier de campeones de Arena verificado con muestra, fuente y caché offline.");
    }

    private static string Page(string patch) => $$$$"""
        <link rel="canonical" href="https://ggez.gg/tierlist/arena"/>
        <script>const arena={type:"data",data:{list:{patch:"{{{{patch}}}}",matches:8047,players:144846,rows:[{id:526,games:830,top3Rate:56.62650602409639,firstRate:23.49,tier:"S"},{id:62,games:633,top3Rate:40.12638,banRate:.02485,tier:"D"},{id:99,games:19,top3Rate:100,tier:"B"},{id:1,games:80,top3Rate:50,tier:null}]},names:{"526":{id:"Rell",name:"Rell",key:"526"},"62":{id:"MonkeyKing",name:"Wukong",key:"62"},"99":{id:"Lux",name:"Lux",key:"99"},"1":{id:"Annie",name:"Annie",key:"1"}},filters:{region:"all",window:"patch"}}};</script>
        """;

    private static void Reject(string html, string patch, IReadOnlyDictionary<string, string> aliases)
    {
        try { ArenaChampionTierPage.Read(html, patch, aliases); }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException("se aceptó una tabla de Arena incompatible");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Handler(Func<CancellationToken, Task<string>> respond) : HttpMessageHandler
    {
        internal int Requests { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            Check(request.RequestUri == ArenaChampionTierSource.Address, "se consulta solo la página global de campeones de Arena");
            return new(HttpStatusCode.OK) { Content = new StringContent(await respond(cancellationToken)) };
        }
    }
}

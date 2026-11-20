using System.IO;
using System.Net;
using System.Net.Http;
using Aumentos;

internal static class GgezArenaGlobalTests
{
    internal static async Task Run(Ratings ratings)
    {
        var catalog = ratings.Catalog;
        var patch = catalog.Patch;
        var ids = catalog.Augments.Where(card => card.Mode == nameof(Mode.Arena)).Select(card => card.Id).ToHashSet();
        var html = Page(patch);
        var snapshot = GgezArenaGlobalRatings.Read(html, patch, ids);
        Check(snapshot is { Champion: "", PlacementCount: 6, PlacementSchemaRead: true, SampleGames: 1800 } &&
            snapshot.Picks.GetValueOrDefault(48) is { GamesPicked: 450, AveragePlace: 3.25m, Rarity: 2 } &&
            snapshot.Picks.GetValueOrDefault(238) is { GamesPicked: 180, AveragePlace: null, Rarity: 1 } &&
            !snapshot.Picks.ContainsKey(99999), "la muestra global reconoce IDs, porcentajes y posiciones válidas");
        Reject(html.Replace("window:\"patch\"", "window:\"rolling\""), "rechaza ventanas que combinan parches");
        Reject(html.Replace("region:\"all\"", "region:\"lan\""), "rechaza una muestra de distinta región");
        Reject(html.Replace("matches:100", "matches:101"), "descarta una muestra de 3×6 inconsistente");
        Reject(html.Replace("href=\"/tierlist/arena/teams\"", "href=\"/tierlist/arena\""), "exige un formato global conocido");
        Reject(html.Replace("tierlist/arena/augments", "tierlist/arena"), "exige la página global de aumentos");
        Reject(Page("13.1"), "descarta un parche fuera de la ventana vigente");
        if (LeaguePatch.Previous(patch) is { } previous)
            Check(GgezArenaGlobalRatings.Read(Page(previous), patch, ids).IsPrevious, "identifica el parche anterior");

        var temp = Path.Combine(Path.GetTempPath(), $"aumentos-ggez-global-{Guid.NewGuid():N}");
        var directory = Path.Combine(temp, "ggez-arena", patch);
        var path = Path.Combine(directory, "global-augments.json");
        try
        {
            using var server = new Handler(html);
            using var http = new HttpClient(server);
            var source = new GgezArenaGlobalRatings(catalog, temp, http);
            var first = source.ForCaptureAsync(default);
            var second = source.ForCaptureAsync(default);
            Check((await first).Data is { PlacementCount: 6 } && (await second).Data == (await first).Data &&
                server.Requests == 1 && File.Exists(path), "las consultas globales concurrentes comparten la misma descarga");

            using var offline = new Handler(null);
            using var offlineHttp = new HttpClient(offline);
            var restarted = new GgezArenaGlobalRatings(catalog, temp, offlineHttp);
            Check((await restarted.ForCaptureAsync(default)).Data?.Picks[48].AveragePlace == 3.25m &&
                offline.Requests == 0, "la caché global conserva las posiciones y partidas tras reiniciar");
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
            if (Directory.Exists(directory))
                Directory.Delete(directory);
            var parent = Path.GetDirectoryName(directory)!;
            if (Directory.Exists(parent))
                Directory.Delete(parent);
            if (Directory.Exists(temp))
                Directory.Delete(temp);
        }
        Console.WriteLine("GGEZ.GG global verificado con filtro de parche, formato 3×6, posiciones y caché.");

        void Reject(string page, string message)
        {
            try { GgezArenaGlobalRatings.Read(page, patch, ids); }
            catch (InvalidDataException) { return; }
            throw new InvalidOperationException(message);
        }
    }

    private static string Page(string patch) => $$$"""
        <link rel="canonical" href="{{{GgezArenaGlobalRatings.Address}}}"/>
        <a href="/tierlist/arena/teams">Teams of three</a>
        <script>kit.start({data:[{data:{result:{patch:"{{{patch}}}",total:1800,matches:100,rows:[
        {id:48,games:450,avgPlace:3.25,top3Rate:55,firstRate:12,pickShare:25,name:"Jeweled Gauntlet",rarity:2},
        {id:238,games:180,avgPlace:7,top3Rate:57,pickShare:10,name:"Transmute: Prismatic",rarity:1},
        {id:99999,games:360,avgPlace:3,top3Rate:42,pickShare:20,name:"Unlisted",rarity:0}
        ]},filters:{region:"all",window:"patch"},min:20,namesOk:true}}]});</script>
        """;

    private static void Check(bool valid, string message)
    {
        if (!valid)
            throw new InvalidOperationException(message);
    }

    private sealed class Handler(string? html) : HttpMessageHandler
    {
        internal int Requests;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            Check(request.RequestUri == GgezArenaGlobalRatings.Address, "consulta la página pública global de aumentos");
            Interlocked.Increment(ref Requests);
            return Task.FromResult(html is null ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) :
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(html) });
        }
    }
}

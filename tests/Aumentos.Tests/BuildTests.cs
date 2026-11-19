using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using Aumentos;

internal static class BuildTests
{
    internal static async Task Run(Ratings ratings)
    {
        const string patch = "16.23";
        const string prior = "16.22";
        var catalog = ratings.Catalog with { Patch = patch };
        var zyra = new GameContext(Mode.Caos, "Zyra");
        var elise = new GameContext(Mode.Arena, "Elise");
        var mayhemHtml = Page(zyra, patch,
            Table("Objetos iniciales", [Item(2031, "Poción Reutilizable"), Item(3802, "Capítulo Perdido")]),
            Table("Botas", [Item(3020, "Botas del Hechicero")], [Item(3158, "Botas Jonias de la Lucidez")]),
            Table("Builds principales", [Item(2503, "Antorcha de Fuegoscuro"), Item(6653, "El Tormento de Liandry"),
                Item(3116, "Cetro de Cristal de Rylai")],
                [Item(2503, "Antorcha de Fuegoscuro"), Item(3116, "Cetro de Cristal de Rylai"), Item(6653, "El Tormento de Liandry")],
                [Item(3118, "Ignorado")]),
            Table("Sinergias", [Item(9999, "Ignorado")]));
        var mayhem = OpGgBuildPage.Read(mayhemHtml, zyra, patch);
        Check(mayhem is { IsPrevious: false, SourcePatch: patch, Groups.Length: 3 } &&
            mayhem.Groups.Single(group => group.Section == OpGgBuildSection.Start).Choices.Single()[1].Name == "Capítulo Perdido",
            "los objetos iniciales de Caos conservan sus nombres localizados");
        var core = mayhem.Groups.Single(group => group.Section == OpGgBuildSection.Core).Choices;
        Check(core.Length == 2 && core[0].Select(item => item.Id).SequenceEqual([2503, 6653, 3116]) &&
            core[1].Select(item => item.Id).SequenceEqual([2503, 3116, 6653]),
            "las dos rutas conservan el orden de los objetos de OP.GG");

        var arenaHtml = Page(elise, prior,
            Table("Objetos iniciales"),
            Table("Builds principales", [Item(223020, "Botas del Hechicero")], [Item(223158, "Botas Jonias de la Lucidez")]),
            Table("Objetos prismáticos", [Item(444636, "Cosechador Nocturno")]),
            Table("Builds principales", [Item(224645, "Lumbría"), Item(223089, "Sombrero Mortífero de Rabadon"),
                Item(223135, "Báculo del Vacío")]));
        var arena = OpGgBuildPage.Read(arenaHtml, elise, patch);
        Check(arena.IsPrevious && arena.Groups.Select(group => group.Section)
            .SequenceEqual([OpGgBuildSection.Boots, OpGgBuildSection.Core, OpGgBuildSection.Prismatic]) &&
            arena.Groups.Single(group => group.Section == OpGgBuildSection.Boots).Choices[0][0].Id == 223020,
            "Arena separa sus botas de las rutas principales y conserva los prismáticos");
        Reject(arenaHtml, zyra, "una página de otro modo o campeón se rechaza");
        Reject(arenaHtml.Replace("parche 16.22", "parche 16.21"), elise, "un parche demasiado antiguo se rechaza");
        Reject(arenaHtml.Replace("parche 16.22", "parche 16.24"), elise, "un parche futuro se rechaza");

        using var directory = new TemporaryFolder();
        var firstPage = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = new Handler((_, token) => firstPage.Task.WaitAsync(token));
        using var http = new HttpClient(server);
        var provider = new OpGgBuilds(catalog, directory.Path, http);
        Check(provider.Current(zyra, default).Data is null, "la consulta inicial deja libre al overlay");
        var read = provider.ForCaptureAsync(zyra, default);
        var simultaneousRead = provider.ForCaptureAsync(zyra with { Champion = "zyra" }, default);
        firstPage.SetResult(Ok(mayhemHtml));
        var downloaded = await read;
        Check(downloaded.Data is { IsPrevious: false } && (await simultaneousRead).Data == downloaded.Data &&
            server.Requests == 1 && provider.SourceUrl == OpGgBuildPage.Address(zyra).AbsoluteUri,
            "una sola descarga para el mismo campeón y acceso a la página de origen");

        var nextServer = new Handler((_, _) => Task.FromResult(Ok(arenaHtml.Replace("parche 16.22", "parche 16.23"))));
        using var nextHttp = new HttpClient(nextServer);
        var restarted = new OpGgBuilds(catalog, directory.Path, nextHttp);
        Check((await restarted.ForCaptureAsync(zyra, default)).Data is { IsPrevious: false } && nextServer.Requests == 0,
            "la build del parche vigente reusa su copia en disco");
        var arenaCache = Path.Combine(directory.Path, "opgg-builds", patch, "Arena-elise.json");
        File.WriteAllText(arenaCache, JsonSerializer.Serialize(arena with { FetchedAt = DateTimeOffset.UtcNow.AddHours(-1) }));
        Check((await restarted.ForCaptureAsync(elise, default)).Data is { IsPrevious: false } && nextServer.Requests == 1,
            "el parche vigente reemplaza la build anterior");

        var blocked = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        using var offlineHttp = new HttpClient(blocked);
        File.WriteAllText(arenaCache, JsonSerializer.Serialize(arena with { FetchedAt = DateTimeOffset.UtcNow.AddHours(-1) }));
        var fallback = await new OpGgBuilds(catalog, directory.Path, offlineHttp).ForCaptureAsync(elise, default);
        Check(fallback.Data is { IsPrevious: true } && fallback.Status.Contains("Parche anterior 26.22") &&
            fallback.Status.Contains("Copia guardada") && blocked.Requests == 1,
            "si falla OP.GG se identifica la copia del parche inmediatamente anterior");
        Console.WriteLine("Builds de OP.GG verificadas para ARAM Caos y Arena.");

        static void Reject(string html, GameContext game, string message)
        {
            try { OpGgBuildPage.Read(html, game, patch); }
            catch (InvalidDataException) { return; }
            throw new InvalidOperationException(message);
        }
    }

    private static string Page(GameContext game, string patch, params string[] tables) =>
        $"""<title>Build de {game.Champion}, parche {patch}</title><link href="{OpGgBuildPage.Address(game)}" rel="canonical">{string.Concat(tables)}""";

    private static string Table(string title, params string[][] rows) =>
        $"<table><caption>{title}</caption><thead><tr><th>{title}</th></tr></thead><tbody>" +
        string.Concat(rows.Select(items => $"<tr><td>{string.Concat(items)}</td><td>20 %</td></tr>")) + "</tbody></table>";

    private static string Item(int id, string name) =>
        $"""<img alt="{name}" src="https://opgg-static.akamaized.net/meta/images/lol/16.23.1/item/{id}.png?w=64">""";

    private static HttpResponseMessage Ok(string html) => new(HttpStatusCode.OK) { Content = new StringContent(html) };

    private static void Check(bool success, string message)
    {
        if (!success) throw new InvalidOperationException(message);
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
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
        internal string Path { get; } = Directory.CreateTempSubdirectory("Aumentos-BuildTests-").FullName;
        public void Dispose() => Directory.Delete(Path, true);
    }
}

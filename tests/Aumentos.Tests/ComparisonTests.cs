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
        Check(labels[0] == new CardComparison("A", new OpGgCard("A")) &&
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
        Check(unavailable.Data is null && unavailable.Status.Contains("15.1") && oldPage.Requests == 2,
            "si ninguna página tiene un parche aplicable, se informa la causa y no se asigna un dato");
        await restarted.ForCaptureAsync(elise, default);
        Check(oldPage.Requests == 2, "la fuente no se vuelve a consultar por cada carta");

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
        await CheckZileanOffer(ratings, lux, patch, folder.Path);
        await CheckRellArenaOffer(ratings, patch);
        CheckRellRoundTen(ratings, patch);
        CheckRellConditionalOffers(ratings, patch);
        CheckRellPrismaticAugments(ratings, patch);
        CheckRellSilverAugments(ratings, patch);
        Console.WriteLine("Comparación OP.GG, rerolls, caché y aislamiento de parches verificados.");
        await CheckAramMayhem(ratings, lux, elise);
        await CheckGgezArena(ratings, lux, elise);
    }

    private static void CheckRellConditionalOffers(Ratings ratings, string patch)
    {
        var rell = new GameContext(Mode.Arena, "Rell");
        var ids = new[] { "De Basura a Tesoro", "Mutación Parasitaria", "A Tope" }
            .Select(name => ratings.Recognize(Mode.Arena, name)?.Id).ToArray();
        var cards = CardComparison.ForOffer(ratings, rell, ids, new bool[3], null);
        Check(ids.SequenceEqual([389, 240, 2016]) && cards is
            [{ Local: "Variable", LocalReason: "Prismático al eliminar o reemplazar esta carta" },
             { Local: "Variable", LocalReason: "Copia un aumento rival cada ronda" }, { Local: "B", LocalReason: null }],
            "cada carta reconocida tiene un tier o una condición explícita aunque falten ambas fuentes");
        Check(ratings.Rate(Mode.Arena, "Jinx", 2016) == Tier.A && ratings.Rate(Mode.Arena, "Rell", 389) is null,
            "A Tope favorece los ataques y De Basura a Tesoro conserva una valoración condicional");

        int?[] otherIds = [322, 136, 57, 1344, 193, 108, 110];
        var opgg = new OpGgSnapshot(patch, Mode.Arena, "Rell", DateTimeOffset.UtcNow,
            new() { [322] = new(PickPercent: 1.87m) });
        var other = CardComparison.ForOffer(ratings, rell, otherIds, new bool[otherIds.Length], opgg);
        Check(other.Select(card => card?.Local).SequenceEqual(["Variable", "A", "A", "A", "B", "A", "B"]) &&
            other[0] is { LocalReason: "Depende del daño de tus objetos y aumentos", External.PickPercent: 1.87m },
            "las sinergias observadas aparecen y la valoración condicional convive con el porcentaje publicado");
    }

    private static void CheckRellRoundTen(Ratings ratings, string patch)
    {
        var rell = new GameContext(Mode.Arena, "Rell");
        var ids = new[] { "Infranqueable", "Curación Crítica", "Movimiento Sombrío" }
            .Select(name => ratings.Recognize(Mode.Arena, name)?.Id).ToArray();
        var opgg = new OpGgSnapshot(patch, Mode.Arena, "Rell", DateTimeOffset.UtcNow,
            new() { [149] = new(PickPercent: 4.71m), [118] = new(PickPercent: 1.73m) });
        var cards = CardComparison.ForOffer(ratings, rell, ids, new bool[3], opgg);
        Check(ids.SequenceEqual([149, 118, 73]) && cards.Select(card => card?.LocalText).SequenceEqual(["S+", "B", "B"]) &&
            cards[0]?.External?.PickPercent == 4.71m && cards[1]?.External?.PickPercent == 1.73m && cards[2]?.External is null,
            "las letras de Rell se muestran con o sin porcentaje publicado");
    }

    private static async Task CheckRellArenaOffer(Ratings ratings, string patch)
    {
        var rell = new GameContext(Mode.Arena, "Rell");
        var ids = new[] { "Plan de Escape", "Profanador", "Purista: Hechicero" }
            .Select(name => ratings.Recognize(rell.Mode, name)?.Id).ToArray();
        Check(ids.SequenceEqual([1333, 218, 2018]), "las tres cartas de Arena se reconocen por título");

        var ggez = new GgezArenaSnapshot("Rell", patch, patch, DateTimeOffset.UtcNow, 833,
            new() { [218] = new(13.085m, 109) });
        var offer = CardComparison.ForOffer(ratings, rell, ids, new bool[3], null, ggez: ggez);
        Check(offer.Select(card => card?.Local).SequenceEqual(["A", "A", "B"]) &&
            offer[1]?.Arena?.GamesPicked == 109 && offer[0]?.Arena is null && offer[2]?.Arena is null,
            "las afinidades de Rell cubren las tres cartas y respetan la cobertura de GGEZ.GG");

        if (LeaguePatch.Previous(patch) is not { } previous)
            return;
        const string rows = """{"data":{"1":[{"id":218,"pick_rate":14}]},"mode":"arena"}""";
        const string currentRows = """{"data":{"1":[{"id":218,"pick_rate":16},{"id":2018,"pick_rate":2}]},"mode":"arena"}""";
        using var folder = new TemporaryFolder();
        var server = new PageHandler((request, _) =>
        {
            var localized = request.RequestUri!.AbsolutePath.StartsWith("/es-419/");
            return Task.FromResult(Ok(Page(rell, previous, rows, localized)));
        });
        using var http = new HttpClient(server);
        var view = await new OpGgRatings(ratings.Catalog, folder.Path, http).ForCaptureAsync(rell, default);
        var prior = view.Data;
        offer = CardComparison.ForOffer(ratings, rell, ids, new bool[3], prior, ggez: ggez);
        var previousDisplay = LeaguePatch.Public(previous);
        Check(prior is { IsPrevious: true } && server.Requests == 2 && view.Status.Contains(previousDisplay) &&
            offer[1]?.ExternalText == $"Elección 14 % · parche ant. {previousDisplay}" &&
            offer[1]?.Arena?.GamesPicked == 109 && offer[0]?.External is null && offer[2]?.External is null,
            "el parche anterior se identifica en la carta y no se mezcla con el dato vigente de GGEZ.GG");

        var recheck = new PageHandler((request, _) =>
        {
            var localized = request.RequestUri!.AbsolutePath.StartsWith("/es-419/");
            return Task.FromResult(Ok(localized ? Page(rell, patch, currentRows, localized: true) : Page(rell, previous, rows)));
        });
        using var recheckHttp = new HttpClient(recheck);
        var cached = await new OpGgRatings(ratings.Catalog, folder.Path, recheckHttp).ForCaptureAsync(rell, default);
        Check(cached.Data is { IsPrevious: true } && recheck.Requests == 0,
            "la copia anterior recién descargada se reutiliza");
        var cacheFile = Directory.GetFiles(folder.Path, "*.json", SearchOption.AllDirectories).Single();
        File.WriteAllText(cacheFile, JsonSerializer.Serialize(prior! with { FetchedAt = DateTimeOffset.UtcNow.AddMinutes(-16) }));
        var current = await new OpGgRatings(ratings.Catalog, folder.Path, recheckHttp).ForCaptureAsync(rell, default);
        var refreshed = CardComparison.ForOffer(ratings, rell, ids, new bool[3], current.Data);
        Check(current.Data is { IsPrevious: false } && recheck.Requests == 2 &&
            refreshed[1]?.ExternalText == "Elección 16 %" && refreshed[2]?.ExternalText == "Elección 2 %",
            "la copia anterior se revisa a los 15 minutos y cede ante la tabla vigente localizada");
    }

    private static void CheckRellPrismaticAugments(Ratings ratings, string patch)
    {
        var rell = new GameContext(Mode.Arena, "Rell");
        var ids = new[] { "Archimago", "(2) Incendio", "Caída de Icathia" }
            .Select(title => ratings.Recognize(rell.Mode, title)?.Id).ToArray();
        Check(ids.SequenceEqual([2087, 379, 393]) && ratings.Recognize(rell.Mode, "(2) Inccendio") is null,
            "el OCR recupera el título exacto tras un prefijo numérico de interfaz");

        if (LeaguePatch.Previous(patch) is not { } previous)
            return;
        var catalog = ratings.Catalog.Augments.Where(card => card.Mode == nameof(Mode.Arena)).ToDictionary(card => card.Id);
        const string rows = """{"data":{"8":[{"id":379,"pick_rate":1.66},{"id":393,"pick_rate":4.9}]},"mode":"arena"}""";
        var source = OpGgPage.Read(Page(rell, previous, rows), rell, patch, catalog);
        var ggez = new GgezArenaSnapshot("Rell", patch, patch, DateTimeOffset.UtcNow, 833,
            new() { [2087] = new(1.08m, 9), [379] = new(3.48m, 29), [393] = new(7.44m, 62) });
        var offer = CardComparison.ForOffer(ratings, rell, ids, new bool[3], source, ggez: ggez);
        Check(offer.Select(card => card?.Local).SequenceEqual(["C", "B", "A"]) &&
            offer.Select(card => card?.External?.PickPercent).SequenceEqual([null, 1.66m, 4.9m]) &&
            offer.Skip(1).All(card => card?.ExternalPreviousPatch == LeaguePatch.Public(previous)) &&
            offer.Select(card => card?.Arena?.GamesPicked).SequenceEqual([9, 29, 62]),
            "la oferta prismática de aumentos conserva la cobertura real de cada fuente y recomienda la misión defensiva");
    }

    private static void CheckRellSilverAugments(Ratings ratings, string patch)
    {
        var rell = new GameContext(Mode.Arena, "Rell");
        var ids = new[] { "Enroque", "Compañeros de Mejora", "Atormentador" }
            .Select(title => ratings.Recognize(rell.Mode, title)?.Id).ToArray();
        var source = new OpGgSnapshot(patch, rell.Mode, rell.Champion, DateTimeOffset.UtcNow,
            new() { [85] = new(PickPercent: 4.82m) }, LeaguePatch.Previous(patch));
        var ggez = new GgezArenaSnapshot(rell.Champion, patch, patch, DateTimeOffset.UtcNow, 833,
            new() { [85] = new(4.8m, 40) });
        var offer = CardComparison.ForOffer(ratings, rell, ids, new bool[3], source, ggez: ggez);
        Check(ids.SequenceEqual([12, 9, 85]) && offer.Select(card => card?.Local).SequenceEqual(["A", "B", "A"]) &&
            offer.Take(2).All(card => card is { External: null, Arena: null }) &&
            offer[2]?.External?.PickPercent == 4.82m && offer[2]?.Arena?.GamesPicked == 40,
            "las estimaciones de sinergia de Rell se muestran aunque las fuentes solo cubran Atormentador");
    }

    private static async Task CheckZileanOffer(Ratings ratings, GameContext lux, string patch, string settings)
    {
        var zilean = new GameContext(Mode.Caos, "Zilean");
        const string rows = """{"data":[{"id":1211,"tier":4,"key":"ARAM_ItsKillingTime"},{"id":1373,"tier":0,"key":"ShrinkEngine"},{"id":2103,"tier":1,"key":"ARAM_BangBang"}]}""";
        var server = new PageHandler((request, _) => Task.FromResult(Ok(
            request.RequestUri!.AbsolutePath == "/es-419/lol/modes/aram-mayhem/zilean/augments"
                ? Page(zilean, patch, rows, localized: true) : Page(lux, patch, rows))));
        using var http = new HttpClient(server);
        var source = new OpGgRatings(ratings.Catalog, settings, http);
        int?[] ids = [1211, 1373, 2103];
        var missing = CardComparison.ForOffer(ratings, zilean, ids, new bool[3], null);
        Check(missing.Select(card => card?.Local).SequenceEqual(["C", "A", "A"]),
            "las tres cartas de Zilean tienen estimación local aunque OP.GG no esté disponible");

        var view = await source.ForCaptureAsync(zilean, default);
        var offer = CardComparison.ForOffer(ratings, zilean, ids, new bool[3], view.Data);
        Check(server.Requests == 2 && offer.Select(card => card?.ExternalText).SequenceEqual(["D", "S", "A"]),
            "si OP.GG devuelve otro campeón, la página localizada recupera los tiers de Zilean");
    }

    private static async Task CheckAramMayhem(Ratings ratings, GameContext lux, GameContext elise)
    {
        var current = LeaguePatch.Public(ratings.Catalog.Patch);
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

    private static async Task CheckGgezArena(Ratings ratings, GameContext lux, GameContext elise)
    {
        var patch = ratings.Catalog.Patch;
        var ids = ratings.Catalog.Augments.Where(card => card.Mode == nameof(Mode.Arena)).Select(card => card.Id).ToHashSet();
        using var folder = new TemporaryFolder();
        var server = new PageHandler((_, _) => Task.FromResult(Ok(Page(elise.Champion, patch, "patch"))));
        using var http = new HttpClient(server);
        var source = new GgezArenaRatings(ratings.Catalog, folder.Path, http);
        Check(source.Current(lux, default) is null && await source.ForCaptureAsync(lux, default) is null && server.Requests == 0,
            "Caos no consulta GGEZ.GG");
        var work = source.ForCaptureAsync(elise, default);
        var second = source.ForCaptureAsync(elise, default);
        var ready = await work;
        var data = ready?.Data;
        Check(data is { IsPrevious: false, SampleGames: 760 } && (await second)?.Data == data && server.Requests == 1 &&
            source.SourceUrl == GgezArenaPage.Address(elise.Champion).AbsoluteUri, "una descarga por campeón y enlace a la fuente");
        var labels = CardComparison.ForOffer(ratings, elise, [97, 120, 1034, null], new bool[4], null, ggez: data);
        Check(labels[0]?.Arena is { GamesPicked: 59, SampleGames: 760, Percent: 7.7631578947368425m } &&
            labels[1]?.Arena?.GamesPicked == 17 && labels[2]?.Arena is null && labels[3] == CardComparison.Unread,
            "porcentaje y muestra para cada carta, con rechazo de una fila inconsistente");
        Check(CardComparison.ForOffer(ratings, elise, [120], [false], null, ggez: data)[0] == labels[1] &&
            CardComparison.ForOffer(ratings, elise with { Champion = "Lux" }, [97], [false], null, ggez: data)[0]?.Arena is null,
            "reroll y aislamiento del campeón");

        var broken = new PageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        using var offline = new HttpClient(broken);
        var restarted = new GgezArenaRatings(ratings.Catalog, folder.Path, offline);
        Check((await restarted.ForCaptureAsync(elise, default))?.Data?.Picks[97].GamesPicked == 59 && broken.Requests == 0,
            "reutiliza una copia válida al volver a iniciar");
        var stale = data! with { FetchedAt = DateTimeOffset.UtcNow.AddDays(-1) };
        var cache = Directory.GetFiles(folder.Path, "*.json", SearchOption.AllDirectories).Single();
        File.WriteAllText(cache, JsonSerializer.Serialize(stale));
        Check((await new GgezArenaRatings(ratings.Catalog, folder.Path, offline).ForCaptureAsync(elise, default))?.Data?.SampleGames == 760 &&
            broken.Requests == 1, "conserva la copia al fallar la red");

        if (LeaguePatch.Previous(patch) is { } previous)
        {
            var older = GgezArenaPage.Read(Page(elise.Champion, previous, "patch"), elise.Champion, patch, ids);
            Check(older.IsPrevious, "identifica datos del parche anterior");
        }
        Reject(Page(elise.Champion, "13.1", "patch"), "rechaza un parche vencido");
        Reject(Page(elise.Champion, patch, "rolling"), "rechaza una ventana que mezcla parches");
        Reject(Page(lux.Champion, patch, "patch"), "rechaza una página de otro campeón");
        Reject(Page(elise.Champion, patch, "patch").Replace("champion:\"Elise\"", "champion:\"Lux\""),
            "rechaza datos de otro campeón incluso con la página correcta");
        Check(GgezArenaPage.Address("MonkeyKing").AbsolutePath == "/champion/Wukong/arena" &&
            GgezArenaPage.Read(Page("MonkeyKing", patch, "patch"), "MonkeyKing", patch, ids).SampleGames == 760,
            "la página de Wukong usa su nombre público y conserva la identidad del cliente");
        Console.WriteLine("GGEZ.GG verificado con porcentaje, partidas, parche, caché y aislamiento de Arena.");

        void Reject(string html, string message)
        {
            try { GgezArenaPage.Read(html, elise.Champion, patch, ids); }
            catch (InvalidDataException) { return; }
            throw new InvalidOperationException(message);
        }

        static string Page(string champion, string pagePatch, string window) => $$$"""
            <link rel="canonical" href="{{{GgezArenaPage.Address(champion)}}}"/>
            <script>kit.start({data:[{type:"data",data:{champion:"{{{champion}}}",id:60,filters:{region:"all",window:"{{{window}}}"},available:true,patch:"{{{pagePatch}}}",matches:8047,row:{games:610},augments:[
            {id:97,games:59,avgPlace:3.4,top3Rate:53.7,pickShare:7.7631578947368425,name:"Witchful Thinking"},
            {id:120,games:17,pickShare:2.236842105263158,name:"Serve Beyond Death"},
            {id:1034,games:70,pickShare:10,name:"mismatched"},
            {id:99999,games:0,pickShare:0}],augOk:true,namesOk:true,teamsOk:true,augTotal:760}}]});</script>
            """;
    }

    private static string Page(GameContext game, string patch, string props, bool localized = false)
    {
        var record = $"1:[\"$\",\"$L1\",null,{props}]\n";
        var mid = record.Length / 2;
        static string Chunk(string text) =>
            $"<script>self.__next_f.push({JsonSerializer.Serialize(new object[] { 1, text })})</script>";
        var title = localized ? $"{game.Mode} parche {patch}" : $"Build Patch {patch}: Augments";
        return $"<title>{title} | OP.GG</title><link rel=\"canonical\" href=\"{OpGgPage.Address(game, localized)}\"/>" +
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

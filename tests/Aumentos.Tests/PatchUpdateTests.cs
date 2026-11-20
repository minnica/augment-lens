using System.IO;
using Aumentos;

internal static class PatchUpdateTests
{
    internal static async Task Run(Ratings ratings, string assets, bool live)
    {
        var patch = ratings.Catalog.Patch;
        var aliases = ArenaChampionTierPage.Aliases(ratings.Catalog);
        const string names = """
            "54":{id:"Malphite",name:"Malphite",key:"54"},"157":{id:"Yasuo",name:"Yasuo",key:"157"},"777":{id:"Yone",name:"Yone",key:"777"}
            """;
        const string row = "{ids:[54,157,777],top3:47,first:20,games:60,avgPlace:2.5,top3Rate:78.3,firstRate:33.3}";
        var page = ArenaTeamSource.Read(Page(patch), patch, 0, aliases);
        var team = new ArenaDraftTeam(["Yone", "Malphite"], [], 3);
        var result = page.Teams[ArenaTeamSource.TeamKey(["Yasuo", "Yone", "Malphite"])];
        Check(ArenaCompositionAdvice.Evaluate(ratings, "Yasuo", team, result) is { Observed.Games: 60, Tier: "S" },
            "un trío exacto con muestra usa el puesto ajustado y muestra su origen");
        Check(ArenaCompositionAdvice.Evaluate(ratings, "Yasuo", team with { Teammates = ["Yone"] }, result) is { Observed: null },
            "la preselección con un integrante pendiente conserva su estimación");
        Check(ArenaCompositionAdvice.Evaluate(ratings, "Yasuo", team, result with { Games = 2 }) is { Observed: null },
            "una muestra escasa conserva la estimación local");
        Check(ArenaTeamSource.Read(Page(patch, sample: 19), patch, 0, aliases).Teams.Count == 0,
            "los tríos con menos de 20 partidas se descartan al importar");
        Check(Rejects(Page("12.1")) && Rejects(Page(patch, players: 1600)),
            "el formato 2×8 y un parche distinto se rechazan");

        var first = DateTimeOffset.UtcNow;
        Check(PatchStatistics.RefreshDay(first, 0, first, first.AddDays(1)) == 0 &&
            PatchStatistics.RefreshDay(first, 0, first, first.AddDays(2)) == 2 &&
            PatchStatistics.RefreshDay(first, 2, first.AddDays(2), first.AddDays(7)) == 7 &&
            PatchStatistics.RefreshDay(first, 2, first.AddDays(2), first.AddDays(7), review: false) == 0 &&
            PatchStatistics.RefreshDay(first, 7, first.AddDays(7), first.AddDays(8)) == 0, "ventanas de importación por parche");

        var build = new OpGgBuildSnapshot(Mode.Arena, "Yasuo", patch, patch, first,
            [new(OpGgBuildSection.Core, [[new(223089, "Capucha"), new(223135, "Bastón")]])]);
        var observed = new GgezArenaSnapshot("Yasuo", patch, patch, first, 100, [], ItemGames: new() { [223089] = 50 });
        Check(PatchStatistics.CompareBuild(build, observed)?.Contains("1/2", StringComparison.Ordinal) == true &&
            PatchStatistics.CompareBuild(build, observed with { SourcePatch = "12.1" }) is null &&
            PatchStatistics.CompareBuild(build with { Mode = Mode.Caos }, observed) is null, "cotejo de builds del mismo modo, campeón y parche");

        if (live)
        {
            var folder = Path.Combine(Path.GetTempPath(), $"AumentosLoL-patch-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(folder);
            try
            {
                using var http = OpGgRatings.CreateClient();
                var teamSource = new ArenaTeamSource(ratings.Catalog, folder, http);
                var liveTeams = await teamSource.Prefetch(CancellationToken.None);
                Check(liveTeams is { Matches: > 0 } && liveTeams.Patch == patch, "página real de GGEZ verificada");
                Console.WriteLine($"GGEZ {patch} · {liveTeams!.Matches} partidas · {liveTeams.Teams.Count} tríos con {ArenaTeamSource.MinimumGames}+ partidas.");
                var ggez = await new GgezArenaRatings(ratings.Catalog, folder, http)
                    .ForCaptureAsync(new GameContext(Mode.Arena, "Elise"), CancellationToken.None);
                Check(ggez?.Data is { ItemGames.Count: > 0 } && ggez.Data.SourcePatch == patch, "objetos del parche en la página real de GGEZ");

                var tools = Path.Combine(AppContext.BaseDirectory, "PatchTools");
                var catalog = new PatchCatalog(assets, folder, tools);
                Check(catalog.Select(patch) == assets, "se conserva el catálogo incluido antes de descargar");
                await catalog.Prepare(patch, Console.WriteLine, CancellationToken.None);
                string oldBundle = Path.Combine(folder, "bundled-previous");
                Check(StatsCache.Write(Path.Combine(oldBundle, "catalog.json"), ratings.Catalog with { Patch = "12.1" }),
                    "se preparó un catálogo anterior para probar la transición");
                var update = new PatchCatalog(oldBundle, folder, tools);
                string downloaded = update.Select(patch);
                Check(downloaded != oldBundle && new Ratings(downloaded, allowMissingRules: true).Catalog.Patch == patch,
                    "se activa el catálogo generado y validado con datos reales");
                Check(catalog.Select(patch) == assets, "una compilación del parche vigente conserva su catálogo incluido");
                Check(catalog.Select("12.1") == assets, "solo se activa el catálogo del parche solicitado");
                Console.WriteLine($"Catálogo {patch} descargado y validado en una ubicación temporal.");
            }
            finally { Directory.Delete(folder, recursive: true); }
        }

        bool Rejects(string html)
        {
            try { ArenaTeamSource.Read(html, patch, 0, aliases); return false; }
            catch (InvalidDataException) { return true; }
        }

        static string Page(string patch, int players = 1800, int sample = 60) => $$"""
            <link rel="canonical" href="https://ggez.gg/tierlist/arena/teams"/>
            <script>data:{result:{patch:"{{patch}}",matches:100,players:{{players}},teams:[{{row.Replace("games:60", $"games:{sample}")}}],pairs:[]},names:{ {{names}} },filters:{region:"all",window:"patch"},champ:null,min:20}</script>
            """;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}

using Aumentos;

internal static class ArenaCompositionTests
{
    internal static void Run(Ratings ratings)
    {
        var patch = ratings.Catalog.Patch;
        var tiers = new ArenaChampionTierList(patch, patch, DateTimeOffset.UtcNow, 1200,
            new Dictionary<string, ArenaChampionTier>(StringComparer.OrdinalIgnoreCase)
            {
                ["Nunu"] = new("Nunu", "S", 57m, 600),
                ["Malphite"] = new("Malphite", "S", 56m, 650),
                ["Leona"] = new("Leona", "S", 60m, 675),
                ["Alistar"] = new("Alistar", "A", 58m, 500),
                ["Jinx"] = new("Jinx", "S", 62m, 700),
                ["Vayne"] = new("Vayne", "D", 50m, 780),
                ["Kaisa"] = new("Kaisa", "C", 49m, 550),
                ["Braum"] = new("Braum", "S", 63m, 790)
            });

        Check(ArenaCompositionAdvice.Recommend(ratings, tiers, null).Length == 0 &&
            ArenaCompositionAdvice.Recommend(ratings, tiers, Team(["Desconocido"], "Vayne")).Length == 0,
            "la composición necesita al menos un compañero reconocido");

        var openFront = Team(["Jinx", "LUX"], "Nunu", "Malphite", "Alistar", "Leona", "Vayne", "Jinx");
        var recommendations = ArenaCompositionAdvice.Recommend(ratings, tiers,
            openFront with { Unavailable = [.. openFront.Unavailable, "lEOnA"] });
        Check(recommendations.Select(pick => (pick.Champion, pick.Tier)).SequenceEqual(
                [("Nunu y Willump", "S"), ("Malphite", "S"), ("Alistar", "A")]) &&
            recommendations.All(pick => pick.Reason == "Aporta primera línea"),
            "la primera línea faltante manda, el tier desempata y se excluyen bloqueos y compañeros");

        var openCarry = Team(["Alistar", "Leona"], "Braum", "Kaisa", "Vayne", "Sona");
        var damage = ArenaCompositionAdvice.Recommend(ratings, tiers, openCarry);
        Check(damage.Select(pick => pick.Champion).SequenceEqual(["Kai'Sa", "Vayne", "Sona"]) &&
            damage.Take(2).All(pick => pick.Reason.Contains("daño sostenido")) &&
            damage[2].Reason.Contains("protección"),
            "dos tanques necesitan daño sostenido y no priorizan un tercer tanque por su tier global");

        var openMagic = Team(["Jax", "Tristana"], "Jinx", "Lux", "Brand", "Nami");
        var withoutTiers = ArenaCompositionAdvice.Recommend(ratings, null, openMagic);
        Check(withoutTiers.Select(pick => pick.Champion).SequenceEqual(["Lux", "Brand", "Nami"]) &&
            withoutTiers.All(pick => pick.Tier is null) &&
            withoutTiers[0].Reason == "Aporta daño mágico y protección para el equipo",
            "los perfiles equilibran daño y protección aunque GGEZ.GG no tenga datos");

        var balanced = ArenaCompositionAdvice.Recommend(ratings, tiers,
            Team(["Jax", "Lux"], "Nunu", "Braum", "Rell"));
        Check(balanced.Length == 3 && balanced.All(pick => pick.Reason.Contains("más control")),
            "una composición equilibrada puede reforzar su control");

        var waiting = new ArenaDraftTeam([], [], 3);
        var protectedCarry = new ArenaDraftTeam(["Janna", "Chogath"], [], 3);
        var exposedCarry = new ArenaDraftTeam(["Jinx", "Tristana"], [], 3);
        var fullGrade = ArenaCompositionAdvice.Evaluate(ratings, "KogMaw", protectedCarry);
        Check(ArenaCompositionAdvice.Evaluate(ratings, "KogMaw", waiting) is null &&
            ArenaCompositionAdvice.Evaluate(ratings, null, protectedCarry) is null &&
            ArenaCompositionAdvice.Evaluate(ratings, "KogMaw", protectedCarry with { Teammates = ["KogMaw"] }) is null &&
            fullGrade is { Tier: "S", Members: 3, TeamSize: 3, Reason: "Protege el daño sostenido" } &&
            ArenaCompositionAdvice.Evaluate(ratings, "KogMaw", exposedCarry) is { Tier: not "S" } &&
            ArenaCompositionAdvice.Evaluate(ratings, "KogMaw", protectedCarry with { Teammates = ["Janna"] })
                is { Members: 2, TeamSize: 3 } &&
            ArenaCompositionAdvice.Evaluate(ratings, "KogMaw", protectedCarry with { Teammates = ["Janna"], Size = 2 })
                is { Members: 2, TeamSize: 2 }, "la sinergia valora el equipo real y distingue un trío parcial de un dúo");

        var engageGrade = ArenaCompositionAdvice.Evaluate(ratings, "Riven", new(["Kled", "Galio"], [], 3));
        Check(engageGrade is { Tier: "A" or "S", Reason: "Encadena control y daño" } &&
            ArenaCompositionAdvice.Evaluate(ratings, "Vayne", new(["Vayne", "Yuumi"], [], 2)) is { Members: 2 } &&
            ArenaCompositionAdvice.Evaluate(ratings, "Sona", new(["Yuumi"], [], 2)) is { Tier: "D" },
            "Arena premia el control encadenado con seguimiento y distingue un equipo sin presión ofensiva");
        Console.WriteLine("Recomendaciones de composición de Arena verificadas por perfiles, bloqueos y desempates.");

        ArenaDraftTeam Team(string[] teammates, params string[] candidates) => new(teammates,
            ratings.Catalog.Champions.Select(champion => champion.Key)
                .Except(candidates, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}

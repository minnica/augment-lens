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

        var waiting = new ArenaDraftTeam([], [], 3);
        var hover = new ArenaDraftTeam(["Leona", "Lux"], ["Vayne"], 3, OwnConfirmed: false, ConfirmedTeammates: ["Leona"]);
        Check(ArenaCompositionAdvice.ConfirmedPicks("Jinx", hover).SequenceEqual(["Leona"]) &&
            ArenaCompositionAdvice.ConfirmedPicks("Jinx", hover with { OwnConfirmed = true }).SequenceEqual(["Jinx", "Leona"]) &&
            ArenaCompositionAdvice.Recommend(ratings, tiers, [], [], 3).Length == 0,
            "las sugerencias comienzan con una elección confirmada y no se anclan a preselecciones");

        var firstPick = ArenaCompositionAdvice.Recommend(ratings, tiers, ["Leona"], ExcludeExcept("Jinx", "Lux", "Alistar", "Braum"), 3);
        Check(firstPick.Length > 1 &&
            firstPick[0].Members.Select(member => member.Champion).ToHashSet().SetEquals(["Leona", "Jinx", "Lux"]) &&
            firstPick.All(option => option.Members is [{ Champion: "Leona", Confirmed: true }, { Confirmed: false }, { Confirmed: false }]),
            "con una elección se sugieren dos compañeros para lograr daño sostenido con protección y control");

        var lastPick = ArenaCompositionAdvice.Recommend(ratings, null, ["Leona", "Jinx"],
            ExcludeExcept("Lux", "Nunu", "Alistar"), 3);
        Check(lastPick.Length == 3 && lastPick[0] is { Tier: "S", Members: [_, _, { Champion: "Lux" }] } &&
            lastPick.All(option => option.Members is [{ Champion: "Leona", Confirmed: true },
                { Champion: "Jinx", Confirmed: true }, { Confirmed: false }]) &&
            lastPick.All(option => option.Reason.Length > 0) &&
            ArenaCompositionAdvice.Recommend(ratings, tiers, ["Leona", "Jinx", "Lux"], [], 3).Length == 0,
            "la segunda elección recalcula la plaza restante sin datos externos y un trío completo deja de mostrar sugerencias");

        Check(ArenaCompositionAdvice.Recommend(ratings, tiers, ["Jinx"], ExcludeExcept("Braum"), 2)
            is [{ Members: [{ Champion: "Jinx", Confirmed: true }, { Champion: "Braum", Confirmed: false }] }] &&
            ArenaCompositionAdvice.Recommend(ratings, tiers, ["Jinx"], ExcludeExcept("Braum"), 3).Length == 0,
            "los dúos ofrecen una plaza y los tríos requieren dos opciones disponibles distintas");

        var partial = new ArenaDraftTeam(["Illaoi"], [], 3);
        var grade = ArenaCompositionAdvice.Evaluate(ratings, "Milio", partial);
        var protectedCarry = new ArenaDraftTeam(["Janna", "Chogath"], [], 3);
        var exposedCarry = new ArenaDraftTeam(["Jinx", "Tristana"], [], 3);
        var fullGrade = ArenaCompositionAdvice.Evaluate(ratings, "KogMaw", protectedCarry);
        Check(ArenaCompositionAdvice.Evaluate(ratings, "KogMaw", waiting) is null &&
            ArenaCompositionAdvice.Evaluate(ratings, null, protectedCarry) is null &&
            ArenaCompositionAdvice.Evaluate(ratings, "KogMaw", protectedCarry with { Teammates = ["KogMaw"] }) is null &&
            fullGrade is { Tier: "S", Members: 3, TeamSize: 3, Reason: "Equipo equilibrado" } &&
            ArenaCompositionAdvice.Evaluate(ratings, "KogMaw", exposedCarry) is { Tier: not "S", Reason: "Falta protección" } &&
            ArenaCompositionAdvice.Evaluate(ratings, "KogMaw", protectedCarry with { Teammates = ["Janna"] })
                is { Members: 2, TeamSize: 3 } &&
            ArenaCompositionAdvice.Evaluate(ratings, "KogMaw", protectedCarry with { Teammates = ["Janna"], Size = 2 })
                is { Members: 2, TeamSize: 2 }, "la sinergia valora el equipo real y distingue un trío parcial de un dúo");

        var engageGrade = ArenaCompositionAdvice.Evaluate(ratings, "Riven", new(["Kled", "Galio"], [], 3));
        Check(engageGrade is { Tier: "A" or "S", Reason: "Falta daño sostenido" } &&
            ArenaCompositionAdvice.Evaluate(ratings, "Vayne", new(["Vayne", "Yuumi"], [], 2)) is { Members: 2 } &&
            ArenaCompositionAdvice.Evaluate(ratings, "Sona", new(["Yuumi"], [], 2)) is { Tier: "D" },
            "Arena premia el control encadenado con seguimiento y distingue un equipo sin presión ofensiva");
        Check(grade is { Tier: "C", Members: 2, TeamSize: 3, Reason: "Falta daño sostenido" } &&
            ArenaCompositionAdvice.Evaluate(ratings, "KogMaw", new(["Milio"], [], 2))?.Reason == "Falta control" &&
            ArenaCompositionAdvice.Evaluate(ratings, "Ashe", new(["Milio"], [], 2))?.Reason == "Falta daño de seguimiento" &&
            ArenaCompositionAdvice.Evaluate(ratings, "KogMaw", new(["Janna"], [], 2))?.Reason == "Falta aguante",
            "la evaluación encabeza con la necesidad del equipo reconocido");
        var observed = new ArenaTeamResult(patch, 80, 2.30m);
        Check(ArenaCompositionAdvice.Evaluate(ratings, "Milio", partial with { Teammates = ["Illaoi", "Sona"] }, observed) is
            { Tier: "S", Observed.Games: 80, Reason: "Falta daño sostenido" },
            "la estadística del trío conserva su tier sin desplazar la necesidad detectada");
        Console.WriteLine("Recomendaciones de composición de Arena verificadas por perfiles, bloqueos y desempates.");

        string[] ExcludeExcept(params string[] candidates) => ratings.Catalog.Champions.Select(champion => champion.Key)
            .Except(candidates, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}

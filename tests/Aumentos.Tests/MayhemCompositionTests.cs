using Aumentos;

internal static class MayhemCompositionTests
{
    internal static void Run(Ratings ratings)
    {
        var advisor = new MayhemCompositionAdvice(ratings);
        var tiers = new MayhemChampionTierList(ratings.Catalog.Patch, DateTimeOffset.UtcNow, new()
        {
            ["rell"] = 1, ["sejuani"] = 2, ["thresh"] = 3, ["vayne"] = 1, ["milio"] = 1,
            ["caitlyn"] = 2, ["brand"] = 3, ["jinx"] = 1
        });

        var backline = new[] { "Jinx", "LUX", "Brand" };
        var choices = new[] { "milio", "Sejuani", "Rell", "Thresh", "Vayne", "Jinx", "Rell" };
        var frontline = advisor.Recommend(tiers, "Milio", backline, choices, ["rEll"]);
        Check(frontline.Select(pick => (pick.Champion, pick.Tier)).SequenceEqual([("Sejuani", "A"), ("Thresh", "B")]) &&
            frontline.All(pick => pick.Reason.Contains("primera línea", StringComparison.OrdinalIgnoreCase)),
            "solo se sugieren campeones disponibles y se excluyen el propio, los aliados y los bloqueos");
        var allowed = advisor.Recommend(tiers, "Milio", backline, choices);
        Check(allowed.Select(pick => pick.Champion).SequenceEqual(["Rell", "Sejuani", "Thresh"]),
            "los campeones que aportan al equipo se ordenan por su tier de OP.GG");
        var ranked = tiers with
        {
            Tiers = new(tiers.Tiers) { ["sejuani"] = 1 },
            Ranks = new() { ["sejuani"] = 3, ["rell"] = 5 }
        };
        Check(advisor.Recommend(ranked, "Milio", backline, choices)[0].Champion == "Sejuani",
            "a igual afinidad y tier se usa el puesto de OP.GG cuando está publicado");

        var tanks = new[] { "Alistar", "Rell", "Nautilus" };
        var damage = advisor.Recommend(tiers, null, tanks, ["Braum", "Ashe", "Jinx", "Brand", "Caitlyn"]);
        Check(damage.Select(pick => (pick.Champion, pick.Tier)).SequenceEqual([("Jinx", "S"), ("Caitlyn", "A"), ("Brand", "B")]) &&
            damage.Select(pick => pick.Reason).SequenceEqual(["Básicos a distancia", "Básicos a distancia", "Daño mágico a distancia"]),
            "cada sugerencia muestra su aporte principal y el límite de tres prioriza los tiers publicados");
        var previewTiers = tiers with { Tiers = new() { ["tryndamere"] = 3, ["diana"] = 3, ["aurelionsol"] = 4 },
            Ranks = new() { ["tryndamere"] = 34, ["diana"] = 54 } };
        var preview = advisor.Recommend(previewTiers, "Alistar", ["Leona", "Nautilus", "Lux", "Jayce"],
            ["AurelionSol", "Diana", "Tryndamere"]);
        Check(preview.SequenceEqual([
            new("Tryndamere", "B", "Daño físico con básicos"),
            new("Diana", "B", "Daño mágico en área de cerca"),
            new("Aurelion Sol", "C", "Daño mágico a distancia")
        ]), "la misma necesidad se explica según la forma de aportar de cada campeón");
        string[] squishyAllies = ["Lux", "Talon", "Katarina", "Fizz"];
        Check(advisor.Recommend(previewTiers, "Alistar", squishyAllies, ["AurelionSol", "Diana"]) is
            [{ Tradeoff: null }, { Tradeoff: "El equipo pierde primera línea" }] &&
            advisor.Recommend(previewTiers, "Alistar", squishyAllies.Take(3), ["AurelionSol"]).Single().Tradeoff is null,
            "la pérdida se muestra solo si el cambio deja la función sin cubrir en un equipo confirmado");
        var sameTier = tiers with { Tiers = new(tiers.Tiers) { ["jinx"] = 2 }, Ranks = new() { ["jinx"] = 1, ["caitlyn"] = 2 } };
        Check(advisor.Recommend(sameTier, null, tanks, ["Jinx", "Caitlyn", "Ashe"])
            .Select(pick => (pick.Champion, pick.Tier)).SequenceEqual([("Caitlyn", "A"), ("Jinx", "A"), ("Ashe", null)]),
            "la afinidad desempata el tier y las opciones sin tier aparecen al final");
        Check(advisor.Recommend(tiers with { SourcePatch = "12.1" }, null, tanks, ["Jinx"]).Single().Tier is null &&
            advisor.Recommend(null, null, tanks, ["Caitlyn"]).Single() is { Champion: "Caitlyn", Tier: null } &&
            advisor.Recommend(tiers, null, ["Desconocido"], choices).Length == 0 &&
            advisor.Recommend(tiers, null, tanks, []).Length == 0,
            "la afinidad local sigue disponible sin tier y la falta de opciones o aliados confirmados evita suposiciones");

        var balanced = advisor.Evaluate("Jinx", ["Ornn", "Brand", "Lulu", "Caitlyn"]);
        var exposed = advisor.Evaluate("Jinx", ["Sivir", "Tristana", "Vayne", "Aphelios"]);
        var partial = advisor.Evaluate("Jinx", ["Ornn", "Brand", "ornn", "Desconocido"]);
        Check(balanced is { Tier: "S", Members: 5, TeamSize: 5, Reason: "Primera línea protege el daño sostenido" } &&
            exposed is { Tier: "C", Members: 5, Reason: "Falta primera línea" } &&
            partial is { Tier: "A", Members: 3, TeamSize: 5 } &&
            advisor.Evaluate(null, ["Brand", "Ornn"]) is { Members: 2, TeamSize: 5 } &&
            advisor.Evaluate("Jinx", ["JINX", "Desconocido"]) is null,
            "la nota de equipo usa cinco plazas y el progreso solo cuenta campeones distintos y reconocidos");
        Console.WriteLine("Composición de ARAM Caos verificada con equipos parciales y completos y alternativas disponibles.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}

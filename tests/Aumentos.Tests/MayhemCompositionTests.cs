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
            frontline.All(pick => pick.Reason == "Aporta primera línea"),
            "solo se sugieren campeones disponibles y se excluyen el propio, los aliados y los bloqueos");
        var allowed = advisor.Recommend(tiers, "Milio", backline, choices);
        Check(allowed.Select(pick => pick.Champion).SequenceEqual(["Rell", "Sejuani", "Thresh"]),
            "OP.GG desempata campeones con el mismo aporte a la composición");
        var ranked = tiers with
        {
            Tiers = new(tiers.Tiers) { ["sejuani"] = 1 },
            Ranks = new() { ["sejuani"] = 3, ["rell"] = 5 }
        };
        Check(advisor.Recommend(ranked, "Milio", backline, choices)[0].Champion == "Sejuani",
            "a igual afinidad y tier se usa el puesto de OP.GG cuando está publicado");

        var tanks = new[] { "Alistar", "Rell", "Nautilus" };
        var damage = advisor.Recommend(tiers, null, tanks, ["Braum", "Jinx", "Brand", "Caitlyn"]);
        Check(damage.Select(pick => pick.Champion).SequenceEqual(["Caitlyn", "Brand", "Jinx"]) &&
            damage.Take(2).All(pick => pick.Reason == "Aporta daño sostenido y presión a distancia"),
            "el daño sostenido y la presión a distancia pesan más que el tier general para una línea de tanques");
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

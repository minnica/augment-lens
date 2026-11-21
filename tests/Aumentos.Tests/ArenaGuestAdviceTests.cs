using Aumentos;

internal static class ArenaGuestAdviceTests
{
    internal static void Run(Ratings ratings)
    {
        var ahri = new LiveMatch(new(Mode.Arena, "Ahri"), Arena: new([], []));
        ArenaParticipant[] engage = [new("Jax", []), new("Maokai", [])];
        ArenaParticipant[] protect = [new("Janna", []), new("Yuumi", [])];
        Check(Rate("Yone", ahri, engage).Tier == Tier.S &&
            Rate("Yone", ahri, protect).Tier == Tier.B &&
            Rate("Yone", ahri).Tier == Tier.B,
            "los campeones del equipo cambian el potencial de derrotar a los demonios");

        var jax = ahri with { Game = new(Mode.Arena, "Jax") };
        ArenaParticipant[] allies = [new("Maokai", []), new("Janna", [])];
        Check(Rate("Evelynn", jax, allies).Score > Rate("Evelynn", ahri, allies).Score &&
            Rate("Evelynn", ahri, allies, health: 36) is { Tier: Tier.S, Reason: "Tu equipo sube a 75 de vida" } &&
            Rate("Evelynn", ahri, allies, health: 100).Tier == Tier.B,
            "Evelynn valora las sinergias del campeón y el efecto comprobado sobre la vida del equipo");

        var outgeared = ahri with
        {
            Arena = new([443055],
                [new("Swain", [443054, 223089]), new("Nunu", [443055, 223084]), new("Braum", [443055, 223065])],
                Allies: [new("Jax", [443054]), new("Maokai", [443054])])
        };
        Check(Rate("TahmKench", outgeared).Tier == Tier.S &&
            Rate("TahmKench", outgeared with { Arena = outgeared.Arena! with { InventoryKnown = false } }).Tier != Tier.S &&
            Rate("TahmKench", outgeared with { Arena = outgeared.Arena! with { Items = [443055, 223084] } }).Tier == Tier.B &&
            Rate("TahmKench", outgeared with { Game = new(Mode.Arena, "DrMundo") }).Score < 5,
            "el desfase confirmado en objetos favorece los tratos salvo cuando la vida máxima sostiene la build");

        Check(Rate("Garen", ahri, health: 18, round: 8).Tier == Tier.S &&
            Rate("Garen", ahri, health: 80, round: 8).Tier == Tier.C &&
            Rate("Garen", ahri, round: 8).Tier == Tier.B,
            "la recuperación de vida depende de la lectura del marcador y queda neutra cuando falta");
        Check(Rate("Vayne", outgeared).Tier == Tier.S && Rate("Vayne", ahri).Tier == Tier.B,
            "las herramientas de Vayne cobran valor contra rivales confirmados de aguante o curación");
        Check(ArenaGuestAdvice.Evaluate(ratings, ["Yone"], ahri with { Game = new(Mode.Caos, "Ahri") })[0] is null &&
            ArenaGuestAdvice.Evaluate(ratings, ["Yone"], ahri with { Game = new(Mode.Arena, "Desconocido") })[0] is null &&
            ArenaGuestAdvice.Evaluate(ratings, ["Desconocido"], ahri)[0] is null,
            "la valoración requiere un invitado reconocido y un campeón de una partida de Arena");

        Console.WriteLine("Invitados de Arena verificados con campeón, equipo, vida, inventario y rivales.");

        ArenaGuestRating Rate(string guest, LiveMatch match, ArenaParticipant[]? team = null, int? health = null, int? round = null)
        {
            if (team is not null && match.Arena is { } arena)
                match = match with { Arena = arena with { Allies = team } };
            return ArenaGuestAdvice.Evaluate(ratings, [guest], match, health, round)[0] ??
                throw new InvalidOperationException($"Falta la valoración de {guest}.");
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}

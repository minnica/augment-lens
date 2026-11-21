using Aumentos;

internal static class ArenaVoteDraftTests
{
    internal static void Run()
    {
        ArenaParticipant[] allies = [new("Jax", [223124]), new("Ornn", [223084])];
        var opponents = Enumerable.Range(0, 15).Select(i => new ArenaParticipant($"Rival{i}", [])).ToArray();
        ArenaParticipant[] roster = [new("Ahri", []), .. allies, .. opponents];
        var arena = new ArenaMatch([], [], Participants: roster);
        var match = new LiveMatch(new GameContext(Mode.Arena, "Ahri"), Arena: arena, GameTime: 150);
        var draft = new ArenaVoteDraft("Ahri", new(["Jax", "Ornn"], [], 3), 120000);
        var vote = draft.Apply(match, 300000);
        Check(vote.Arena?.Allies?.SequenceEqual(allies) == true &&
            vote.Arena.LobbyOpponents?.SequenceEqual(opponents) == true && vote.Arena.Opponents.Length == 0,
            "un equipo fijado permite recuperar inventarios y rivales del lobby antes del primer combate registrado");
        var differentGame = match with { GameTime = 190 };
        Check(draft.Apply(match, 500000) == match && draft.Apply(differentGame, 300000) == differentGame,
            "se descarta una selección de otra partida");
        var conflicting = match with { Arena = arena with { Opponents = [allies[0]] } };
        Check(draft.Apply(conflicting, 300000) == conflicting,
            "un rival confirmado por el combate invalida la coincidencia de la selección");
        var ambiguous = match with { Arena = arena with { Participants = [.. roster.SkipLast(1), allies[0]] } };
        Check(draft.Apply(ambiguous, 300000) == ambiguous,
            "se conserva el contexto del cliente cuando hay un campeón ambiguo");

        Console.WriteLine("Composición e inventarios para la votación de Arena verificados.");
    }

    private static void Check(bool condition, string description)
    {
        if (!condition)
            throw new InvalidOperationException($"Falló: {description}.");
    }
}

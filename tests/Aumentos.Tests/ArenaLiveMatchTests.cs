using System.Text.Json;
using Aumentos;

internal static class ArenaLiveMatchTests
{
    internal static void Run()
    {
        const string players = """
            [
              {"riotId":"Self#001","riotIdGameName":"Self","team":"ORDER","rawChampionName":"game_character_displayname_Chogath","items":[{"itemID":223084},{"itemID":443080},{"itemID":"a"},null]},
              {"riotId":"Ally#002","riotIdGameName":"Ally","team":"ORDER","rawChampionName":"game_character_displayname_Brand","items":[]},
              {"riotId":"Earlier#003","riotIdGameName":"Earlier","team":"ORDER","rawChampionName":"game_character_displayname_Ashe","items":[{"itemID":223031}]},
              {"riotId":"Latest#004","riotIdGameName":"Latest","team":"ORDER","rawChampionName":"game_character_displayname_Ornn","items":[{"itemID":223065}]},
              {"riotId":"LatestMate#005","riotIdGameName":"LatestMate","team":"ORDER","rawChampionName":"game_character_displayname_Jinx","items":[{"itemID":223036}]},
              {"riotId":"Unseen#006","riotIdGameName":"Unseen","team":"ORDER","rawChampionName":"game_character_displayname_Lux","items":[]},
              {"riotId":"Shared#007","riotIdGameName":"Shared","team":"ORDER","rawChampionName":"game_character_displayname_Lulu","items":[]},
              {"riotId":"Shared#008","riotIdGameName":"Shared","team":"ORDER","rawChampionName":"game_character_displayname_Zed","items":[]}
            ]
            """;
        const string events = """
              {"EventName":"GameStart","EventTime":0},
              {"EventName":"ChampionKill","EventTime":220,"KillerName":"Latest","VictimName":"Ally","Assisters":["LatestMate"]},
              {"EventName":"ChampionKill","EventTime":110,"KillerName":"Self","VictimName":"Earlier","Assisters":["Ally"]},
              {"EventName":"ChampionKill","EventTime":230,"KillerName":"Earlier","VictimName":"Unseen","Assisters":[]},
              {"EventName":"ChampionKill","EventTime":270,"KillerName":"Shared","VictimName":"Self","Assisters":[]},
              {"EventName":"ChampionKill","KillerName":"Unknown","VictimName":"Self","Assisters":["Unseen"]}
            """;
        var match = Parse(players, $"[{events}]");
        Check(match?.Game == new GameContext(Mode.Arena, "Chogath") && match.Mayhem is null &&
            match.Arena?.Items.SequenceEqual([223084, 443080]) == true &&
            match.Arena.Opponents.Select(opponent => opponent.Champion).SequenceEqual(["Ornn", "Jinx", "Ashe"]) &&
            match.Arena.Opponents.SelectMany(opponent => opponent.Items).SequenceEqual([223065, 223036, 223031]),
            "Arena une aliados por asistencias y ordena solo los rivales confirmados por encuentro reciente");
        Check(match?.Arena?.Roster is { Length: 8 } roster && roster.Select(player => player.Champion)
                .SequenceEqual(["Chogath", "Brand", "Ashe", "Ornn", "Jinx", "Lux", "Lulu", "Zed"]) &&
            roster[0].Items.SequenceEqual([223084, 443080]) && roster[2].Items.SequenceEqual([223031]),
            "la lobby conserva al jugador activo, sus aliados y rivales que todavía no ha enfrentado");

        var latest = Parse(players, $$"""
            [{{events}}, {"EventName":"ChampionKill","EventTime":280,"KillerName":"Self","VictimName":"Unseen"}]
            """);
        Check(latest?.Arena?.Opponents.Select(opponent => opponent.Champion).SequenceEqual(["Lux", "Ornn", "Jinx", "Ashe"]) == true,
            "Arena conserva rivales anteriores y actualiza el orden tras un nuevo encuentro confirmado");

        var beforeCombat = Parse(players, "[]");
        using var playerList = JsonDocument.Parse(players);
        var fallback = LiveGame.ParseMatch(Mode.Arena, playerList.RootElement, "Self#001");
        Check(beforeCombat?.Arena is { Items.Length: 2, Opponents.Length: 0, Roster.Length: 8 } &&
            fallback?.Arena is { Opponents.Length: 0, Roster.Length: 8 } &&
            Parse(players, "[]", mode: "KIWI") is null && Parse(players, "[]", active: "Shared") is null,
            "la lobby llega antes de los combates y también por la API de respaldo");
        Console.WriteLine("Lectura de Arena verificada con inventario y rivales confirmados por eventos de combate.");
    }

    private static LiveMatch? Parse(string players, string events, string mode = "CHERRY", string active = "Self#001")
    {
        using var snapshot = JsonDocument.Parse($$$"""
            {"gameData":{"gameMode":"{{{mode}}}"},"activePlayer":{"riotId":"{{{active}}}"},
             "allPlayers":{{{players}}},"events":{"Events":{{{events}}}
            }}
            """);
        return LiveGame.ParseArenaMatch(snapshot.RootElement);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

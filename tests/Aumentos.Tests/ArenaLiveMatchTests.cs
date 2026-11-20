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
            match.Arena.Allies is [{ Champion: "Brand" }] &&
            match.Arena.Opponents.Select(opponent => opponent.Champion).SequenceEqual(["Ornn", "Jinx", "Ashe"]) &&
            match.Arena.Opponents.SelectMany(opponent => opponent.Items).SequenceEqual([223065, 223036, 223031]),
            "Arena une aliados por asistencias y ordena solo los rivales confirmados por encuentro reciente");
        Check(match?.Arena?.LobbyOpponents is { Length: 6 } roster && roster.Select(player => player.Champion)
                .SequenceEqual(["Ashe", "Ornn", "Jinx", "Lux", "Lulu", "Zed"]) &&
            roster[0].Items.SequenceEqual([223031]),
            "un dúo confirmado excluye al jugador activo y su aliado, y conserva rivales de otras rondas");

        var latest = Parse(players, $$"""
            [{{events}}, {"EventName":"ChampionKill","EventTime":280,"KillerName":"Self","VictimName":"Unseen"}]
            """);
        Check(latest?.Arena?.Opponents.Select(opponent => opponent.Champion).SequenceEqual(["Lux", "Ornn", "Jinx", "Ashe"]) == true,
            "Arena conserva rivales anteriores y actualiza el orden tras un nuevo encuentro confirmado");

        var beforeCombat = Parse(players, "[]");
        using var playerList = JsonDocument.Parse(players);
        var fallback = LiveGame.ParseMatch(Mode.Arena, playerList.RootElement, "Self#001");
        Check(beforeCombat?.Arena is { Items.Length: 2, Opponents.Length: 0, LobbyOpponents.Length: 0 } &&
            fallback?.Arena is { Opponents.Length: 0, LobbyOpponents: null } &&
            Parse(players, "[]", mode: "KIWI") is null && Parse(players, "[]", active: "Shared") is null,
            "sin eventos de combate, todavía no hay rivales identificados en la API de Arena");

        var trio = players.TrimEnd()[..^1] + """
            ,{"riotId":"Third","rawChampionName":"game_character_displayname_Janna"},
             {"riotId":"Rival1","rawChampionName":"game_character_displayname_Vayne"},
             {"riotId":"Rival2","rawChampionName":"game_character_displayname_Tristana"},
             {"riotId":"Rival3","rawChampionName":"game_character_displayname_Chogath"},
             {"riotId":"Rival4","rawChampionName":"game_character_displayname_Aatrox"},
             {"riotId":"Rival5","rawChampionName":"game_character_displayname_Xayah"},
             {"riotId":"Rival6","rawChampionName":"game_character_displayname_Rakan"},
             {"riotId":"Rival7","rawChampionName":"game_character_displayname_Kennen"},
             {"riotId":"Rival8","rawChampionName":"game_character_displayname_Varus"},
             {"riotId":"Rival9","rawChampionName":"game_character_displayname_Nautilus"}]
            """;
        const string otherRound = """
            {"EventName":"ChampionKill","EventTime":230,"KillerName":"Rival1","VictimName":"Rival4","Assisters":["Rival2","Rival3"]}
            """;
        var partialTrio = Parse(trio, $"[{events}, {otherRound}]")?.Arena;
        Check(partialTrio?.LobbyOpponents?.Select(player => player.Champion).SequenceEqual(
                ["Ashe", "Ornn", "Jinx", "Vayne", "Tristana", "Chogath"]) == true,
            "en 3×6, solo entran encuentros propios confirmados y escuadras ajenas completas mientras falta identificar un aliado");
        var fullTrio = Parse(trio, $$"""
            [{{events}}, {{otherRound}},
             {"EventName":"ChampionKill","EventTime":280,"KillerName":"Self","VictimName":"Rival4","Assisters":["Third"]}]
            """)?.Arena;
        Check(fullTrio?.LobbyOpponents is { Length: 15 } remaining &&
            remaining.Count(player => player.Champion == "Chogath") == 1 &&
            remaining.All(player => player.Champion is not ("Janna" or "Brand")),
            "al identificar los tres aliados, el lobby excluye sus celdas y conserva un rival con el mismo campeón");
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

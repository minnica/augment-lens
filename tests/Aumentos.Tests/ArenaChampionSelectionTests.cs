using System.Text.Json;
using Aumentos;

internal static class ArenaChampionSelectionTests
{
    internal static void Run()
    {
        using var intended = JsonDocument.Parse("""
            {"localPlayerCellId":4,"myTeam":[{"cellId":1,"championId":164,"championPickIntent":1},
                {"cellId":4,"championId":0,"championPickIntent":902}]}
            """);
        using var locked = JsonDocument.Parse("""
            {"localPlayerCellId":4,"myTeam":[{"cellId":4,"championId":902,"championPickIntent":164}]}
            """);
        using var random = JsonDocument.Parse("""
            {"localPlayerCellId":4,"myTeam":[{"cellId":1,"championId":164},
                {"cellId":4,"championId":0,"championPickIntent":-1}]}
            """);
        using var absent = JsonDocument.Parse("""
            {"localPlayerCellId":4,"myTeam":[{"cellId":1,"championId":164,"championPickIntent":902}]}
            """);

        var intendedPick = ChampionSelectClient.ArenaDraftIds(intended.RootElement);
        var lockedPick = ChampionSelectClient.ArenaDraftIds(locked.RootElement);
        if (intendedPick.Champion != 902 || intendedPick.ChampionConfirmed ||
            lockedPick.Champion != 902 || !lockedPick.ChampionConfirmed ||
            ChampionSelectClient.ArenaDraftIds(random.RootElement).Champion is not null ||
            ChampionSelectClient.ArenaDraftIds(absent.RootElement).Champion is not null ||
            ChampionSelectClient.OwnPick(intended.RootElement) is not null)
            throw new InvalidOperationException("Falló la preselección o elección de Arena en la celda local.");

        using var teamDraft = JsonDocument.Parse("""
            {"localPlayerCellId":4,
             "myTeam":[{"cellId":4,"team":3,"championId":902},
               {"cellId":5,"team":3,"championId":86,"championPickIntent":122},
               {"cellId":6,"team":3,"championId":0,"championPickIntent":61},
               {"cellId":7,"team":4,"championId":99,"championPickIntent":16}],
             "theirTeam":[{"cellId":8,"team":5,"championId":103},
               {"cellId":9,"team":5,"championId":0,"championPickIntent":17}],
             "bans":{"myTeamBans":[1,1,0,-1],"theirTeamBans":[2]},
             "actions":[[{"actorCellId":5,"championId":86,"completed":true,"type":"pick"},
               {"actorCellId":9,"championId":11,"completed":true,"type":"pick"},
               {"actorCellId":10,"championId":12,"completed":true,"type":"ban"},
               {"actorCellId":8,"championId":19,"completed":false,"type":"pick"},
               {"actorCellId":10,"championId":20,"completed":false,"type":"ban"}]]}
            """);
        var (self, allies, unavailable, teamSize, confirmed, confirmedTeammates) =
            ChampionSelectClient.ArenaDraftIds(teamDraft.RootElement);
        if (self != 902 || !confirmed || !allies.SequenceEqual([86, 61]) || !confirmedTeammates.SequenceEqual([86]) ||
            unavailable.Length != 6 ||
            !unavailable.ToHashSet().SetEquals([1, 2, 11, 12, 99, 103]) || teamSize != 3)
            throw new InvalidOperationException("Falló el alcance de equipo o las exclusiones confirmadas en Arena.");
        var final = ChampionSelectClient.ArenaDraftIds(teamDraft.RootElement, confirmedOnly: true);
        if (final.Champion != 902 || !final.ChampionConfirmed || final.TeamSize != 3 ||
            !final.Teammates.SequenceEqual([86]) || !final.ConfirmedTeammates.SequenceEqual([86]))
            throw new InvalidOperationException("La composición fijada para la votación omite las intenciones de campeón.");

        using var hovering = JsonDocument.Parse("""
            {"localPlayerCellId":4,
             "myTeam":[{"cellId":4,"championId":902,"championPickIntent":902},
               {"cellId":5,"championId":0},{"cellId":6,"championId":0}],
             "theirTeam":[{"cellId":8,"championId":0}],
             "actions":[[{"actorCellId":4,"championId":96,"completed":false,"isInProgress":true,"type":"pick"},
               {"actorCellId":5,"championId":40,"completed":false,"isInProgress":true,"type":"pick"},
               {"actorCellId":6,"championId":31,"completed":false,"isInProgress":false,"type":"pick"},
               {"actorCellId":6,"championId":32,"completed":false,"isInProgress":true,"type":"ban"},
               {"actorCellId":8,"championId":24,"completed":false,"isInProgress":true,"type":"pick"}]]}
            """);
        var partial = ChampionSelectClient.ArenaDraftIds(hovering.RootElement);
        if (partial.Champion != 96 || partial.ChampionConfirmed || !partial.Teammates.SequenceEqual([40]) ||
            partial.ConfirmedTeammates.Length != 0 || partial.TeamSize != 3 ||
            partial.Unavailable.Length != 0)
            throw new InvalidOperationException("La preselección activa valora solo el equipo propio y conserva la plaza pendiente.");

        using var allyLocked = JsonDocument.Parse(hovering.RootElement.GetRawText().Replace(
            "\"actorCellId\":5,\"championId\":40,\"completed\":false",
            "\"actorCellId\":5,\"championId\":40,\"completed\":true", StringComparison.Ordinal));
        var interim = ChampionSelectClient.ArenaDraftIds(allyLocked.RootElement);
        if (!interim.Teammates.SequenceEqual(partial.Teammates) || !interim.ConfirmedTeammates.SequenceEqual([40]))
            throw new InvalidOperationException("La acción completada confirma la elección aunque el campeón del aliado tarde en sincronizarse.");

        Console.WriteLine("Preselección, aliados y bloqueos de Arena verificados sin cambiar los hechizos de ARAM Caos.");
    }
}

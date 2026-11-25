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
        var (self, allies, unavailable, teamSize, confirmed, confirmedTeammates, allySlots) =
            ChampionSelectClient.ArenaDraftIds(teamDraft.RootElement);
        if (self != 902 || !confirmed || !allies.SequenceEqual([86, 61]) || !confirmedTeammates.SequenceEqual([86]) ||
            !allySlots.SequenceEqual(new int?[] { 86, 61 }) || unavailable.Length != 6 ||
            !unavailable.ToHashSet().SetEquals([1, 2, 11, 12, 99, 103]) || teamSize != 3)
            throw new InvalidOperationException("Falló el alcance de equipo o las exclusiones confirmadas en Arena.");
        var final = ChampionSelectClient.ArenaDraftIds(teamDraft.RootElement, confirmedOnly: true);
        if (final.Champion != 902 || !final.ChampionConfirmed || final.TeamSize != 3 ||
            !final.Teammates.SequenceEqual([86]) || !final.ConfirmedTeammates.SequenceEqual([86]) ||
            !final.AllySlots.SequenceEqual(new int?[] { 86, null }))
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

        using var secondSlot = JsonDocument.Parse("""
            {"localPlayerCellId":4,"myTeam":[{"cellId":6,"championId":61},{"cellId":4,"championId":0},{"cellId":5,"championId":0}]}
            """);
        var stable = ChampionSelectClient.ArenaDraftIds(secondSlot.RootElement);
        if (!stable.AllySlots.SequenceEqual(new int?[] { null, 61 }))
            throw new InvalidOperationException("El aliado que fija primero su campeón conserva su plaza aunque quede otra vacía.");

        // Reproduce the client's Arena 3×6 layout with Zyra in cell 13, between Olaf and Dr. Mundo.
        using var trios = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            queueId = 1750,
            localPlayerCellId = 13,
            myTeam = Enumerable.Range(0, 18).Select(cell => new
            {
                cellId = cell, team = 1, championId = cell switch { 12 => 2, 13 => 143, 14 => 36, _ => 0 }
            }).Reverse(),
            theirTeam = Array.Empty<object>(),
            actions = new[] { new[] { new { actorCellId = 15, type = "pick", championId = 115, completed = true } } },
            bans = new { theirTeamBans = new[] { 22 } }
        }));
        var triosFinal = ChampionSelectClient.ArenaDraftIds(trios.RootElement, confirmedOnly: true);
        if (triosFinal.Champion != 143 || !triosFinal.ChampionConfirmed || triosFinal.TeamSize != 3 ||
            !triosFinal.Teammates.ToHashSet().SetEquals([2, 36]) ||
            !triosFinal.ConfirmedTeammates.ToHashSet().SetEquals([2, 36]) ||
            !triosFinal.AllySlots.SequenceEqual(new int?[] { 2, 36 }) ||
            !triosFinal.Unavailable.ToHashSet().SetEquals([22, 115]))
            throw new InvalidOperationException("Arena 3×6 debe leer el trío local y excluir las elecciones de los otros cinco equipos.");

        using var triosPartial = JsonDocument.Parse(trios.RootElement.GetRawText().Replace(
            "\"cellId\":12,\"team\":1,\"championId\":2", "\"cellId\":12,\"team\":1,\"championId\":0", StringComparison.Ordinal));
        var openTrio = ChampionSelectClient.ArenaDraftIds(triosPartial.RootElement);
        if (openTrio.TeamSize != 3 || !openTrio.ConfirmedTeammates.SequenceEqual([36]) ||
            !openTrio.AllySlots.SequenceEqual(new int?[] { null, 36 }))
            throw new InvalidOperationException("Arena 3×6 conserva la plaza propia pendiente y asigna a Dr. Mundo su plaza real.");

        Console.WriteLine("Preselección, aliados y bloqueos de Arena verificados sin cambiar los hechizos de ARAM Caos.");
    }
}

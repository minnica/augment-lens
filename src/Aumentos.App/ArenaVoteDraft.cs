using System;
using System.Linq;

namespace Aumentos;

internal sealed record ArenaVoteDraft(string Champion, ArenaDraftTeam Team, long ObservedAt)
{
    internal LiveMatch Apply(LiveMatch match, long now)
    {
        if (match.Game.Mode != Mode.Arena || !SameChampion(match.Game.Champion, Champion) ||
            match.GameTime is not { } seconds || match.Arena is not { Participants: { } players } arena)
            return match;

        // Tie the completed draft to this game's start even when voting occurs in a later round.
        double gap = now - seconds * 1000 - ObservedAt;
        int teamSize = players.Length switch { 18 => 3, 8 or 16 => 2, _ => 0 };
        if (gap is < -5000 or > 180000 || Team.Size != teamSize || Team.Teammates.Length != teamSize - 1 ||
            players.Count(player => SameChampion(player.Champion, Champion)) != 1)
            return match;

        var allies = players.Where(player => IsDraftAlly(player.Champion)).ToArray();
        if (allies.Length != Team.Teammates.Length ||
            allies.Select(ally => ally.Champion).Distinct(StringComparer.OrdinalIgnoreCase).Count() != allies.Length ||
            (arena.Allies ?? []).Any(ally => !IsDraftAlly(ally.Champion)) ||
            arena.Opponents.Any(rival => IsDraftAlly(rival.Champion)))
            return match;

        var lobby = players.Where(player => !IsDraftAlly(player.Champion) && !SameChampion(player.Champion, Champion)).ToArray();
        return match with { Arena = arena with { Allies = allies, LobbyOpponents = lobby } };
    }

    private bool IsDraftAlly(string key) => Team.Teammates.Contains(key, StringComparer.OrdinalIgnoreCase);
    private static bool SameChampion(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}

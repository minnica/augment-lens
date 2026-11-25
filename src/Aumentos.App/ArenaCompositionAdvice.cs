using System;
using System.Collections.Generic;
using System.Linq;

namespace Aumentos;

internal sealed record ArenaCompositionMember(string Champion, bool Confirmed);
internal sealed record ArenaComposition(string Tier, string Reason, ArenaCompositionMember[] Members);
internal sealed record ArenaCompositionGrade(string Tier, string Reason, int Members, int TeamSize, ArenaTeamResult? Observed = null);

internal static class ArenaCompositionAdvice
{
    private const int RecommendationLimit = 4;
    private const string TierOrder = "SABCD";

    [Flags]
    private enum Coverage
    {
        None = 0,
        Frontline = 1,
        SustainedDamage = 4,
        Control = 8,
        Protection = 16,
        PhysicalDamage = 32,
        MagicDamage = 64,
        Damage = PhysicalDamage | MagicDamage,
        FollowUpDamage = 128,
        HoldGround = 256,
        CoverDamage = HoldGround | Protection
    }

    private sealed record Candidate(Champion Champion, Coverage Profile, decimal Popularity);
    private readonly record struct LineupOption(Candidate First, Candidate? Second, int Score,
        bool AddressesNeed, decimal Popularity);

    internal static ArenaCompositionGrade? Evaluate(Ratings ratings, string? selected, ArenaDraftTeam? team, ArenaTeamResult? observed = null)
    {
        if (selected is null || team is null)
            return null;

        var champions = ratings.Catalog.Champions;
        var own = champions.FirstOrDefault(champion => string.Equals(champion.Key, selected, StringComparison.OrdinalIgnoreCase));
        if (own is null)
            return null;

        var teammates = team.Teammates.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var profiles = champions.Where(champion => teammates.Contains(champion.Key) && champion != own)
            .Prepend(own).Select(champion => CombatProfile(ratings, champion)).ToArray();
        if (profiles.Length < 2)
            return null;

        var (score, primary) = Assess(profiles);
        string tier = TierForScore(score);
        int teamSize = Math.Max(team.Size, profiles.Length);
        string reason = primary != Coverage.None ? $"Falta {CategoryName(primary).ToLowerInvariant()}" :
            profiles.Length == teamSize ? "Equipo equilibrado" : "Sin carencias claras";
        var result = team.Size == 3 && profiles.Length == 3 && observed is { Games: >= ArenaTeamSource.MinimumGames } &&
            observed.Patch == ratings.Catalog.Patch ? observed : null;
        return new(result?.Tier ?? tier, reason, profiles.Length, teamSize, result);
    }

    internal static string[] ConfirmedPicks(string? selected, ArenaDraftTeam? team)
    {
        if (team is null)
            return [];

        return (team.OwnConfirmed == true && selected is not null ? new[] { selected } : [])
            .Concat(team.ConfirmedTeammates ?? []).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    internal static ArenaComposition[] Recommend(Ratings ratings, ArenaChampionTierList? tiers,
        string[] confirmed, string[] unavailable, int teamSize)
    {
        if (confirmed.Length == 0 || confirmed.Length >= teamSize)
            return [];

        var champions = ratings.Catalog.Champions.ToDictionary(champion => champion.Key, StringComparer.OrdinalIgnoreCase);
        var locked = confirmed.Select(key => champions.GetValueOrDefault(key)).OfType<Champion>().ToArray();
        if (locked.Length != confirmed.Length)
            return [];

        var excluded = unavailable.Concat(confirmed).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var statistics = tiers?.ClientPatch == ratings.Catalog.Patch ? tiers.Champions : null;
        var candidates = champions.Values.Where(champion => !excluded.Contains(champion.Key)).Select(champion =>
            new Candidate(champion, CombatProfile(ratings, champion),
                statistics?.GetValueOrDefault(champion.Key)?.TopThreePercent ?? 0)).ToArray();
        var lineup = new Coverage[teamSize];
        for (int i = 0; i < locked.Length; i++)
            lineup[i] = CombatProfile(ratings, locked[i]);

        var prior = Assess(lineup.AsSpan(0, locked.Length));
        var covered = lineup.Aggregate(Coverage.None, (total, profile) => total | profile);
        int firstOpen = locked.Length;
        bool twoOpen = teamSize - firstOpen == 2;
        var options = new List<LineupOption>();
        for (int first = 0; first < candidates.Length; first++)
        {
            lineup[firstOpen] = candidates[first].Profile;
            if (!twoOpen)
                AddOption(candidates[first], null);
            else
            {
                for (int second = first + 1; second < candidates.Length; second++)
                {
                    lineup[firstOpen + 1] = candidates[second].Profile;
                    AddOption(candidates[first], candidates[second]);
                }
            }
        }

        var shown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var suggestions = new List<ArenaComposition>(RecommendationLimit);
        var ranked = options.OrderByDescending(option => option.Score).ThenByDescending(option => option.AddressesNeed)
            .ThenByDescending(option => option.Popularity)
            .ThenBy(option => option.First.Champion.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var band in ranked.GroupBy(option => TierRank(option.Score)))
        {
            foreach (var option in band)
            {
                if (!shown.Contains(option.First.Champion.Key) &&
                    (option.Second is null || !shown.Contains(option.Second.Champion.Key)))
                    AddSuggestion(option);
                if (suggestions.Count == RecommendationLimit)
                    return suggestions.ToArray();
            }
            foreach (var option in band)
            {
                if (!shown.Contains(option.First.Champion.Key) ||
                    option.Second is { } partner && !shown.Contains(partner.Champion.Key))
                    AddSuggestion(option);
                if (suggestions.Count == RecommendationLimit)
                    return suggestions.ToArray();
            }
        }
        return suggestions.ToArray();

        void AddOption(Candidate first, Candidate? second)
        {
            var added = first.Profile | (second?.Profile ?? Coverage.None);
            int score = Assess(lineup).Score;
            options.Add(new(first, second, score, (added & prior.PrimaryNeed) != 0,
                first.Popularity + (second?.Popularity ?? 0)));
        }

        void AddSuggestion(LineupOption option)
        {
            Candidate[] additions = option.Second is { } second ? [option.First, second] : [option.First];
            foreach (var addition in additions)
                shown.Add(addition.Champion.Key);
            var members = locked.Select(champion => new ArenaCompositionMember(champion.Name, Confirmed: true))
                .Concat(additions.Select(addition => new ArenaCompositionMember(addition.Champion.Name, Confirmed: false))).ToArray();
            var added = additions.Aggregate(Coverage.None, (total, addition) => total | addition.Profile);
            suggestions.Add(new(TierForScore(option.Score), CompositionReason(covered, added, prior.PrimaryNeed), members));
        }
    }

    private static string CompositionReason(Coverage covered, Coverage added, Coverage primary)
    {
        Coverage[] contributions =
            [Coverage.Frontline, Coverage.SustainedDamage, Coverage.Control, Coverage.Protection, Coverage.MagicDamage,
             Coverage.PhysicalDamage, Coverage.FollowUpDamage, Coverage.HoldGround];
        var reason = (added & primary) != 0 ? primary : contributions.FirstOrDefault(role =>
            (added & role) != 0 && (covered & role) == 0);
        if (reason != Coverage.None)
            return "Aporta " + CategoryName(reason).ToLowerInvariant();
        if ((added & Coverage.Control) != 0 && (covered & Coverage.FollowUpDamage) != 0)
            return "Prepara el daño del equipo";
        if ((added & Coverage.FollowUpDamage) != 0 && (covered & Coverage.Control) != 0)
            return "Aprovecha el control aliado";
        return "Refuerza " + CategoryName(contributions.FirstOrDefault(role => (added & role) != 0)).ToLowerInvariant();
    }

    private static (int Score, Coverage PrimaryNeed) Assess(ReadOnlySpan<Coverage> profiles)
    {
        bool protectedDamage = HasPair(profiles, Coverage.SustainedDamage, Coverage.CoverDamage);
        bool damageAfterControl = HasPair(profiles, Coverage.Control, Coverage.FollowUpDamage);
        bool holdsSpace = HasPair(profiles, Coverage.HoldGround, Coverage.Control | Coverage.Protection);
        bool mixedDamage = HasPair(profiles, Coverage.PhysicalDamage, Coverage.MagicDamage);
        var combined = Coverage.None;
        int controlCount = 0;
        foreach (var profile in profiles)
        {
            combined |= profile;
            if ((profile & Coverage.Control) != 0)
                controlCount++;
        }

        bool chainControl = damageAfterControl && controlCount >= 2;
        bool sustainedDamage = (combined & Coverage.SustainedDamage) != 0;
        int score = (protectedDamage ? 3 : 0) + (damageAfterControl ? 2 : 0) + (chainControl ? 2 : 0) +
            (holdsSpace ? 2 : 0) + (sustainedDamage ? 1 : 0) + (mixedDamage ? 1 : 0);
        var primary = (sustainedDamage, protectedDamage, controlCount, damageAfterControl) switch
        {
            (false, _, _, _) => Coverage.SustainedDamage,
            (_, false, _, _) => Coverage.CoverDamage,
            (_, _, 0, _) => Coverage.Control,
            (_, _, _, false) => Coverage.FollowUpDamage,
            _ when score < 8 && (combined & Coverage.HoldGround) == 0 => Coverage.HoldGround,
            _ => Coverage.None
        };
        return (score, primary);
    }

    private static bool HasPair(ReadOnlySpan<Coverage> profiles, Coverage first, Coverage second)
    {
        for (int i = 0; i < profiles.Length; i++)
            for (int j = 0; j < profiles.Length; j++)
                if (i != j && (profiles[i] & first) != 0 && (profiles[j] & second) != 0)
                    return true;
        return false;
    }

    private static string TierForScore(int score) => TierOrder[TierRank(score)].ToString();

    private static int TierRank(int score) => score switch { >= 8 => 0, >= 6 => 1, >= 3 => 2, >= 1 => 3, _ => 4 };

    private static string CategoryName(Coverage need) => need switch
    {
        Coverage.Frontline => "Primera línea",
        Coverage.SustainedDamage => "Daño sostenido",
        Coverage.Control => "Control",
        Coverage.Protection or Coverage.CoverDamage => "Protección",
        Coverage.HoldGround => "Aguante",
        Coverage.MagicDamage => "Daño mágico",
        Coverage.PhysicalDamage => "Daño físico",
        Coverage.FollowUpDamage => "Daño de seguimiento",
        _ => "Presión ofensiva"
    };

    private static Coverage Profile(Ratings ratings, Champion champion)
    {
        var role = champion.Roles[0];
        var profile = Coverage.None;
        if (role is "Tank" or "Fighter")
            profile |= Coverage.Frontline;
        if (role == "Marksman" || ratings.HasTrait(champion.Key, "attackspeed") ||
            (role == "Fighter" && ratings.HasTrait(champion.Key, "attacks")))
            profile |= Coverage.SustainedDamage;
        if (ratings.HasTrait(champion.Key, "control"))
            profile |= Coverage.Control;
        if (ratings.HasTrait(champion.Key, "ally"))
            profile |= Coverage.Protection;

        if (role is "Mage" or "Marksman" or "Fighter" or "Assassin")
            profile |= EnemyDamageMix.PhysicalShare(ratings, champion, 0, 0) switch
            {
                0 => Coverage.MagicDamage,
                1 => Coverage.PhysicalDamage,
                _ => Coverage.None
            };
        return profile;
    }

    private static Coverage CombatProfile(Ratings ratings, Champion champion)
    {
        var profile = Profile(ratings, champion);
        var key = champion.Key;
        // Repeated spell damage can carry a round alongside basic-attack damage.
        if (key is "Anivia" or "AurelionSol" or "Azir" or "Brand" or "Cassiopeia" or "Heimerdinger" or
            "Karthus" or "Lillia" or "Malzahar" or "Rumble" or "Singed" or "Swain" or "Teemo" or "Viktor" or "Zyra")
            profile |= Coverage.SustainedDamage;
        if (profile.HasFlag(Coverage.SustainedDamage) || ratings.HasTrait(key, "burst") || champion.Roles[0] == "Fighter")
            profile |= Coverage.FollowUpDamage;
        if (ratings.HasTrait(key, "durability"))
            profile |= Coverage.HoldGround;
        if (ratings.HasTrait(key, "hybrid"))
            profile |= Coverage.Damage;
        return profile;
    }
}

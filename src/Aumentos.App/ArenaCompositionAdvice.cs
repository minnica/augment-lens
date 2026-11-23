using System;
using System.Collections.Generic;
using System.Linq;

namespace Aumentos;

internal sealed record ArenaCompositionOption(string Champion, string? Tier);
internal sealed record ArenaCompositionGroup(string Category, ArenaCompositionOption[] Options);
internal sealed record ArenaCompositionGrade(string Tier, string Reason, int Members, int TeamSize, ArenaTeamResult? Observed = null,
    ArenaCompositionAdvice.Coverage PrimaryNeed = ArenaCompositionAdvice.Coverage.None);

internal static class ArenaCompositionAdvice
{
    private const int CategoryLimit = 2;
    private const int OptionsPerCategory = 4;
    private const string TierOrder = "SABCD";

    [Flags]
    internal enum Coverage
    {
        None = 0,
        Frontline = 1,
        Tank = 2,
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

    private sealed record Need(Coverage Coverage, int Weight);

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

        // Arena teams need a way to convert control into a takedown or keep their damage alive as the ring closes.
        bool protectedDamage = HasPair(Coverage.SustainedDamage, Coverage.CoverDamage);
        bool damageAfterControl = HasPair(Coverage.Control, Coverage.FollowUpDamage);
        int controlCount = profiles.Count(profile => profile.HasFlag(Coverage.Control));
        bool chainControl = damageAfterControl && controlCount >= 2;
        bool holdsSpace = HasPair(Coverage.HoldGround, Coverage.Control | Coverage.Protection);
        bool sustainedDamage = profiles.Any(profile => profile.HasFlag(Coverage.SustainedDamage));
        bool mixedDamage = HasPair(Coverage.PhysicalDamage, Coverage.MagicDamage);

        int score = (protectedDamage ? 3 : 0) + (damageAfterControl ? 2 : 0) + (chainControl ? 2 : 0) +
            (holdsSpace ? 2 : 0) + (sustainedDamage ? 1 : 0) + (mixedDamage ? 1 : 0);
        string tier = score switch { >= 8 => "S", >= 6 => "A", >= 3 => "B", >= 1 => "C", _ => "D" };
        int teamSize = Math.Max(team.Size, profiles.Length);
        var primary = (sustainedDamage, protectedDamage, controlCount, damageAfterControl) switch
        {
            (false, _, _, _) => Coverage.SustainedDamage,
            (_, false, _, _) => Coverage.CoverDamage,
            (_, _, 0, _) => Coverage.Control,
            (_, _, _, false) => Coverage.FollowUpDamage,
            _ when tier != "S" && !profiles.Any(profile => profile.HasFlag(Coverage.HoldGround)) => Coverage.HoldGround,
            _ => Coverage.None
        };
        string reason = primary != Coverage.None ? $"Falta {CategoryName(primary).ToLowerInvariant()}" :
            profiles.Length == teamSize ? "Equipo equilibrado" : "Sin carencias claras";
        var result = team.Size == 3 && profiles.Length == 3 && observed is { Games: >= ArenaTeamSource.MinimumGames } &&
            observed.Patch == ratings.Catalog.Patch ? observed : null;
        return new(result?.Tier ?? tier, reason, profiles.Length, teamSize, result, primary);

        bool HasPair(Coverage first, Coverage second)
        {
            int partners = profiles.Count(profile => (profile & second) != 0);
            return profiles.Any(profile => (profile & first) != 0 && partners > ((profile & second) != 0 ? 1 : 0));
        }
    }

    internal static ArenaCompositionGroup[] Recommend(Ratings ratings, ArenaChampionTierList? tiers, ArenaDraftTeam? team,
        string? selected = null, ArenaCompositionGrade? grade = null)
    {
        if (team is null || team.Teammates.Length == 0)
            return [];

        var teammates = team.Teammates.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var champions = ratings.Catalog.Champions;
        var allies = champions.Where(champion => teammates.Contains(champion.Key)).ToArray();
        if (allies.Length == 0)
            return [];

        var allyCoverage = allies.Aggregate(Coverage.None, (combined, ally) => combined | CombatProfile(ratings, ally));
        var current = champions.FirstOrDefault(champion => string.Equals(champion.Key, selected, StringComparison.OrdinalIgnoreCase));
        var covered = current is null ? allyCoverage : allyCoverage | CombatProfile(ratings, current);
        var replacementNeeds = Priorities(allyCoverage).ToArray();
        var categories = Priorities(covered).OrderByDescending(need => need.Weight)
            .Concat(replacementNeeds.OrderByDescending(need => need.Weight))
            .Select(need => need.Coverage).Prepend(grade?.PrimaryNeed ?? Coverage.None)
            .Where(need => need != Coverage.None).DistinctBy(CategoryName);
        var unavailable = team.Unavailable.ToHashSet(StringComparer.OrdinalIgnoreCase);
        unavailable.UnionWith(teammates);
        if (selected is not null)
            unavailable.Add(selected);
        var statistics = tiers?.ClientPatch == ratings.Catalog.Patch ? tiers.Champions : null;
        var candidates = champions.Where(champion => !unavailable.Contains(champion.Key)).Select(champion =>
            {
                var profile = CombatProfile(ratings, champion);
                return (Champion: champion, Profile: profile, Rating: statistics?.GetValueOrDefault(champion.Key),
                    Score: replacementNeeds.Where(need => (need.Coverage & profile) != 0).Sum(need => need.Weight));
            })
            .OrderBy(option => TierRank(option.Rating?.Tier))
            .ThenByDescending(option => option.Score)
            .ThenByDescending(option => option.Rating?.TopThreePercent)
            .ThenBy(option => option.Champion.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var shown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var groups = new List<ArenaCompositionGroup>();
        foreach (var category in categories)
        {
            var options = candidates.Where(option => (option.Profile & category) != 0 && !shown.Contains(option.Champion.Key))
                .Take(OptionsPerCategory).ToArray();
            if (options.Length == 0)
                continue;
            shown.UnionWith(options.Select(option => option.Champion.Key));
            groups.Add(new(CategoryName(category), options.Select(option =>
                new ArenaCompositionOption(option.Champion.Name, option.Rating?.Tier)).ToArray()));
            if (groups.Count == CategoryLimit)
                break;
        }
        return groups.ToArray();
    }

    private static string CategoryName(Coverage need) => need switch
    {
        Coverage.Frontline or Coverage.Tank => "Primera línea",
        Coverage.SustainedDamage => "Daño sostenido",
        Coverage.Control => "Control",
        Coverage.Protection or Coverage.CoverDamage => "Protección",
        Coverage.HoldGround => "Aguante",
        Coverage.MagicDamage => "Daño mágico",
        Coverage.PhysicalDamage => "Daño físico",
        Coverage.FollowUpDamage => "Daño de seguimiento",
        _ => "Presión ofensiva"
    };

    private static IEnumerable<Need> Priorities(Coverage covered)
    {
        bool front = covered.HasFlag(Coverage.Frontline);
        bool sustained = covered.HasFlag(Coverage.SustainedDamage);
        bool control = covered.HasFlag(Coverage.Control);
        bool protection = covered.HasFlag(Coverage.Protection);
        var damage = covered & Coverage.Damage;

        if (!front)
        {
            yield return new(Coverage.Frontline, 6);
            yield return new(Coverage.Tank, 2);
        }
        if (!sustained)
            yield return new(Coverage.SustainedDamage, damage == Coverage.None ? 7 : 4);
        if (!control)
            yield return new(Coverage.Control, 3);
        if (!protection)
            yield return new(Coverage.Protection, sustained ? 4 : 2);

        if (damage == Coverage.PhysicalDamage)
            yield return new(Coverage.MagicDamage, 5);
        else if (damage == Coverage.MagicDamage)
            yield return new(Coverage.PhysicalDamage, 5);
        else if (damage == Coverage.None)
            yield return new(Coverage.Damage, 2);

        if (front && sustained && control && protection && damage == Coverage.Damage)
        {
            yield return new(Coverage.Control, 3);
            yield return new(Coverage.Protection, 2);
            yield return new(Coverage.Frontline, 2);
            yield return new(Coverage.SustainedDamage, 2);
        }
    }

    private static Coverage Profile(Ratings ratings, Champion champion)
    {
        var role = champion.Roles[0];
        var profile = Coverage.None;
        if (role == "Tank")
            profile |= Coverage.Frontline | Coverage.Tank;
        else if (role == "Fighter")
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

    private static int TierRank(string? tier) => tier is { Length: 1 } && TierOrder.IndexOf(tier[0]) is >= 0 and var rank
        ? rank : TierOrder.Length;
}

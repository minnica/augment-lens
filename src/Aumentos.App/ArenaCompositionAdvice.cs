using System;
using System.Collections.Generic;
using System.Linq;

namespace Aumentos;

internal sealed record ArenaCompositionPick(string Champion, string? Tier, string Reason);
internal sealed record ArenaCompositionGrade(string Tier, string Reason, int Members, int TeamSize, ArenaTeamResult? Observed = null);

internal static class ArenaCompositionAdvice
{
    private const int PickLimit = 3;
    private const string TierOrder = "SABCD";

    [Flags]
    private enum Coverage
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
        HoldGround = 256
    }

    private sealed record Need(Coverage Coverage, int Weight, string Reason);

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
        bool protectedDamage = HasPair(Coverage.SustainedDamage, Coverage.HoldGround | Coverage.Protection);
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
        string reason = (sustainedDamage, protectedDamage, controlCount, damageAfterControl) switch
        {
            (false, _, _, _) => "Falta daño sostenido",
            (_, false, _, _) => "Falta protección",
            (_, _, 0, _) => "Falta control",
            (_, _, _, false) => "Falta daño de seguimiento",
            _ when tier != "S" && !profiles.Any(profile => profile.HasFlag(Coverage.HoldGround)) => "Falta aguante",
            _ => profiles.Length == teamSize ? "Equipo equilibrado" : "Sin carencias claras"
        };
        var result = team.Size == 3 && profiles.Length == 3 && observed is { Games: >= ArenaTeamSource.MinimumGames } &&
            observed.Patch == ratings.Catalog.Patch ? observed : null;
        return new(result?.Tier ?? tier, reason, profiles.Length, teamSize, result);

        bool HasPair(Coverage first, Coverage second)
        {
            int partners = profiles.Count(profile => (profile & second) != 0);
            return profiles.Any(profile => (profile & first) != 0 && partners > ((profile & second) != 0 ? 1 : 0));
        }
    }

    internal static ArenaCompositionPick[] Recommend(Ratings ratings, ArenaChampionTierList? tiers, ArenaDraftTeam? team)
    {
        if (team is null || team.Teammates.Length == 0)
            return [];

        var teammates = team.Teammates.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var champions = ratings.Catalog.Champions;
        var allies = champions.Where(champion => teammates.Contains(champion.Key)).ToArray();
        if (allies.Length == 0)
            return [];

        var covered = allies.Aggregate(Coverage.None, (combined, ally) => combined | Profile(ratings, ally));
        var needs = Priorities(covered).OrderByDescending(need => need.Weight).ToArray();
        var unavailable = team.Unavailable.ToHashSet(StringComparer.OrdinalIgnoreCase);
        unavailable.UnionWith(teammates);
        var statistics = tiers?.ClientPatch == ratings.Catalog.Patch ? tiers.Champions : null;
        return champions.Where(champion => !unavailable.Contains(champion.Key)).Select(champion =>
            {
                var contribution = Profile(ratings, champion);
                var matching = needs.Where(need => (need.Coverage & contribution) != 0).ToArray();
                return (Champion: champion, Profile: contribution, Rating: statistics?.GetValueOrDefault(champion.Key), Matching: matching,
                    Score: matching.Sum(need => need.Weight));
            })
            .Where(option => option.Score > 0)
            .OrderByDescending(option => option.Score)
            .ThenBy(option => TierRank(option.Rating?.Tier))
            .ThenByDescending(option => option.Rating?.TopThreePercent)
            .ThenBy(option => option.Champion.Name, StringComparer.OrdinalIgnoreCase)
            .Take(PickLimit)
            .Select(option => new ArenaCompositionPick(option.Champion.Name, option.Rating?.Tier,
                Contribution(option.Champion, option.Profile, option.Matching)))
            .ToArray();
    }

    private static string Contribution(Champion champion, Coverage profile, Need[] matching)
    {
        if ((matching[0].Coverage is Coverage.MagicDamage or Coverage.SustainedDamage) &&
            matching.Any(need => need.Coverage == Coverage.SustainedDamage))
        {
            if (champion.Key == "Diana")
                return "Daño mágico en área de cerca";
            if (champion.Roles[0] == "Marksman")
                return profile.HasFlag(Coverage.MagicDamage) ? "Básicos con daño mágico" : "Básicos a distancia";
        }

        var reason = string.Join(" · ", matching.Select(need => need.Reason).Distinct().Take(2));
        return char.ToUpperInvariant(reason[0]) + reason[1..];
    }

    private static IEnumerable<Need> Priorities(Coverage covered)
    {
        bool front = covered.HasFlag(Coverage.Frontline);
        bool sustained = covered.HasFlag(Coverage.SustainedDamage);
        bool control = covered.HasFlag(Coverage.Control);
        bool protection = covered.HasFlag(Coverage.Protection);
        var damage = covered & Coverage.Damage;

        if (!front)
        {
            yield return new(Coverage.Frontline, 6, "primera línea");
            yield return new(Coverage.Tank, 2, "primera línea");
        }
        if (!sustained)
            yield return new(Coverage.SustainedDamage, damage == Coverage.None ? 7 : 4, "daño sostenido");
        if (!control)
            yield return new(Coverage.Control, 3, "control");
        if (!protection)
            yield return new(Coverage.Protection, sustained ? 4 : 2, "protección");

        if (damage == Coverage.PhysicalDamage)
            yield return new(Coverage.MagicDamage, 5, "daño mágico");
        else if (damage == Coverage.MagicDamage)
            yield return new(Coverage.PhysicalDamage, 5, "daño físico");
        else if (damage == Coverage.None)
            yield return new(Coverage.Damage, 2, "presión ofensiva");

        if (front && sustained && control && protection && damage == Coverage.Damage)
        {
            yield return new(Coverage.Control, 3, "más control");
            yield return new(Coverage.Protection, 2, "más protección");
            yield return new(Coverage.Frontline, 2, "otra primera línea");
            yield return new(Coverage.SustainedDamage, 2, "más daño sostenido");
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

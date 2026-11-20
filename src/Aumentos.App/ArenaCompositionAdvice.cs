using System;
using System.Collections.Generic;
using System.Linq;

namespace Aumentos;

internal sealed record ArenaCompositionPick(string Champion, string? Tier, string Reason);

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
        Damage = PhysicalDamage | MagicDamage
    }

    private sealed record Need(Coverage Coverage, int Weight, string Reason);

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
                return (champion.Name, Rating: statistics?.GetValueOrDefault(champion.Key), Matching: matching,
                    Score: matching.Sum(need => need.Weight));
            })
            .Where(option => option.Score > 0)
            .OrderByDescending(option => option.Score)
            .ThenBy(option => TierRank(option.Rating?.Tier))
            .ThenByDescending(option => option.Rating?.TopThreePercent)
            .ThenBy(option => option.Name, StringComparer.OrdinalIgnoreCase)
            .Take(PickLimit)
            .Select(option => new ArenaCompositionPick(option.Name, option.Rating?.Tier,
                $"Aporta {string.Join(" y ", option.Matching.Select(need => need.Reason).Distinct().Take(2))}"))
            .ToArray();
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
            yield return new(Coverage.Protection, sustained ? 4 : 2, "protección para el equipo");

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

    private static int TierRank(string? tier) => tier is { Length: 1 } && TierOrder.IndexOf(tier[0]) is >= 0 and var rank
        ? rank : TierOrder.Length;
}

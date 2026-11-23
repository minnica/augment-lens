using System;
using System.Collections.Generic;
using System.Linq;

namespace Aumentos;

internal sealed record MayhemCompositionGrade(string Tier, string Reason, int Members, int TeamSize = 5);
internal sealed record MayhemCompositionPick(string Champion, string? Tier, string Reason, string? Tradeoff = null);

internal sealed class MayhemCompositionAdvice
{
    private const int TeamSize = 5;
    private const int PickLimit = 3;
    private readonly string patch;
    private readonly Dictionary<string, DraftChampion> champions;

    [Flags]
    private enum Coverage
    {
        None = 0, Frontline = 1, Tank = 2, Sustained = 4, Control = 8, Protection = 16,
        Physical = 32, Magic = 64, Poke = 128
    }

    private sealed record DraftChampion(string Name, Coverage Profile, string SustainedReason);
    private sealed record Need(Coverage Coverage, int Weight, string Reason);

    internal MayhemCompositionAdvice(Ratings ratings)
    {
        patch = ratings.Catalog.Patch;
        champions = ratings.Catalog.Champions.ToDictionary(champion => champion.Key, champion =>
        {
            var profile = Profile(ratings, champion);
            return new DraftChampion(champion.Name, profile, SustainedReason(champion, profile));
        }, StringComparer.OrdinalIgnoreCase);
    }

    internal MayhemCompositionGrade? Evaluate(string? selected, IEnumerable<string> teammates)
    {
        var members = Resolve(teammates.Prepend(selected ?? "")).Take(TeamSize).ToArray();
        if (members.Length < 2)
            return null;

        var covered = Combine(members);
        bool frontline = covered.HasFlag(Coverage.Frontline);
        bool sustained = covered.HasFlag(Coverage.Sustained);
        bool control = covered.HasFlag(Coverage.Control);
        bool poke = covered.HasFlag(Coverage.Poke);
        // A damage dealer cannot also be the teammate protecting its own uptime.
        bool protectedDamage = HasPair(Coverage.Sustained, Coverage.Protection | Coverage.Frontline);
        bool mixedDamage = HasPair(Coverage.Physical, Coverage.Magic);
        int score = (frontline ? 3 : 0) + (sustained ? 3 : 0) + (control ? 2 : 0) + (mixedDamage ? 2 : 0) +
            (poke ? 1 : 0) + (protectedDamage ? 1 : 0);
        string tier = score switch
        {
            >= 11 when members.Length == TeamSize => "S",
            >= 9 => "A",
            >= 6 => "B",
            >= 3 => "C",
            _ => "D"
        };
        string reason = (frontline, sustained, mixedDamage, control, protectedDamage) switch
        {
            (false, _, _, _, _) => "Falta primera línea",
            (_, false, _, _, _) => "Falta daño sostenido",
            (_, _, false, _, _) => "Falta daño mixto",
            (_, _, _, false, _) => "Falta control",
            (_, _, _, _, true) => "Primera línea protege el daño sostenido",
            _ => "Primera línea y daño mixto"
        };
        return new(tier, reason, members.Length, TeamSize);

        bool HasPair(Coverage first, Coverage second) => members.Any(member => (member.Profile & first) != 0 &&
            members.Any(partner => partner != member && (partner.Profile & second) != 0));
    }

    internal MayhemCompositionPick[] Recommend(MayhemChampionTierList? tiers, string? selected,
        IEnumerable<string> teammates, IEnumerable<string> candidates, IEnumerable<string>? unavailable = null)
    {
        var teammateKeys = teammates.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var confirmedAllies = Resolve(teammateKeys.Where(key => !string.Equals(key, selected, StringComparison.OrdinalIgnoreCase)))
            .Take(TeamSize - 1).ToArray();
        if (confirmedAllies.Length == 0)
            return [];

        if (tiers is not null && tiers.SourcePatch != patch && tiers.SourcePatch != LeaguePatch.Previous(patch))
            tiers = null;
        var excluded = (unavailable ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
        excluded.UnionWith(teammateKeys);
        if (selected is not null)
            excluded.Add(selected);

        var allyCoverage = Combine(confirmedAllies);
        var current = confirmedAllies.Length == TeamSize - 1 && selected is not null
            ? champions.GetValueOrDefault(selected)?.Profile ?? Coverage.None : Coverage.None;
        var uniqueCoverage = current & ~allyCoverage;
        var priorities = Priorities(allyCoverage).OrderByDescending(need => need.Weight).ToArray();
        return candidates.Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(key => !excluded.Contains(key) && champions.ContainsKey(key))
            .Select(key =>
            {
                var champion = champions[key];
                var matches = priorities.Where(need => (need.Coverage & champion.Profile) != 0).ToArray();
                return (Champion: champion, Tier: tiers?.Letter(key), Rank: tiers?.Rank(key),
                    Matches: matches);
            })
            .Where(candidate => candidate.Matches.Length > 0)
            .OrderBy(candidate => candidate.Tier is { } tier ? "SABCD".IndexOf(tier, StringComparison.Ordinal) : 5)
            .ThenByDescending(candidate => candidate.Matches.Sum(need => need.Weight))
            .ThenBy(candidate => candidate.Rank ?? int.MaxValue)
            .ThenBy(candidate => candidate.Champion.Name, StringComparer.OrdinalIgnoreCase)
            .Take(PickLimit)
            .Select(candidate => new MayhemCompositionPick(candidate.Champion.Name, candidate.Tier,
                Contribution(candidate.Champion, candidate.Matches[0]),
                LostRole(uniqueCoverage & ~candidate.Champion.Profile)))
            .ToArray();
    }

    private static string Contribution(DraftChampion champion, Need need) => need.Coverage switch
    {
        Coverage.Sustained => champion.SustainedReason,
        Coverage.Frontline when champion.Profile.HasFlag(Coverage.Tank) => "Tanque de primera línea",
        Coverage.Frontline when champion.Profile.HasFlag(Coverage.Control) => "Primera línea con control",
        Coverage.Control when champion.Profile.HasFlag(Coverage.Frontline) => "Control desde primera línea",
        _ => need.Reason
    };

    private static string SustainedReason(Champion champion, Coverage profile) => champion.Roles[0] switch
    {
        "Marksman" => "Básicos a distancia",
        "Fighter" when champion.Key == "Diana" => "Daño mágico en área de cerca",
        "Fighter" when profile.HasFlag(Coverage.Physical) => "Daño físico con básicos",
        _ when profile.HasFlag(Coverage.Magic | Coverage.Poke) => "Daño mágico a distancia",
        _ when profile.HasFlag(Coverage.Magic) => "Daño mágico sostenido",
        _ => "Daño sostenido"
    };

    private static string? LostRole(Coverage lost) => lost switch
    {
        _ when lost.HasFlag(Coverage.Frontline) => "El equipo pierde primera línea",
        _ when lost.HasFlag(Coverage.Sustained) => "El equipo pierde daño sostenido",
        _ when lost.HasFlag(Coverage.Control) => "El equipo pierde control",
        _ when lost.HasFlag(Coverage.Protection) => "El equipo pierde apoyo defensivo",
        _ => null
    };

    private IEnumerable<DraftChampion> Resolve(IEnumerable<string> keys) => keys.Distinct(StringComparer.OrdinalIgnoreCase)
        .Select(key => champions.GetValueOrDefault(key)).OfType<DraftChampion>();

    private static Coverage Combine(IEnumerable<DraftChampion> members) =>
        members.Aggregate(Coverage.None, (covered, member) => covered | member.Profile);

    private static IEnumerable<Need> Priorities(Coverage covered)
    {
        bool frontline = covered.HasFlag(Coverage.Frontline);
        bool sustained = covered.HasFlag(Coverage.Sustained);
        bool control = covered.HasFlag(Coverage.Control);
        bool protection = covered.HasFlag(Coverage.Protection);
        bool physical = covered.HasFlag(Coverage.Physical);
        bool magic = covered.HasFlag(Coverage.Magic);
        bool poke = covered.HasFlag(Coverage.Poke);
        if (!frontline)
        {
            yield return new(Coverage.Frontline, 6, "Primera línea");
            yield return new(Coverage.Tank, 1, "Primera línea");
        }
        if (!sustained)
            yield return new(Coverage.Sustained, 6, "Daño sostenido");
        if (!control)
            yield return new(Coverage.Control, 3, "Control");
        if (physical != magic)
            yield return physical ? new(Coverage.Magic, 4, "Daño mágico") : new(Coverage.Physical, 4, "Daño físico");
        if (!poke)
            yield return new(Coverage.Poke, 2, "Hostigamiento");
        if (!protection && sustained)
            yield return new(Coverage.Protection, 2, "Protección");
        if (frontline && sustained && control && physical && magic && (poke || protection))
        {
            yield return new(Coverage.Control, 2, "Control");
            yield return new(Coverage.Sustained, 1, "Daño sostenido");
            yield return new(Coverage.Protection, 1, "Protección");
        }
    }

    private static Coverage Profile(Ratings ratings, Champion champion)
    {
        var role = champion.Roles[0];
        var key = champion.Key;
        var coverage = role switch
        {
            "Tank" => Coverage.Frontline | Coverage.Tank,
            "Fighter" when ratings.HasTrait(key, "durability") => Coverage.Frontline,
            "Support" when champion.Roles.Contains("Tank") && ratings.HasTrait(key, "durability") => Coverage.Frontline,
            _ => Coverage.None
        };
        if (role == "Marksman" || ratings.HasTrait(key, "attackspeed") ||
            role == "Fighter" && ratings.HasTrait(key, "attacks") ||
            key is "Anivia" or "AurelionSol" or "Azir" or "Brand" or "Cassiopeia" or "Heimerdinger" or "Karthus" or
                "Lillia" or "Malzahar" or "Rumble" or "Singed" or "Swain" or "Teemo" or "Viktor" or "Zyra")
            coverage |= Coverage.Sustained;
        if (ratings.HasTrait(key, "control"))
            coverage |= Coverage.Control;
        if (ratings.HasTrait(key, "ally"))
            coverage |= Coverage.Protection;
        if (ratings.HasTrait(key, "range") && (role != "Marksman" ||
            key is "Ashe" or "Caitlyn" or "Corki" or "Ezreal" or "Jhin" or "Kaisa" or "KogMaw" or "Senna" or "Varus"))
            coverage |= Coverage.Poke;
        if (role is "Marksman" or "Mage" or "Fighter" or "Assassin")
            coverage |= EnemyDamageMix.PhysicalShare(ratings, champion, 0, 0) switch
            {
                0 => Coverage.Magic,
                1 => Coverage.Physical,
                _ => Coverage.None
            };
        return coverage;
    }
}

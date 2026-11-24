using System;
using System.Globalization;
using System.Linq;

namespace Aumentos;

internal sealed class ShopAugmentAffinity(Catalog catalog)
{
    internal MayhemBuildPlan? Prioritize(Mode mode, string[]? held, MayhemBuildPlan? plan)
    {
        if (plan is null || plan.Swap is not null || held is not { Length: > 0 })
            return plan;

        int first = Array.FindIndex(plan.Steps, step => !step.Owned && !OpGgBuildPage.IsBoot(step.Item.Id));
        if (first < 0 || HasSituationalReason(plan.Steps[first]))
            return plan;

        var keys = held.Select(value => Ratings.Normalize(AugmentKey(value))).ToHashSet(StringComparer.Ordinal);
        var owned = catalog.Augments.Where(augment => augment.Mode == mode.ToString() &&
            (keys.Contains(Ratings.Normalize(AugmentKey(augment.Key))) || keys.Contains(Ratings.Normalize(augment.Name)) ||
             keys.Contains(augment.Id.ToString(CultureInfo.InvariantCulture)))).ToArray();
        var candidates = plan.Steps.Select((step, index) => (Step: step, Index: index))
            .Where(candidate => candidate.Index >= first && !candidate.Step.Owned &&
                !OpGgBuildPage.IsBoot(candidate.Step.Item.Id) && !HasSituationalReason(candidate.Step))
            .Select(candidate => (candidate.Index, Matches: owned.Where(augment => Fits(AugmentKey(augment.Key), candidate.Step.Item.Id)).ToArray()));
        var best = candidates.OrderByDescending(candidate => candidate.Matches.Length).First();
        if (best.Matches.Length == 0)
            return plan;

        var reordered = plan.Steps.ToArray();
        var chosen = reordered[best.Index] with { Detail = $"Sinergia con {best.Matches[0].Name}" };
        Array.Copy(reordered, first, reordered, first + 1, best.Index - first);
        reordered[first] = chosen;
        return plan with { Steps = reordered };
    }

    private static bool HasSituationalReason(BuildStep step) => step.Detail is not
        (null or MayhemBuilds.PublishedCore or ArenaBuilds.PublishedPick or ArenaBuilds.ChampionFit);

    private static string AugmentKey(string key)
    {
        if (key.StartsWith("Augment_", StringComparison.OrdinalIgnoreCase))
            key = key[8..];
        return key.StartsWith("ARAM_", StringComparison.OrdinalIgnoreCase) ? key[5..] : key;
    }

    private static bool Fits(string key, int id)
    {
        if (id is >= 220000 and < 230000)
            id -= 220000;
        id = MayhemBuilds.BaseId(id);
        return key switch
        {
            "EtherealWeapon" or "Typhoon" => id is 3091 or 3124 or 3153 or 6672,
            "JeweledGauntlet" => id is 3031 or 3036 or 3085 or 3094 or 3508 or 6673 or 6676,
            _ => false
        };
    }
}

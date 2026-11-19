using System;
using System.Collections.Generic;
using System.Linq;

namespace Aumentos;

internal readonly record struct EnemyDamageMix(int PhysicalPercent)
{
    internal int MagicPercent => 100 - PhysicalPercent;

    internal static EnemyDamageMix? Estimate(IEnumerable<double?> physicalShares) =>
        physicalShares.Average() is { } average
            ? new((int)Math.Round(100 * average, MidpointRounding.AwayFromZero))
            : null;

    internal static double? PhysicalShare(Ratings ratings, Champion champion, int physicalItems, int magicItems)
    {
        // Each rival contributes equally. Their champion profile and offensive items determine their own split.
        if (ratings.HasTrait(champion.Key, "ap"))
            magicItems++;
        else if (champion.Roles[0] is "Marksman" or "Fighter" or "Assassin")
            physicalItems++;

        int total = physicalItems + magicItems;
        return total == 0 ? null : (double)physicalItems / total;
    }
}

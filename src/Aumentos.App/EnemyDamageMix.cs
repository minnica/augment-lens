using System;
using System.Collections.Generic;
using System.Linq;

namespace Aumentos;

internal readonly record struct EnemyDamageMix(int PhysicalPercent, int TruePercent = 0)
{
    internal int MagicPercent => 100 - PhysicalPercent - TruePercent;

    internal static EnemyDamageMix? Estimate(IEnumerable<(double Physical, double True)?> shares)
    {
        var rivals = shares.OfType<(double Physical, double True)>().ToArray();
        if (rivals.Length == 0)
            return null;
        int trueDamage = RoundPercent(rivals.Average(rival => rival.True));
        return new(Math.Min(100 - trueDamage, RoundPercent(rivals.Average(rival => rival.Physical))), trueDamage);
    }

    private static int RoundPercent(double share) => (int)Math.Round(100 * share, MidpointRounding.AwayFromZero);

    internal static (double Physical, double True)? Shares(Ratings ratings, Champion champion,
        int physicalItems, int magicItems, int trueDamageItems)
    {
        // These weights describe recurring sources and finishers. The live API does not report damage dealt.
        double championTrue = champion.Key switch
        {
            "Camille" or "Fiora" or "Vayne" or "Velkoz" => .25,
            "Corki" or "Gangplank" or "Gwen" or "MasterYi" => .15,
            "Ahri" or "Belveth" or "Chogath" or "Darius" or "Garen" or "KogMaw" or
                "KSante" or "Lillia" or "Olaf" or "RekSai" or "Sett" or "Smolder" or "Zoe" => .08,
            _ => 0
        };
        double trueDamage = 1 - (1 - championTrue) * Math.Pow(.92, trueDamageItems);
        var physical = PhysicalShare(ratings, champion, physicalItems, magicItems);
        if (physical is null && trueDamage == 0)
            return null;
        // Known true-damage users without an offensive item still deal regular damage through their kit.
        double fallback = champion.Key is "Chogath" or "KogMaw" or "Lillia" ? 0 : 1;
        return ((physical ?? fallback) * (1 - trueDamage), trueDamage);
    }

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

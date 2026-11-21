using System;
using System.Linq;

namespace Aumentos;

internal static class ArenaUpgradeRatings
{
    internal const int LightEmUp = 51;
    internal const int Cerberus = 323;
    private const int OmniSoul = 62;
    private const int ReplaceAugment = 364;

    internal static void Apply(Ratings ratings, GameContext game, int?[] ids, int?[] levels, CardComparison?[] badges)
    {
        var soul = Array.IndexOf(ids, OmniSoul);
        if (soul >= 0 && badges[soul] is { UpgradeEffect.Length: > 0 } upgrade)
        {
            badges[soul] = upgrade with
            {
                Local = nameof(Tier.B),
                LocalReason = "Ganas un alma aleatoria y conservas tus aumentos",
                RecommendedUpgrade = ids.Length == 2 && ids[1 - soul] == ReplaceAugment &&
                    badges[1 - soul] is { IsArenaVariable: true }
            };
        }

        var fireworks = Array.IndexOf(ids, LightEmUp);
        if (fireworks >= 0 && levels[fireworks] == 2 && badges[fireworks] is { } light)
            badges[fireworks] = light with { LocalReason = "Fuegos artificiales de 4 a 5 · Daño de 90 % a 100 %" };

        if (game.Champion.Equals("Jax", StringComparison.OrdinalIgnoreCase))
        {
            RecommendJaxUpgrade(ids, levels, badges, fireworks);
            return;
        }
        if (!ratings.HasTrait(game.Champion, "attackspeed") || !ratings.HasTrait(game.Champion, "crit"))
            return;

        var choices = ids.Zip(levels, Rate).ToArray();
        bool complete = choices.Length > 1 && choices.All(choice => choice is not null) &&
            badges.All(badge => badge?.Local is "S" or "A");
        int best = complete ? choices.Max(choice => choice!.Priority) : 0;
        bool unique = complete && choices.Count(choice => choice!.Priority == best) == 1;
        for (int index = 0; index < choices.Length; index++)
            if (choices[index] is { } choice && badges[index] is { Local: "S" or "A" } badge)
                badges[index] = badge with { LocalReason = choice.Reason, RecommendedUpgrade = unique && choice.Priority == best };
    }

    private static void RecommendJaxUpgrade(int?[] ids, int?[] levels, CardComparison?[] badges, int fireworks)
    {
        const int deft = 22;
        var speed = Array.IndexOf(ids, deft);
        if (IsPreferred(speed, 3, LightEmUp))
            badges[speed] = badges[speed]! with { RecommendedUpgrade = true };
        else if (IsPreferred(fireworks, 2, Cerberus))
            badges[fireworks] = badges[fireworks]! with { RecommendedUpgrade = true };

        bool IsPreferred(int candidate, int level, int alternative) =>
            badges.Length > 1 && candidate >= 0 && levels[candidate] == level && badges[candidate] is { Local: "A" } &&
            Enumerable.Range(0, badges.Length).Where(index => index != candidate)
                .All(index => badges[index]?.Local is "B" or "C" ||
                    (ids[index] == alternative && badges[index]?.Local == "A" && levels[index] is null));
    }

    private static Upgrade? Rate(int? id, int? level) => (id, level) switch
    {
        (52, 2) => new(3, "Límite de ataques/s de 3.5 a 10"),
        (1328, 2) => new(2, "Duplica el máximo de acumulaciones"),
        (22, 2) => new(1, "Velocidad de ataque adicional desde el inicio"),
        (22, 3) => new(2, "Velocidad de ataque adicional desde el inicio"),
        _ => null
    };

    private sealed record Upgrade(int Priority, string Reason);
}

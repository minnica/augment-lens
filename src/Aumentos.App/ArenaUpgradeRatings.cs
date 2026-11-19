using System.Linq;

namespace Aumentos;

internal static class ArenaUpgradeRatings
{
    internal static bool Supports(int id) => id is 22 or 52 or 1328;

    internal static void Apply(Ratings ratings, GameContext game, int?[] ids, int?[] levels, CardComparison?[] badges)
    {
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

    private static Upgrade? Rate(int? id, int? level) => (id, level) switch
    {
        (52, 2) => new(3, "Límite de ataques/s de 3.5 a 10"),
        (1328, 2) => new(2, "Duplica el máximo de acumulaciones"),
        (1328, 3) => new(4, "Aumenta el límite y las acumulaciones de críticos"),
        (22, 2) => new(1, "Velocidad de ataque adicional desde el inicio"),
        (22, 3) => new(2, "Velocidad de ataque adicional desde el inicio"),
        _ => null
    };

    private sealed record Upgrade(int Priority, string Reason);
}

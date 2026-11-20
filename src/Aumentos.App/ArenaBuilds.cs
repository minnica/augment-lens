using System;
using System.Collections.Generic;
using System.Linq;

namespace Aumentos;

internal sealed class ArenaBuilds(Ratings ratings)
{
    internal const string ChampionFit = "Encaja con tu campeón";
    internal const string PublishedPick = "De la build de OP.GG";
    private const int Slots = 6;
    private static readonly HashSet<string> Healers = new(["Aatrox", "Briar", "DrMundo", "Illaoi", "Nami",
        "Nilah", "Sona", "Soraka", "Swain", "Sylas", "Taric", "Vladimir", "Warwick", "Yuumi", "Zac"], StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> Shielders = new(["Ivern", "Janna", "Karma", "Lulu", "Lux", "Milio",
        "Orianna", "Seraphine", "Shen", "Sona"], StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<int> AntiHealing = [223033, 223075, 223165, 226609];

    private readonly Dictionary<string, Champion> champions = ratings.Catalog.Champions.ToDictionary(
        champion => champion.Key, StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, ArenaItem> equipment = (ratings.Catalog.ArenaLegendaries ?? [])
        .Concat(ratings.Catalog.ArenaPrismatics ?? []).Concat(ratings.Catalog.ArenaEquipment ?? [])
        .DistinctBy(item => item.Id).ToDictionary(item => item.Id);
    private readonly HashSet<int> shopLegendaries = (ratings.Catalog.ArenaLegendaries ?? [])
        .Where(item => item.Buyable).Select(item => item.Id).ToHashSet();
    private SuggestedSwap? suggestedSwap;

    internal void RememberSuggestedSwap(GameContext game, MayhemBuildPlan? plan)
    {
        if (plan?.Swap is { } swap)
        {
            var kept = plan.Steps.Where(step => step.Item.Id != swap.Outgoing.Id).ToArray();
            suggestedSwap = new(game, swap, kept, Sold: false);
        }
    }

    internal void ObserveInventory(GameContext game, ArenaMatch? match)
    {
        if (suggestedSwap is not { } suggestion || match is not { InventoryKnown: true })
            return;

        var owned = OwnedItems(match).Select(item => BaseId(item.Id)).ToHashSet();
        if (game != suggestion.Game || !suggestion.Kept.All(step => owned.Contains(BaseId(step.Item.Id))))
            ResetSwap();
        else if (owned.Count == suggestion.Kept.Length)
            suggestedSwap = suggestion with { Sold = true };
        else if (suggestion.Sold || !owned.Contains(suggestion.Swap.Outgoing.Id))
            ResetSwap();
    }

    internal void ResetSwap() => suggestedSwap = null;

    internal EnemyDamageMix? EstimateEnemyDamage(ArenaMatch? match) => match is null ? null :
        EnemyDamageMix.Estimate(RecentOpponents(match).Where(opponent => champions.ContainsKey(opponent.Champion)).Select(opponent =>
        {
            var items = EquippedItems(opponent).Where(item => !IsBoot(item)).ToArray();
            return EnemyDamageMix.PhysicalShare(ratings, champions[opponent.Champion],
                items.Count(item => Has(item, "Damage") || Has(item, "CriticalStrike")), items.Count(item => Has(item, "SpellDamage")));
        }));

    internal MayhemBuildPlan? Recommend(GameContext game, ArenaMatch? match, OpGgBuildSnapshot? build)
    {
        if (game.Mode != Mode.Arena || match is null || !champions.TryGetValue(game.Champion, out var champion) || equipment.Count == 0)
            return null;

        var owned = OwnedItems(match).ToArray();
        var steps = owned.Select(item => Step(item, "Comprado", owned: true)).ToList();
        if (suggestedSwap is { Sold: true } pending && pending.Game == game)
        {
            IEnumerable<BuildStep> kept = match.InventoryKnown ? steps : pending.Kept;
            return new([..kept, new(pending.Swap.Incoming, pending.Swap.Reason)]);
        }

        var source = ValidSource(build, game) ? build : null;
        var paths = source?.Groups.Where(group => group.Section == OpGgBuildSection.Core).SelectMany(group => group.Choices)
            .Select(choice => choice.Select(item => Resolve(item.Id)).OfType<ArenaItem>()
                .Select(item => equipment.GetValueOrDefault(BaseId(item.Id))).OfType<ArenaItem>()
                .Where(item => item.Buyable && !IsBoot(item)).DistinctBy(item => item.Id).ToArray())
            .Where(choice => choice.Length > 0).ToArray() ?? [];
        var baseIds = owned.Select(item => BaseId(item.Id)).ToHashSet();
        var core = paths.MaxBy(path => path.Count(item => baseIds.Contains(item.Id))) ?? [];
        var role = BuildRole(champion, owned, core);

        int[] defaults = role switch
        {
            "Tank" => [223084, 222524, 226665, 223068, 223050, 223190],
            "Support" => [226617, 223107, 223190, 223222, 226620, 222065],
            "Mage" => [226655, 224645, 223089, 224629, 226653, 223135],
            "Marksman" => [223032, 223031, 223036, 223072, 223046, 223153],
            "Assassin" => [223142, 226696, 226694, 226699, 226695, 226698],
            _ => [223078, 226610, 226692, 223053, 223071, 223153]
        };

        if (steps.Count == Slots)
        {
            var equipped = steps.ToArray();
            return new(equipped, match.InventoryKnown ? RecommendSwap(champion, equipped, core, defaults, role, match) : null);
        }

        var rivals = RecentOpponents(match).Select(opponent => ReadRival(opponent)).OfType<Rival>().ToArray();
        if (steps.Count < 4 && champion.Key != "Cassiopeia" && !owned.Any(IsBoot))
        {
            var bootIds = source?.Groups.Where(group => group.Section == OpGgBuildSection.Boots)
                .SelectMany(group => group.Choices).SelectMany(choice => choice).Select(item => item.Id) ?? [];
            var boot = bootIds.Append(DefaultBoot(role)).Select(Resolve).OfType<ArenaItem>()
                .FirstOrDefault(item => item.Buyable && IsBoot(item));
            if (boot is not null)
                steps.Add(Step(boot, bootIds.Any(id => Resolve(id)?.Id == boot.Id) ? PublishedPick : "Botas para tu campeón"));
        }

        foreach (var item in core.Concat(defaults.Select(Resolve).OfType<ArenaItem>()))
        {
            if (steps.Count >= 4)
                break;
            if (CanAdd(item, steps))
                steps.Add(Step(item, core.Contains(item) ? PublishedPick : ChampionFit));
        }

        foreach (var counter in Counters(role, rivals).OrderByDescending(counter => counter.Priority))
        {
            if (steps.Count == Slots)
                break;
            if (counter.ItemIds.Any(AntiHealing.Contains) && steps.Any(step => AntiHealing.Contains(step.Item.Id)))
                continue;
            var item = counter.ItemIds.Select(Resolve).OfType<ArenaItem>().FirstOrDefault(item => CanAdd(item, steps));
            if (item is not null)
                steps.Add(Step(item, counter.Reason));
        }

        var affinity = equipment.Values.Where(item => item.Buyable && !IsBoot(item))
            .Select(item => (Item: item, Rating: ratings.Legendaries.Rate(champion.Key, item)))
            .Where(pick => pick.Rating is { Tier: Tier.S or Tier.A or Tier.B })
            .OrderBy(pick => pick.Rating!.Tier).ThenBy(pick => pick.Item.Id).Select(pick => pick.Item);
        var fallback = core.Concat(defaults.Select(Resolve).OfType<ArenaItem>()).Concat(affinity);
        foreach (var item in fallback)
        {
            if (steps.Count == Slots)
                break;
            if (CanAdd(item, steps))
                steps.Add(Step(item, core.Contains(item) ? PublishedPick : ChampionFit));
        }
        return steps.Count == Slots ? new(steps.ToArray()) : null;
    }

    private BuildSwap? RecommendSwap(Champion champion, BuildStep[] equipped, ArenaItem[] core, int[] defaults, string role, ArenaMatch match)
    {
        var protectedIds = (core.Length == 0 ? defaults.Take(2) : core.Take(3).Select(item => item.Id)).ToHashSet();
        var replaceable = equipped.Where(step =>
        {
            int id = step.Item.Id;
            return shopLegendaries.Contains(id) && id == BaseId(id) && !protectedIds.Contains(id);
        }).OrderByDescending(step => ratings.Legendaries.Rate(champion.Key, equipment[step.Item.Id])?.Tier ?? Tier.B)
            .ThenBy(step => step.Item.Id);
        var rivals = RecentOpponents(match).Select(opponent => ReadRival(opponent, purchasesOnly: true)).OfType<Rival>().ToArray();
        var counters = Counters(role, rivals, purchasesOnly: true).OrderByDescending(counter => counter.Priority)
            .Select(counter => new SwapCounter(counter.Reason, counter.ItemIds.Where(shopLegendaries.Contains).ToArray(),
                id => Covers(equipment[id], counter.Need)));
        return BuildSwap.Find(equipped, replaceable, counters, (id, kept) => CanAdd(equipment[id], kept),
            id => new(id, equipment[id].Name));
    }

    private static bool Covers(ArenaItem item, Need need)
    {
        if (IsBoot(item))
            return false;
        int id = BaseId(item.Id);
        return need switch
        {
            Need.Healing => AntiHealing.Contains(id),
            Need.Crit => id == 223143,
            Need.Armor => id is 223033 or 223036 or 223071 or 223302 or 226694,
            Need.MagicResist => id is 223135 or 223137 or 223302,
            Need.Shields => id == 226695,
            Need.MagicDamage => Has(item, "SpellBlock") || Has(item, "MagicResist"),
            Need.PhysicalDamage => Has(item, "Armor"),
            Need.Health => id is 226653 or 223153,
            _ => false
        };
    }

    private bool ValidSource(OpGgBuildSnapshot? source, GameContext game) => source is { Mode: Mode.Arena } &&
        source.ClientPatch == ratings.Catalog.Patch && (source.SourcePatch == source.ClientPatch ||
        source.SourcePatch == LeaguePatch.Previous(source.ClientPatch)) &&
        string.Equals(source.Champion, game.Champion, StringComparison.OrdinalIgnoreCase);

    private string BuildRole(Champion champion, ArenaItem[] owned, ArenaItem[] core)
    {
        var items = owned.Concat(core).DistinctBy(item => BaseId(item.Id)).ToArray();
        var initial = champion.Roles[0];
        if (initial == "Support" && items.Any(item => item.Id is 222065 or 223107 or 223222 or 223504 or 226616 or 226617 or 226620 or 226621))
            return initial;
        if (items.Count(item => Has(item, "SpellDamage") && !Has(item, "Damage")) >= 2 &&
            items.Count(item => Has(item, "Armor") || Has(item, "SpellBlock")) < 2)
            return "Mage";
        if (ratings.HasTrait(champion.Key, "crit") && items.Count(item => Has(item, "CriticalStrike")) >= 2)
            return "Marksman";
        return initial == "Assassin" && ratings.HasTrait(champion.Key, "ap") ? "Mage" : initial;
    }

    private static IEnumerable<Counter> Counters(string role, Rival[] rivals, bool purchasesOnly = false)
    {
        var magical = rivals.Where(rival => rival.MagicDamage).ToArray();
        var physical = rivals.Where(rival => rival.PhysicalDamage).ToArray();
        var healing = rivals.Where(rival => rival.Healing).ToArray();
        var armored = rivals.Where(rival => rival.Armor > 0).ToArray();
        var resistant = rivals.Where(rival => rival.MagicResist > 0).ToArray();
        var shielded = rivals.Where(rival => rival.Shielded).ToArray();
        var critical = rivals.Where(rival => rival.Crit >= 2).ToArray();
        var tanks = rivals.Where(rival => rival.Health >= 2 || !purchasesOnly && rival.Champion.Roles[0] == "Tank").ToArray();
        bool frontLine = role is "Tank" or "Support" or "Fighter";

        if (healing.Length > 0)
            yield return new(Need.Healing, 90 + healing.Length, Reason("Para reducir la curación de", healing), role switch
            {
                "Tank" => [223075], "Mage" or "Support" => [223165], "Marksman" => [223033], _ => [226609]
            });
        if (critical.Length > 0 && frontLine)
            yield return new(Need.Crit, 85, Reason("Contra los golpes críticos de", critical), [223143]);
        if (armored.Length >= 2 || armored.Any(rival => rival.Armor >= 2))
            yield return new(Need.Armor, 80, Reason("Contra la armadura de", armored), role switch
            {
                "Marksman" => [223036, 223302], "Fighter" => [223071, 226694], "Assassin" => [226694], _ => []
            });
        if ((resistant.Length >= 2 || resistant.Any(rival => rival.MagicResist >= 2)) && role == "Mage")
            yield return new(Need.MagicResist, 80, Reason("Contra la resistencia mágica de", resistant), [223135, 223137]);
        if (shielded.Length > 0 && role is "Assassin" or "Fighter")
            yield return new(Need.Shields, 79, Reason("Contra los escudos de", shielded), [226695]);
        if (magical.Length > physical.Length)
            yield return new(Need.MagicDamage, 70 + magical.Length, Reason("Contra el daño mágico de", magical), role switch
            {
                "Tank" or "Support" => [222504, 224401, 223065], "Mage" => [223102], "Marksman" => [223091, 223156], _ => [223156]
            });
        if (physical.Length > magical.Length)
            yield return new(Need.PhysicalDamage, 70 + physical.Length, Reason("Contra el daño físico de", physical), role switch
            {
                "Tank" or "Support" => [223110, 223143, 223742], "Mage" => [223157], "Marksman" => [223026], _ => [226333, 223026]
            });
        if (tanks.Length > 0 && role is "Mage" or "Fighter" or "Marksman")
            yield return new(Need.Health, 60 + tanks.Length, Reason("Contra rivales con mucha vida como", tanks),
                role == "Mage" ? [226653] : [223153]);
    }

    private Rival? ReadRival(ArenaOpponent opponent, bool purchasesOnly = false)
    {
        if (!champions.TryGetValue(opponent.Champion, out var champion))
            return null;
        var items = EquippedItems(opponent).Where(item => !purchasesOnly || !IsBoot(item)).ToArray();
        int ap = items.Count(item => Has(item, "SpellDamage") && !Has(item, "Damage"));
        int ad = items.Count(item => Has(item, "Damage") && !Has(item, "SpellDamage"));
        var role = champion.Roles[0];
        bool naturalMagic = role == "Mage" || ratings.HasTrait(champion.Key, "ap");
        bool naturalPhysical = !naturalMagic && role is "Marksman" or "Fighter" or "Assassin";
        bool magic = purchasesOnly ? ap >= 2 && ap > ad : ap > ad || (ap == ad && naturalMagic);
        bool physical = purchasesOnly ? ad >= 2 && ad > ap : ad > ap || (ad == ap && naturalPhysical);
        return new(champion, magic, physical,
            (!purchasesOnly && Healers.Contains(champion.Key)) || items.Any(item => Has(item, "LifeSteal") || Has(item, "SpellVamp") ||
                item.Id is 226617 or 226620 or 443062),
            (!purchasesOnly && Shielders.Contains(champion.Key)) ||
                items.Any(item => item.Id is 223053 or 223119 or 223190 or 226673 or 443193),
            items.Count(item => !IsBoot(item) && Has(item, "Armor")),
            items.Count(item => !IsBoot(item) && (Has(item, "SpellBlock") || Has(item, "MagicResist"))),
            items.Count(item => Has(item, "CriticalStrike")), items.Count(item => Has(item, "Health")));
    }

    private static IEnumerable<ArenaOpponent> RecentOpponents(ArenaMatch match) => match.Opponents.Take(3);
    private IEnumerable<ArenaItem> EquippedItems(ArenaOpponent opponent) =>
        opponent.Items.Select(Resolve).OfType<ArenaItem>().DistinctBy(item => item.Id);
    private IEnumerable<ArenaItem> OwnedItems(ArenaMatch match) =>
        match.Items.Select(Resolve).OfType<ArenaItem>().DistinctBy(item => BaseId(item.Id)).Take(Slots);

    private ArenaItem? Resolve(int id) => equipment.GetValueOrDefault(id) ?? equipment.GetValueOrDefault(id is > 0 and < 10000 ? id + 220000 : id);
    private static bool Has(ArenaItem item, string category) => item.Categories?.Contains(category) == true;
    private static bool IsBoot(ArenaItem item) => Has(item, "Boots");
    private static bool CanAdd(ArenaItem item, IReadOnlyList<BuildStep> steps)
    {
        var group = ExclusiveGroup(item.Id);
        return item.Buyable && !IsBoot(item) && !steps.Any(step => BaseId(step.Item.Id) == item.Id ||
            (group != 0 && ExclusiveGroup(step.Item.Id) == group));
    }
    private static int BaseId(int id) => id switch
    {
        222530 => 222526, 223040 => 223003, 223042 => 223004, 223121 => 223119, _ => id
    };
    private static int ExclusiveGroup(int id) => BaseId(id) switch
    {
        223033 or 223036 or 223302 or 226694 => 1,
        223135 or 223137 => 2,
        223053 or 223156 or 226673 => 3,
        222526 or 223003 or 223004 or 223119 => 4,
        223074 or 223748 or 226631 or 226698 or 226668 => 5,
        _ => 0
    };
    private static int DefaultBoot(string role) => role switch
    {
        "Mage" => 223020, "Marksman" => 223006, "Tank" => 223111, "Support" => 223158, _ => 223047
    };
    private static string Reason(string label, Rival[] rivals) =>
        $"{label} {string.Join(" y ", rivals.Take(2).Select(rival => rival.Champion.Name))}";
    private static BuildStep Step(ArenaItem item, string detail, bool owned = false) => new(new(item.Id, item.Name), detail, owned);
    private enum Need { Healing, Crit, Armor, MagicResist, Shields, MagicDamage, PhysicalDamage, Health }
    private sealed record SuggestedSwap(GameContext Game, BuildSwap Swap, BuildStep[] Kept, bool Sold);
    private sealed record Counter(Need Need, int Priority, string Reason, int[] ItemIds);
    private sealed record Rival(Champion Champion, bool MagicDamage, bool PhysicalDamage, bool Healing, bool Shielded,
        int Armor, int MagicResist, int Crit, int Health);
}

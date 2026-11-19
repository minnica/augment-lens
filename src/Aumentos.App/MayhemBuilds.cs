using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Aumentos;

internal sealed record BuildStep(OpGgBuildItem Item, string? Detail = null, bool Owned = false);
internal sealed record MayhemBuildPlan(BuildStep[] Steps);

internal sealed record MayhemItem(string Name, bool Finished, bool Available, bool Boots, bool OnHit, bool Sustain,
    int Armor, int MagicResist, int Health, int AttackDamage, int AbilityPower, decimal Crit, decimal AttackSpeed);
internal sealed record MayhemItemCatalog(string Patch, Dictionary<int, MayhemItem> Items);

internal sealed class MayhemBuilds
{
    [Flags]
    private enum Style { Mage = 1, Support = 2, Tank = 4, Fighter = 8, Lethality = 16, Crit = 32, OnHit = 64 }

    private enum Need { General, MagicResist, Armor, Healing, Shields, Health, Dive, MagicDamage, PhysicalDamage }
    private sealed record Choice(int Id, Style Styles, Need Need = Need.General);
    private sealed record Threat(Need Need, int Priority, string Reason);
    private sealed record Opponent(Champion Champion, int Armor, int MagicResist, int Health,
        bool Sustain, bool Shields, bool MagicDamage, bool PhysicalDamage, bool Dive);

    private const Style Attackers = Style.Crit | Style.OnHit;
    private const Style Physical = Style.Fighter | Style.Lethality | Attackers;

    private static readonly Choice[] Choices =
    [
        new(3135, Style.Mage, Need.MagicResist), new(3137, Style.Mage | Style.Support, Need.MagicResist),
        new(3302, Style.OnHit, Need.MagicResist),
        new(3036, Style.Crit, Need.Armor), new(3302, Style.OnHit, Need.Armor),
        new(3071, Style.Fighter, Need.Armor), new(6694, Style.Lethality | Style.Fighter, Need.Armor),
        new(3165, Style.Mage | Style.Support, Need.Healing), new(3033, Attackers, Need.Healing),
        new(6609, Style.Fighter | Style.Lethality, Need.Healing), new(3075, Style.Tank, Need.Healing),
        new(6695, Style.Lethality | Style.Fighter, Need.Shields),
        new(6653, Style.Mage, Need.Health), new(3153, Style.OnHit | Style.Fighter, Need.Health),
        new(3157, Style.Mage, Need.Dive), new(3190, Style.Support, Need.Dive),
        new(6673, Style.Crit, Need.Dive), new(3053, Style.Fighter, Need.Dive),
        new(3814, Style.Lethality, Need.Dive), new(2502, Style.Tank, Need.Dive),
        new(3102, Style.Mage, Need.MagicDamage), new(3190, Style.Support, Need.MagicDamage),
        new(2504, Style.Tank, Need.MagicDamage), new(4401, Style.Tank, Need.MagicDamage),
        new(3156, Physical, Need.MagicDamage), new(3091, Attackers, Need.MagicDamage),
        new(3157, Style.Mage, Need.PhysicalDamage), new(3190, Style.Support, Need.PhysicalDamage),
        new(3143, Style.Tank, Need.PhysicalDamage), new(3110, Style.Tank, Need.PhysicalDamage),
        new(6333, Style.Fighter | Style.Lethality, Need.PhysicalDamage), new(6673, Style.Crit, Need.PhysicalDamage),

        new(3089, Style.Mage), new(4646, Style.Mage), new(4629, Style.Mage), new(4628, Style.Mage),
        new(3135, Style.Mage), new(6653, Style.Mage), new(4645, Style.Mage), new(3157, Style.Mage),
        new(3107, Style.Support), new(2065, Style.Support), new(3190, Style.Support),
        new(6616, Style.Support), new(3222, Style.Support), new(6617, Style.Support), new(6620, Style.Support),
        new(6665, Style.Tank), new(2502, Style.Tank), new(3065, Style.Tank),
        new(2504, Style.Tank), new(3083, Style.Tank), new(3109, Style.Tank), new(3190, Style.Tank),
        new(6610, Style.Fighter), new(3053, Style.Fighter), new(3161, Style.Fighter),
        new(6333, Style.Fighter), new(3071, Style.Fighter), new(6692, Style.Fighter | Style.Lethality),
        new(3142, Style.Lethality), new(6699, Style.Lethality), new(6696, Style.Lethality),
        new(3814, Style.Lethality), new(6694, Style.Lethality), new(3179, Style.Lethality),
        new(3031, Style.Crit), new(3036, Style.Crit), new(3072, Attackers), new(6673, Style.Crit),
        new(3094, Style.Crit), new(3046, Style.Crit), new(3139, Attackers),
        new(3124, Style.OnHit), new(3153, Style.OnHit), new(3302, Style.OnHit),
        new(3091, Style.OnHit), new(6672, Style.OnHit), new(3087, Style.OnHit)
    ];

    private static readonly HashSet<string> Healers = new(
        ["Aatrox", "Briar", "DrMundo", "Fiddlesticks", "Illaoi", "Nami", "Nilah", "Sona", "Soraka",
         "Swain", "Sylas", "Taric", "Trundle", "Vladimir", "Warwick", "Yuumi", "Zac"], StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> TeamHealers = new(
        ["Nami", "Sona", "Soraka", "Taric", "Yuumi"], StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> Shielders = new(
        ["Ivern", "Janna", "Karma", "Lulu", "Lux", "Milio", "Orianna", "Seraphine", "Shen", "Sona"],
        StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> Divers = new(
        ["Camille", "Diana", "Hecarim", "Irelia", "Jax", "Kled", "Nocturne", "Pantheon", "Rengar", "Vi", "Yone", "Zac"],
        StringComparer.OrdinalIgnoreCase);

    private readonly Ratings ratings;
    private readonly Dictionary<string, Champion> champions;
    private readonly Dictionary<int, MayhemItem> items;

    private MayhemBuilds(Ratings ratings, Dictionary<int, MayhemItem> items)
    {
        this.ratings = ratings;
        champions = ratings.Catalog.Champions.ToDictionary(champion => champion.Key, StringComparer.OrdinalIgnoreCase);
        this.items = items;
    }

    internal static MayhemBuilds Load(Ratings ratings, string assets)
    {
        try
        {
            var data = Ratings.Read<MayhemItemCatalog>(Path.Combine(assets, "mayhem-items.json"));
            if (data.Patch == ratings.Catalog.Patch && data.Items is { Count: > 0 })
                return new(ratings, data.Items);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { }
        return new(ratings, []);
    }

    internal EnemyDamageMix? EstimateEnemyDamage(MayhemMatch? match) => match is null ? null :
        EnemyDamageMix.Estimate(match.Opponents.Where(opponent => champions.ContainsKey(opponent.Champion)).Select(opponent =>
        {
            var equipped = EquippedItems(opponent).Where(item => item.Finished && !item.Boots).ToArray();
            return EnemyDamageMix.PhysicalShare(ratings, champions[opponent.Champion],
                equipped.Count(item => item.AttackDamage > 0 || item.Crit > 0), equipped.Count(item => item.AbilityPower > 0));
        }));

    internal bool HasStartingPurchase(MayhemMatch? match, OpGgBuildSnapshot? build)
    {
        if (match is null)
            return false;
        if (match.Items.Any(IsFinished))
            return true;

        var starters = build?.Groups.FirstOrDefault(group => group.Section == OpGgBuildSection.Start);
        return starters?.Choices.SelectMany(choice => choice)
            .Any(item => items.ContainsKey(item.Id) && match.Items.Contains(item.Id)) == true;
    }

    internal MayhemBuildPlan? Recommend(GameContext game, MayhemMatch? match, OpGgBuildSnapshot? build)
    {
        if (game.Mode != Mode.Caos || match is null || build is not { Mode: Mode.Caos } ||
            !string.Equals(build.Champion, game.Champion, StringComparison.OrdinalIgnoreCase) ||
            !champions.TryGetValue(game.Champion, out var champion) || items.Count == 0)
            return null;

        var owned = match.Items.Where(IsFinished)
            .DistinctBy(BaseId).Take(6).ToArray();
        var steps = owned.Select(id => Step(id, "Comprado", owned: true)).ToList();
        if (steps.Count == 6)
            return new(steps.ToArray());

        var paths = build.Groups.FirstOrDefault(group => group.Section == OpGgBuildSection.Core)?.Choices
            .Select(choice => choice.Select(item => BaseId(item.Id))
                .Where(id => items.GetValueOrDefault(id) is { Available: true, Boots: false }).Distinct().ToArray())
            .Where(choice => choice.Length >= 2).ToArray() ?? [];
        if (paths.Length == 0)
            return null;

        var core = paths.MaxBy(path => path.Intersect(owned.Select(BaseId)).Count())!;
        var style = BuildStyle(champion, core.Concat(owned).Distinct());
        var boot = build.Groups.FirstOrDefault(group => group.Section == OpGgBuildSection.Boots)?.Choices
            .SelectMany(choice => choice).Select(item => item.Id)
            .FirstOrDefault(id => items.GetValueOrDefault(id) is { Available: true, Boots: true }) ?? 0;
        foreach (var id in core.Take(1).Concat([boot]).Concat(core.Skip(1)))
        {
            if (steps.Count >= 4)
                break;
            if (CanAdd(id, steps))
                steps.Add(Step(id));
        }

        foreach (var threat in AssessThreats(match.Opponents))
        {
            if (steps.Count == 6)
                break;
            if (threat.Need == Need.Healing && steps.Any(step => IsAntiHeal(step.Item.Id)))
                continue;
            var pick = Choices.FirstOrDefault(choice => choice.Need == threat.Need &&
                (choice.Styles & style) != 0 && CanAdd(choice.Id, steps));
            if (pick is not null)
                steps.Add(Step(pick.Id, threat.Reason));
        }

        // Prefer published items from the alternative route when there is no relevant counter item.
        var alternatives = paths.Where(path => BuildStyle(champion, path) == style).SelectMany(path => path);
        var defaults = Choices.Where(choice => choice.Need == Need.General && (choice.Styles & style) != 0)
            .Select(choice => choice.Id);
        foreach (var id in alternatives.Concat(defaults))
        {
            if (steps.Count == 6)
                break;
            if (CanAdd(id, steps) && !items[id].Boots)
                steps.Add(Step(id, "Encaja con tu build"));
        }
        return steps.Count == 6 ? new(steps.ToArray()) : null;
    }

    private BuildStep Step(int id, string? detail = null, bool owned = false) => new(new(id, items[id].Name), detail, owned);
    private bool IsFinished(int id) => items.GetValueOrDefault(id)?.Finished == true;

    private bool CanAdd(int id, List<BuildStep> steps)
    {
        if (items.GetValueOrDefault(id) is not { Available: true } item)
            return false;
        var family = ExclusiveGroup(id);
        return !steps.Any(step => BaseId(step.Item.Id) == id ||
            (item.Boots && items[step.Item.Id].Boots) ||
            (family != 0 && ExclusiveGroup(step.Item.Id) == family)) &&
            (item.Crit == 0 || steps.Sum(step => items[step.Item.Id].Crit) < 1);
    }

    private Style BuildStyle(Champion champion, IEnumerable<int> build)
    {
        var ids = build.ToArray();
        var equipment = ids.Select(id => items[id]).Where(item => !item.Boots).ToArray();
        var role = champion.Roles[0];
        if (ids.Count(id => id is 2065 or 3107 or 3109 or 3190 or 3222 or 3504 or 4005 or 6616 or 6617 or 6620 or 6621) >= 2)
            return Style.Support;
        if (equipment.Count(item => item.Armor + item.MagicResist >= 45 && item.AbilityPower == 0 && item.AttackDamage == 0) >= 2)
            return Style.Tank;
        if (equipment.Any(item => item.Crit > 0) && (role == "Marksman" || equipment.Count(item => item.Crit > 0) >= 2))
            return Style.Crit;
        if (ids.Any(id => id is 3124 or 3302) || equipment.Count(item => item.OnHit && item.AttackSpeed > 0) >= 2)
            return Style.OnHit;
        if (equipment.Sum(item => item.AbilityPower) >= 70 && equipment.Sum(item => item.AttackDamage) < 65)
            return Style.Mage;
        if (ids.Any(id => id is 3142 or 3179 or 3814 or 6695 or 6696 or 6698 or 6699) || role == "Assassin")
            return Style.Lethality;
        return role switch
        {
            "Mage" => Style.Mage, "Support" => Style.Support, "Tank" => Style.Tank,
            "Marksman" => Style.OnHit, _ => Style.Fighter
        };
    }

    private IEnumerable<Threat> AssessThreats(MayhemOpponent[] opponents)
    {
        var rivals = opponents.Where(opponent => champions.ContainsKey(opponent.Champion)).Select(ReadOpponent).ToArray();
        var threats = new List<Threat>();
        int magicResistance = rivals.Count(rival => rival.MagicResist >= 20);
        int armor = rivals.Count(rival => rival.Armor >= 20);
        if (magicResistance >= 2 || rivals.Any(rival => rival.MagicResist >= 65))
            threats.Add(new(Need.MagicResist, 7 + magicResistance, PurchaseReason(magicResistance, "resistencia mágica")));
        if (armor >= 2 || rivals.Any(rival => rival.Armor >= 65))
            threats.Add(new(Need.Armor, 7 + armor, PurchaseReason(armor, "armadura")));

        var healers = rivals.Where(rival => rival.Sustain || Healers.Contains(rival.Champion.Key)).ToArray();
        if (healers.Length >= 2 || healers.Any(rival => TeamHealers.Contains(rival.Champion.Key)))
            threats.Add(new(Need.Healing, 7 + healers.Length, ChampionReason("Para reducir la curación de", healers)));
        int shields = rivals.Count(rival => rival.Shields);
        if (shields >= 2)
            threats.Add(new(Need.Shields, 6 + shields, "Contra rivales con escudos"));
        var divers = rivals.Where(rival => rival.Dive).ToArray();
        if (divers.Length > 0)
            threats.Add(new(Need.Dive, 5 + divers.Length, ChampionReason("Para resistir las entradas de", divers)));
        int health = rivals.Count(rival => rival.Health >= 500);
        if (health >= 2)
            threats.Add(new(Need.Health, 5 + health, PurchaseReason(health, "vida")));
        else if (rivals.Count(rival => rival.Champion.Roles[0] == "Tank") >= 2)
            threats.Add(new(Need.Health, 6, "Contra varios tanques rivales"));
        if (rivals.Count(rival => rival.MagicDamage) >= 3)
            threats.Add(new(Need.MagicDamage, 6, "Contra el daño mágico rival"));
        if (rivals.Count(rival => rival.PhysicalDamage) >= 3)
            threats.Add(new(Need.PhysicalDamage, 6, "Contra el daño físico rival"));
        return threats.OrderByDescending(threat => threat.Priority);
    }

    private Opponent ReadOpponent(MayhemOpponent opponent)
    {
        var champion = champions[opponent.Champion];
        var equipped = EquippedItems(opponent).ToArray();
        var ap = equipped.Sum(item => item.AbilityPower);
        var ad = equipped.Sum(item => item.AttackDamage);
        bool mage = champion.Roles.Contains("Mage");
        bool magic = (ap >= 40 && ap > ad) || (ad < 30 && mage);
        bool physical = (ad >= 30 && ad >= ap) ||
            (ap < 40 && !mage && champion.Roles[0] is "Marksman" or "Fighter" or "Assassin");
        return new(champion, equipped.Sum(item => item.Armor), equipped.Sum(item => item.MagicResist),
            equipped.Sum(item => item.Health), equipped.Any(item => item.Sustain),
            Shielders.Contains(champion.Key) || opponent.Items.Any(id => id is 3053 or 3156 or 3190 or 6673), magic, physical,
            champion.Roles[0] == "Assassin" || Divers.Contains(champion.Key));
    }

    private IEnumerable<MayhemItem> EquippedItems(MayhemOpponent opponent) =>
        opponent.Items.Select(id => items.GetValueOrDefault(id)).OfType<MayhemItem>();

    private static string PurchaseReason(int count, string stat) => count == 1
        ? $"Contra 1 rival con objetos de {stat}" : $"Contra {count} rivales con objetos de {stat}";

    private static string ChampionReason(string label, Opponent[] opponents) =>
        $"{label} {string.Join(" y ", opponents.Take(2).Select(rival => rival.Champion.Name))}";

    private static int BaseId(int id) => id switch
    {
        3040 => 3003, 3042 => 3004, 3121 => 3119, 2530 => 2526, _ => id
    };

    private static bool IsAntiHeal(int id) => id is 3033 or 3075 or 3123 or 3165 or 3916 or 6609 or 3076;

    private static int ExclusiveGroup(int id) => BaseId(id) switch
    {
        3033 or 3036 or 6694 or 3302 => 1,
        3135 or 3137 or 8010 => 2,
        3053 or 3156 or 6673 => 3,
        3003 or 3004 or 3119 or 2526 => 4,
        3074 or 3748 or 6631 or 6698 => 5,
        _ => 0
    };
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Aumentos;

internal sealed record BuildStep(OpGgBuildItem Item, string? Detail = null, bool Owned = false);
internal sealed record MayhemBuildPlan(BuildStep[] Steps, BuildSwap? Swap = null, bool IsLocalRoute = false);

internal sealed record MayhemItem(string Name, bool Finished, bool Available, bool Boots, bool OnHit, bool Sustain,
    int Armor, int MagicResist, int Health, int AttackDamage, int AbilityPower, decimal Crit, decimal AttackSpeed);
internal sealed record MayhemItemCatalog(string Patch, Dictionary<int, MayhemItem> Items)
{
    internal static Dictionary<int, MayhemItem> Load(string patch, string assets)
    {
        try
        {
            var data = Ratings.Read<MayhemItemCatalog>(Path.Combine(assets, "mayhem-items.json"));
            if (data.Patch == patch && data.Items is { Count: > 0 })
                return data.Items;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { }
        return [];
    }
}

internal sealed class MayhemBuilds
{
    [Flags]
    private enum Style { Mage = 1, Support = 2, Tank = 4, Fighter = 8, Lethality = 16, Crit = 32, OnHit = 64 }

    private enum Need { General, MagicResist, Armor, Healing, Shields, Health, Dive, CriticalHits, MagicDamage, PhysicalDamage }
    private sealed record Choice(int Id, Style Styles, Need Need = Need.General);
    private sealed record Threat(Need Need, int Priority, string Reason);
    private sealed record Opponent(Champion Champion, int Armor, int MagicResist, int Health, decimal Crit,
        bool Sustain, bool Shields, bool MagicDamage, bool PhysicalDamage, bool Dive, MayhemScore? Score, Need? DamageNeed);

    private const Style Attackers = Style.Crit | Style.OnHit;
    private const Style Physical = Style.Fighter | Style.Lethality | Attackers;
    internal const string PublishedCore = "Núcleo de OP.GG";
    private const int LeadingRivalPriority = int.MaxValue;
    private const double MinimumMagicDamageGain = 0.15;
    private const double VoidStaffResistanceMultiplier = 0.60;
    private const double BlightingJewelResistanceMultiplier = 0.87;

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
        new(3157, Style.Mage, Need.CriticalHits), new(3143, Style.Tank | Style.Fighter | Style.Support, Need.CriticalHits),
        new(3157, Style.Mage, Need.PhysicalDamage), new(3190, Style.Support, Need.PhysicalDamage),
        new(2502, Style.Tank, Need.PhysicalDamage), new(3110, Style.Tank, Need.PhysicalDamage),
        new(6333, Style.Fighter | Style.Lethality, Need.PhysicalDamage), new(6673, Style.Crit, Need.PhysicalDamage),

        new(3089, Style.Mage), new(4646, Style.Mage), new(4629, Style.Mage), new(4628, Style.Mage),
        new(6653, Style.Mage), new(4645, Style.Mage), new(3157, Style.Mage),
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

    internal MayhemBuilds(Ratings ratings, Dictionary<int, MayhemItem> items)
    {
        this.ratings = ratings;
        champions = ratings.Catalog.Champions.ToDictionary(champion => champion.Key, StringComparer.OrdinalIgnoreCase);
        this.items = items;
    }

    internal static bool IsDiver(Champion champion) => champion.Roles[0] == "Assassin" || Divers.Contains(champion.Key);

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
        if (game.Mode != Mode.Caos || match is null ||
            !champions.TryGetValue(game.Champion, out var champion) || items.Count == 0)
            return null;

        var owned = match.Items.Where(IsFinished)
            .DistinctBy(BaseId).Take(6).ToArray();
        var steps = owned.Select(id => Step(id, "Comprado", owned: true)).ToList();
        bool worthMagicPenetration = CanBenefitFromMagicPenetration(match);
        var source = build is { Mode: Mode.Caos } && string.Equals(build.Champion, game.Champion, StringComparison.OrdinalIgnoreCase)
            ? build : null;
        var paths = source?.Groups.FirstOrDefault(group => group.Section == OpGgBuildSection.Core)?.Choices
            .Select(choice => choice.Select(item => BaseId(item.Id))
                .Where(id => items.GetValueOrDefault(id) is { Available: true, Boots: false }).Distinct().ToArray())
            .Where(choice => choice.Length >= 2).ToArray() ?? [];
        bool localRoute = paths.Length == 0;
        var core = paths.MaxBy(path => path.Intersect(owned.Select(BaseId)).Count()) ?? [];
        var style = BuildStyle(champion, core.Concat(owned).Distinct());
        if (steps.Count == 6)
        {
            var equipped = steps.ToArray();
            return new(equipped, RecommendSwap(equipped, core, style, match, worthMagicPenetration), localRoute);
        }
        var purchaseThreats = AssessThreats(match.Opponents, purchasesOnly: true, prioritizeCarry: owned.Length == 5).ToArray();
        int coreLimit = 4;
        if (owned.Length >= 2)
        {
            foreach (var threat in purchaseThreats)
            {
                if (!AddCounter(threat))
                    continue;
                coreLimit++;
                break;
            }
        }

        var boot = source?.Groups.FirstOrDefault(group => group.Section == OpGgBuildSection.Boots)?.Choices
            .SelectMany(choice => choice).Select(item => item.Id)
            .FirstOrDefault(id => items.GetValueOrDefault(id) is { Available: true, Boots: true }) ?? 0;
        if (localRoute)
        {
            int firstItem = LocalFirstItem(style);
            if (owned.All(id => items[id].Boots) && CanAdd(firstItem, steps, worthMagicPenetration))
                steps.Add(Step(firstItem));
            if (steps.Count < coreLimit && champion.Key != "Cassiopeia" && !owned.Any(id => items[id].Boots))
            {
                int localBoot = boot != 0 ? boot : LocalBoot(style, EstimateEnemyDamage(match));
                if (CanAdd(localBoot, steps, worthMagicPenetration))
                    steps.Add(Step(localBoot, boot != 0 ? "Botas de OP.GG" : null));
            }
        }
        else
        {
            foreach (var id in core.Take(1).Concat([boot]).Concat(core.Skip(1)))
            {
                if (steps.Count >= coreLimit)
                    break;
                if (CanAdd(id, steps, worthMagicPenetration))
                    steps.Add(Step(id, id == boot ? "Botas de OP.GG" : PublishedCore));
            }
        }

        var inferredThreats = AssessThreats(match.Opponents);
        foreach (var threat in purchaseThreats.Concat(inferredThreats).DistinctBy(threat => threat.Need))
        {
            if (steps.Count == 6)
                break;
            AddCounter(threat);
        }

        // Prefer published items from the alternative route when there is no relevant counter item.
        var alternatives = paths.Where(path => BuildStyle(champion, path) == style).SelectMany(path => path).ToArray();
        var defaults = Choices.Where(choice => choice.Need == Need.General && (choice.Styles & style) != 0)
            .Select(choice => choice.Id);
        if (localRoute)
            defaults = defaults.Prepend(LocalFirstItem(style));

        foreach (var id in alternatives.Concat(defaults))
        {
            if (steps.Count == 6)
                break;
            if (CanAdd(id, steps, worthMagicPenetration) && !items[id].Boots)
                steps.Add(Step(id, alternatives.Contains(id) ? PublishedCore : null));
        }
        return steps.Count == 6 || (localRoute && steps.Count > 0) ? new(steps.ToArray(), IsLocalRoute: localRoute) : null;

        bool AddCounter(Threat threat)
        {
            if (steps.Any(step => Covers(step.Item.Id, threat.Need)))
                return false;
            var pick = Choices.FirstOrDefault(choice => choice.Need == threat.Need &&
                (choice.Styles & style) != 0 && CanAdd(choice.Id, steps, worthMagicPenetration));
            if (pick is null)
                return false;
            steps.Add(Step(pick.Id, threat.Reason));
            return true;
        }
    }

    private static int LocalFirstItem(Style style) => style switch
    {
        Style.Mage => 6653,
        Style.Support => 6617,
        Style.Tank => 3084,
        Style.Lethality => 3142,
        Style.Crit => 3032,
        Style.OnHit => 3153,
        _ => 3078
    };

    private static int LocalBoot(Style style, EnemyDamageMix? damage) => style switch
    {
        Style.Mage => 3020,
        Style.Support => 3158,
        Style.Crit or Style.OnHit => 3006,
        Style.Lethality => 3158,
        _ => damage is { PhysicalPercent: >= 60 } ? 3047 : 3111
    };

    private BuildSwap? RecommendSwap(BuildStep[] equipped, int[] core, Style style, MayhemMatch match,
        bool worthMagicPenetration)
    {
        var replaceable = equipped.Where(step =>
        {
            int id = step.Item.Id;
            var item = items[id];
            return id == BaseId(id) && item is { Available: true, Boots: false } && !core.Take(2).Contains(id) &&
                !AmplifiesBuild(id, style) &&
                (core.Length > 0 || IsAntiHeal(id) || Situational(id) && (item.Armor >= 30 || item.MagicResist >= 30));
        }).OrderBy(step => core.Contains(step.Item.Id))
            .ThenByDescending(step => Situational(step.Item.Id)).ThenBy(step => step.Item.Id);

        bool teamHasAntiHeal = match.Allies?.Any(ally => ally.Items.Any(AppliesAntiHealOnDamage)) == true;
        var threats = AssessThreats(match.Opponents, purchasesOnly: true, prioritizeCarry: true)
            .Where(threat => threat.Need != Need.Healing || !teamHasAntiHeal).ToArray();
        bool magicPressure = threats.Any(threat => threat.Need == Need.MagicDamage);
        bool physicalPressure = threats.Any(threat => threat.Need is Need.PhysicalDamage or Need.CriticalHits);
        bool leadingTeam = match.Score is { Pressure: >= 10 } score && match.Allies is { Length: > 0 } allies &&
            allies.All(ally => ally.Score is { } other && score.Pressure > other.Pressure);
        int resistanceLossLimit = leadingTeam ? 20 : 30;
        var counters = threats.Select(threat => new SwapCounter(threat.Reason,
            Choices.Where(choice => choice.Need == threat.Need && (choice.Styles & style) != 0 && IsFinished(choice.Id))
                .Select(choice => choice.Id).ToArray(),
            id => Covers(id, threat.Need)));
        return BuildSwap.Find(equipped, replaceable, counters, (id, kept) => CanAdd(id, kept, worthMagicPenetration),
            id => new(id, items[id].Name), PreservesDefense);

        bool PreservesDefense(int outgoing, int incoming)
        {
            var removed = items[outgoing];
            var added = items[incoming];
            int magicChange = added.MagicResist - removed.MagicResist;
            int armorChange = added.Armor - removed.Armor;
            if (magicPressure && magicChange <= -resistanceLossLimit || physicalPressure && armorChange <= -resistanceLossLimit)
                return false;

            // A tank already leading the team should give up a substantial amount of health only for a clear defensive gain.
            return !leadingTeam || style != Style.Tank || removed.Health - added.Health < 200 ||
                magicPressure && magicChange >= 30 || physicalPressure && armorChange >= 30;
        }
    }

    private static bool Situational(int id) => Choices.Any(choice => choice.Id == id && choice.Need != Need.General);

    private static bool AmplifiesBuild(int id, Style style) => (id, style) is
        (3089, Style.Mage) or (3031, Style.Crit) or (3124, Style.OnHit);

    private bool Covers(int id, Need need)
    {
        var item = items[id];
        if (item.Boots)
            return false;
        return need switch
        {
            Need.Healing => IsAntiHeal(id),
            Need.Armor when id == 3033 => true,
            Need.MagicDamage when item.MagicResist >= 30 => true,
            Need.PhysicalDamage when item.Armor >= 30 => true,
            _ => Choices.Any(choice => choice.Id == BaseId(id) && choice.Need == need)
        };
    }

    private BuildStep Step(int id, string? detail = null, bool owned = false) => new(new(id, items[id].Name), detail, owned);
    private bool IsFinished(int id) => items.GetValueOrDefault(id)?.Finished == true;

    private bool CanAdd(int id, IReadOnlyList<BuildStep> steps, bool worthMagicPenetration)
    {
        if ((!worthMagicPenetration && GrantsPercentMagicPenetration(id)) ||
            items.GetValueOrDefault(id) is not { Available: true } item)
            return false;
        var family = ExclusiveGroup(id);
        return !steps.Any(step => BaseId(step.Item.Id) == id ||
            (item.Boots && items[step.Item.Id].Boots) ||
            (family != 0 && ExclusiveGroup(step.Item.Id) == family)) &&
            (item.Crit == 0 || steps.Sum(step => items[step.Item.Id].Crit) < 1);
    }

    private bool CanBenefitFromMagicPenetration(MayhemMatch match)
    {
        if (match.MagicPenetration is not { } pen || (pen.Remaining >= 1 && pen.Flat < 20) ||
            match.Opponents.Length == 0 || match.Items.Any(GrantsPercentMagicPenetration))
            return true;

        // Void Staff is the upper bound. Its recipe replaces any Blighting Jewel already equipped.
        double upgradedRemaining = match.Items.Contains(4630)
            ? Math.Min(1, pen.Remaining / BlightingJewelResistanceMultiplier) * VoidStaffResistanceMultiplier
            : pen.Remaining * VoidStaffResistanceMultiplier;
        foreach (var opponent in match.Opponents)
        {
            if (!champions.TryGetValue(opponent.Champion, out var champion) ||
                champion.MagicResist is not { } initial || champion.MagicResistPerLevel is not { } growth ||
                opponent.Level is not { } level)
                return true;

            int levelsGained = level - 1;
            double baseResistance = initial + growth * levelsGained * (0.7025 + 0.0175 * levelsGained);
            double resistance = baseResistance + EquippedItems(opponent).Sum(item => item.MagicResist);
            double current = Math.Max(0, resistance * pen.Remaining - pen.Flat);
            double withVoidStaff = Math.Max(0, resistance * upgradedRemaining - pen.Flat);
            if ((current - withVoidStaff) / (100 + withVoidStaff) >= MinimumMagicDamageGain)
                return true;
        }
        return false;
    }

    private static bool GrantsPercentMagicPenetration(int id) => BaseId(id) is 3135 or 3137;

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

    private IEnumerable<Threat> AssessThreats(MayhemOpponent[] opponents, bool purchasesOnly = false, bool prioritizeCarry = false)
    {
        var rivals = opponents.Where(opponent => champions.ContainsKey(opponent.Champion))
            .Select(opponent => ReadOpponent(opponent, purchasesOnly)).ToArray();
        var threats = new List<Threat>();
        int magicResistance = rivals.Count(rival => rival.MagicResist >= 20);
        int armor = rivals.Count(rival => rival.Armor >= 20);
        if (magicResistance >= 2 || rivals.Any(rival => rival.MagicResist >= 65))
            threats.Add(new(Need.MagicResist, 7 + magicResistance, PurchaseReason(magicResistance, "resistencia mágica")));
        if (armor >= 2 || rivals.Any(rival => rival.Armor >= 65))
            threats.Add(new(Need.Armor, 7 + armor, PurchaseReason(armor, "armadura")));

        var healers = rivals.Where(rival => rival.Sustain || !purchasesOnly && Healers.Contains(rival.Champion.Key)).ToArray();
        if (healers.Length >= 2 || healers.Any(rival => TeamHealers.Contains(rival.Champion.Key)))
            threats.Add(new(Need.Healing, 7 + healers.Length, ChampionReason("Para reducir la curación de", healers)));
        int shields = rivals.Count(rival => rival.Shields);
        if (shields >= 2)
            threats.Add(new(Need.Shields, 6 + shields, "Contra rivales con escudos"));
        var divers = rivals.Where(rival => rival.Dive).ToArray();
        if (!purchasesOnly && divers.Length > 0)
            threats.Add(new(Need.Dive, 5 + divers.Length, ChampionReason("Para resistir las entradas de", divers)));
        int health = rivals.Count(rival => rival.Health >= 500);
        if (health >= 2)
            threats.Add(new(Need.Health, 5 + health, PurchaseReason(health, "vida")));
        else if (!purchasesOnly && rivals.Count(rival => rival.Champion.Roles[0] == "Tank") >= 2)
            threats.Add(new(Need.Health, 6, "Contra varios tanques rivales"));
        var critical = rivals.Where(rival => rival.Crit >= 0.5m).OrderByDescending(rival => rival.Crit).ToArray();
        if (purchasesOnly && (critical.Length >= 2 || critical.Any(rival => rival.Crit >= 0.75m)))
            threats.Add(new(Need.CriticalHits, 7 + critical.Length, ChampionReason("Contra los golpes críticos de", critical)));
        int damageThreshold = purchasesOnly ? 2 : 3;
        var magic = rivals.Where(rival => rival.MagicDamage).ToArray();
        var physical = rivals.Where(rival => rival.PhysicalDamage).ToArray();
        if (magic.Length >= damageThreshold)
            threats.Add(new(Need.MagicDamage, 4 + magic.Length,
                ChampionReason(purchasesOnly ? "Por el AP comprado por" : "Contra el daño mágico de", magic)));
        if (physical.Length >= damageThreshold)
            threats.Add(new(Need.PhysicalDamage, 4 + physical.Length,
                ChampionReason(purchasesOnly ? "Contra las compras de daño físico de" : "Contra el daño físico de", physical)));
        if (prioritizeCarry)
            PrioritizeLeadingRival(rivals, threats);
        return threats.OrderByDescending(threat => threat.Priority);
    }

    private static void PrioritizeLeadingRival(Opponent[] rivals, List<Threat> threats)
    {
        if (rivals.Length < 2 || rivals.Any(rival => rival.Score is null))
            return;

        var ranked = rivals.Select(rival => (Rival: rival, Pressure: rival.Score!.Pressure))
            .OrderByDescending(entry => entry.Pressure).ToArray();
        var leader = ranked[0];
        if (leader.Pressure < 6 || leader.Rival.DamageNeed is not { } need)
            return;
        double margin = Math.Max(4, leader.Pressure / 4);
        var leaders = ranked.TakeWhile(entry => leader.Pressure - entry.Pressure < margin).ToArray();
        if (leaders.Length == ranked.Length || leaders.Any(entry => entry.Rival.DamageNeed != need || entry.Rival.Score!.Kills < 3))
            return;

        // Keep the other defense when rivals of that damage type are also doing well.
        bool magicLeader = need == Need.MagicDamage;
        var secondary = magicLeader ? Need.PhysicalDamage : Need.MagicDamage;
        bool secondaryThreat = ranked.Skip(leaders.Length).Any(entry =>
            (magicLeader ? entry.Rival.PhysicalDamage : entry.Rival.MagicDamage) && entry.Pressure >= Math.Max(3, leader.Pressure / 2));
        threats.RemoveAll(threat => threat.Need == need || !secondaryThreat &&
            (threat.Need == secondary || magicLeader && threat.Need == Need.CriticalHits));
        var damage = magicLeader ? "mágico" : "físico";
        var reason = leaders.Length == 1 ? $"Por la ventaja de {leader.Rival.Champion.Name} y su daño {damage}" :
            $"{ChampionReason($"Por el daño {damage} de", leaders.Select(entry => entry.Rival).ToArray())}, que llevan ventaja";
        threats.Add(new(need, LeadingRivalPriority, reason));
    }

    private Opponent ReadOpponent(MayhemOpponent opponent, bool purchasesOnly)
    {
        var champion = champions[opponent.Champion];
        var itemIds = purchasesOnly ? opponent.Items.Where(id => items.GetValueOrDefault(id) is { Finished: true, Boots: false }) :
            opponent.Items.AsEnumerable();
        var purchases = itemIds.ToArray();
        var equipped = purchases.Select(id => items.GetValueOrDefault(id)).OfType<MayhemItem>().ToArray();
        var ap = equipped.Sum(item => item.AbilityPower);
        var ad = equipped.Sum(item => item.AttackDamage);
        var crit = equipped.Sum(item => item.Crit);
        bool mage = champion.Roles.Contains("Mage");
        bool magic = (ap >= 40 && ap > ad) || (!purchasesOnly && ad < 30 && mage);
        bool physical = (ad >= 30 && ad >= ap) ||
            (!purchasesOnly && ap < 40 && !mage && champion.Roles[0] is "Marksman" or "Fighter" or "Assassin");
        Need? damageNeed = null;
        if (purchasesOnly)
        {
            int magicItems = equipped.Count(item => item.AbilityPower > 0);
            int physicalItems = equipped.Count(item => item.AttackDamage > 0 || item.Crit > 0);
            magic = ap >= 40 && magicItems >= physicalItems;
            physical = (ad >= 30 || crit >= 0.5m) && physicalItems >= magicItems;
            damageNeed = magic && !physical && magicItems >= 2 ? Need.MagicDamage :
                physical && !magic && physicalItems >= 2 ? Need.PhysicalDamage : null;
        }
        var defenses = equipped.Where(item => !item.Boots).ToArray();
        return new(champion, defenses.Sum(item => item.Armor), defenses.Sum(item => item.MagicResist),
            equipped.Sum(item => item.Health), crit, equipped.Any(item => item.Sustain),
            (!purchasesOnly && Shielders.Contains(champion.Key)) || purchases.Any(id => id is 3053 or 3156 or 3190 or 6673), magic, physical,
            IsDiver(champion), opponent.Score, damageNeed);
    }

    private IEnumerable<MayhemItem> EquippedItems(MayhemOpponent opponent) =>
        opponent.Items.Select(id => items.GetValueOrDefault(id)).OfType<MayhemItem>();

    private static string PurchaseReason(int count, string stat) => count == 1
        ? $"Contra 1 rival con objetos de {stat}" : $"Contra {count} rivales con objetos de {stat}";

    private static string ChampionReason(string label, Opponent[] opponents) =>
        $"{label} {string.Join(" y ", opponents.Take(2).Select(rival => rival.Champion.Name))}";

    internal static int BaseId(int id) => id switch
    {
        3040 => 3003, 3042 => 3004, 3121 => 3119, 2530 => 2526, 773135 => 3135, _ => id
    };

    private static bool AppliesAntiHealOnDamage(int id) => id is 3033 or 3123 or 3165 or 3916 or 6609;

    private static bool IsAntiHeal(int id) => AppliesAntiHealOnDamage(id) || id is 3075 or 3076;

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

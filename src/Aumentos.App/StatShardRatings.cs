using System;
using System.Collections.Generic;
using System.Linq;

namespace Aumentos;

internal enum StatShard
{
    AttackDamage, Haste, AbilityPower, Armor, AttackSpeed, Crit, Health, Swiftness, Power, Resistances,
    Lethality, MagicPenetration, MagicResist, Random, CritDamage, Fortune, Care, HealthAndSize,
    Movement, Omnivamp, Shardholder, Healing, Tenacity
}

internal sealed class StatShardRatings(Champion[] champions, Dictionary<string, HashSet<string>> profiles)
{
    // Titles from Riot's es_MX client strings. ARAM Caos shares the descriptions of these stats.
    private static readonly Dictionary<StatShard, string> Names = new()
    {
        [StatShard.AttackDamage] = "Fragmento de daño de ataque",
        [StatShard.Haste] = "Fragmento de aceleración de habilidad",
        [StatShard.AbilityPower] = "Fragmento de poder de habilidad",
        [StatShard.Armor] = "Fragmento de armadura",
        [StatShard.AttackSpeed] = "Fragmento de velocidad de ataque",
        [StatShard.Crit] = "Fragmento de golpe crítico",
        [StatShard.Health] = "Fragmento de Vida",
        [StatShard.Swiftness] = "Fragmento de velocidad",
        [StatShard.Power] = "Fragmento de poder",
        [StatShard.Resistances] = "Fragmento Inquebrantable",
        [StatShard.Lethality] = "Fragmento de letalidad",
        [StatShard.MagicPenetration] = "Fragmento de penetración de magia",
        [StatShard.MagicResist] = "Fragmento de resistencia mágica",
        [StatShard.Random] = "Dos fragmentos aleatorios",
        [StatShard.CritDamage] = "Fragmento de daño de golpe crítico",
        [StatShard.Fortune] = "Fragmento de Fortuna",
        [StatShard.Care] = "Paquete de Cuidados",
        [StatShard.HealthAndSize] = "Fragmento de vida y tamaño",
        [StatShard.Movement] = "Fragmento de velocidad de movimiento",
        [StatShard.Omnivamp] = "Fragmento de omnivampirismo",
        [StatShard.Shardholder] = "Fragmento de valor del portafragmento",
        [StatShard.Healing] = "Fragmento de poder de curaciones y escudo",
        [StatShard.Tenacity] = "Fragmento de tenacidad"
    };

    private static readonly Dictionary<string, StatShard> Titles = Names.ToDictionary(
        pair => Ratings.Normalize(pair.Value), pair => pair.Key, StringComparer.Ordinal);
    private readonly Dictionary<string, Champion> roster = champions.ToDictionary(champion => champion.Key, StringComparer.OrdinalIgnoreCase);

    internal static string Name(StatShard shard) => Names[shard];

    internal static StatShard? Recognize(string text)
    {
        var normalized = Ratings.Normalize(text);
        if (Titles.TryGetValue(normalized, out var exact))
            return exact;
        var space = normalized.IndexOf(' ');
        if (space is < 7 or > 11 || Ratings.Distance(normalized[..space], "fragmento") > 2)
            return null;
        var nearest = Titles.Where(pair => pair.Key.StartsWith("fragmento ", StringComparison.Ordinal) &&
                Math.Abs(pair.Key.Length - normalized.Length) <= 3)
            .Select(pair => (Shard: pair.Value, Distance: Ratings.Distance(normalized, pair.Key)))
            .Where(pair => pair.Distance <= 3).OrderBy(pair => pair.Distance).Take(2).ToArray();
        return nearest.Length > 0 && (nearest.Length == 1 || nearest[0].Distance < nearest[1].Distance)
            ? nearest[0].Shard : null;
    }

    internal static StatShard?[]? Identify(DetectionResult offer) => offer.PanelVisible &&
        offer.Shards is { } shards && shards.Count(shard => shard.HasValue) >= 2 &&
        !offer.Cards.Any(card => card.HasValue) && !offer.Items.Any(item => item) ? shards : null;

    internal CardComparison?[]? ForOffer(GameContext game, DetectionResult offer)
    {
        if (Identify(offer) is not { } shards ||
            !profiles.TryGetValue(game.Champion, out var profile) || !roster.TryGetValue(game.Champion, out var champion))
            return null;
        return ArenaChoiceRating.Badges(shards.Select(shard => shard.HasValue ? Rate(shard.Value, champion, profile) : null).ToArray());
    }

    private static ArenaChoiceRating Rate(StatShard shard, Champion champion, HashSet<string> profile)
    {
        bool durable = profile.Contains("durability");
        bool control = profile.Contains("control");
        bool casting = profile.Contains("casting");
        bool ap = profile.Contains("ap");
        bool attacks = profile.Contains("attacks");
        bool ad = champion.Roles[0] is "Fighter" or "Marksman" || attacks ||
            (champion.Roles[0] == "Assassin" && !ap);
        bool crit = profile.Contains("crit");
        bool heals = profile.Contains("ally") || profile.Contains("resistheals");
        var (tier, reason) = shard switch
        {
            StatShard.Haste when durable && control => (Tier.S, "Control disponible más seguido"),
            StatShard.Haste => (casting || control ? Tier.A : Tier.B, "Reduce el enfriamiento de habilidades"),
            StatShard.Health or StatShard.HealthAndSize => (durable ? Tier.A : Tier.B, "Más vida para resistir daño"),
            StatShard.Armor => (durable ? Tier.A : Tier.B, "Mitiga daño físico"),
            StatShard.MagicResist => (durable ? Tier.A : Tier.B, "Mitiga daño mágico"),
            StatShard.Resistances => (durable ? Tier.A : Tier.B, "Protección contra daño físico y mágico"),
            StatShard.AttackDamage => (ad ? Tier.A : Tier.C, "Potencia el daño de ataque"),
            StatShard.Power => (ap && ad ? Tier.A : ap || ad ? Tier.B : Tier.C, "Daño de ataque y poder de habilidad"),
            StatShard.AbilityPower => (ap ? Tier.A : Tier.C, "Beneficia habilidades que escalan con PH"),
            StatShard.AttackSpeed => (attacks ? Tier.A : Tier.C, "Más ataques básicos por segundo"),
            StatShard.Swiftness => (attacks && casting ? Tier.A : attacks || casting || control ? Tier.B : Tier.C,
                "Velocidad de ataque y aceleración"),
            StatShard.Crit or StatShard.CritDamage => (crit ? Tier.A : attacks ? Tier.B : Tier.C,
                "Potencia una build de críticos"),
            StatShard.Lethality => (ad && profile.Contains("burst") ? Tier.A : ad ? Tier.B : Tier.C, "Penetración para daño físico"),
            StatShard.MagicPenetration => (ap ? Tier.A : Tier.C, "Penetración para daño mágico"),
            StatShard.Healing => (heals ? Tier.A : profile.Contains("sustain") ? Tier.B : Tier.C, "Curaciones y escudos más fuertes"),
            StatShard.Omnivamp => (profile.Contains("sustain") || attacks ? Tier.A : Tier.B, "Recuperas vida al infligir daño"),
            StatShard.Tenacity => (durable ? Tier.A : Tier.B, "Acorta el control que recibes"),
            StatShard.Movement => (profile.Contains("mobility") || control ? Tier.A : Tier.B, "Ayuda a entrar o reposicionarte"),
            StatShard.Shardholder => (Tier.B, "Mejora los demás fragmentos"),
            StatShard.Fortune => (Tier.B, "Oro ahora y en cada ronda"),
            StatShard.Care => (Tier.B, "Oro según las rondas perdidas"),
            StatShard.Random => (Tier.B, "Dos estadísticas aleatorias"),
            _ => throw new ArgumentOutOfRangeException(nameof(shard))
        };
        return new(Name(shard), tier, reason);
    }
}

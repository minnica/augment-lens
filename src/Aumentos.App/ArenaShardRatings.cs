using System;
using System.Collections.Generic;
using System.Linq;

namespace Aumentos;

internal enum ArenaShard
{
    AttackDamage, Haste, AbilityPower, Armor, AttackSpeed, Crit, Health, Swiftness, Power, Resistances,
    Lethality, MagicPenetration, MagicResist, Random, CritDamage, Fortune, Care, HealthAndSize,
    Movement, Omnivamp, Shardholder, Healing, Tenacity
}

internal sealed class ArenaShardRatings(Champion[] champions, Dictionary<string, HashSet<string>> profiles)
{
    // Titles from the cherry_*statanvil_*_name entries in Riot's es_MX client strings.
    private static readonly Dictionary<ArenaShard, string> Names = new()
    {
        [ArenaShard.AttackDamage] = "Fragmento de daño de ataque",
        [ArenaShard.Haste] = "Fragmento de aceleración de habilidad",
        [ArenaShard.AbilityPower] = "Fragmento de poder de habilidad",
        [ArenaShard.Armor] = "Fragmento de armadura",
        [ArenaShard.AttackSpeed] = "Fragmento de velocidad de ataque",
        [ArenaShard.Crit] = "Fragmento de golpe crítico",
        [ArenaShard.Health] = "Fragmento de Vida",
        [ArenaShard.Swiftness] = "Fragmento de velocidad",
        [ArenaShard.Power] = "Fragmento de poder",
        [ArenaShard.Resistances] = "Fragmento Inquebrantable",
        [ArenaShard.Lethality] = "Fragmento de letalidad",
        [ArenaShard.MagicPenetration] = "Fragmento de penetración de magia",
        [ArenaShard.MagicResist] = "Fragmento de resistencia mágica",
        [ArenaShard.Random] = "Dos fragmentos aleatorios",
        [ArenaShard.CritDamage] = "Fragmento de daño de golpe crítico",
        [ArenaShard.Fortune] = "Fragmento de Fortuna",
        [ArenaShard.Care] = "Paquete de Cuidados",
        [ArenaShard.HealthAndSize] = "Fragmento de vida y tamaño",
        [ArenaShard.Movement] = "Fragmento de velocidad de movimiento",
        [ArenaShard.Omnivamp] = "Fragmento de omnivampirismo",
        [ArenaShard.Shardholder] = "Fragmento de valor del portafragmento",
        [ArenaShard.Healing] = "Fragmento de poder de curaciones y escudo",
        [ArenaShard.Tenacity] = "Fragmento de tenacidad"
    };

    private static readonly Dictionary<string, ArenaShard> Titles = Names.ToDictionary(
        pair => Ratings.Normalize(pair.Value), pair => pair.Key, StringComparer.Ordinal);
    private readonly Dictionary<string, Champion> roster = champions.ToDictionary(champion => champion.Key, StringComparer.OrdinalIgnoreCase);

    internal static string Name(ArenaShard shard) => Names[shard];

    internal static ArenaShard? Recognize(string text)
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

    internal static ArenaShard?[]? Identify(DetectionResult offer) => offer.PanelVisible &&
        offer.Shards is { } shards && shards.Count(shard => shard.HasValue) >= 2 &&
        !offer.Cards.Any(card => card.HasValue) && !offer.Items.Any(item => item) ? shards : null;

    internal CardComparison?[]? ForOffer(GameContext game, DetectionResult offer)
    {
        if (game.Mode != Mode.Arena || Identify(offer) is not { } shards ||
            !profiles.TryGetValue(game.Champion, out var profile) || !roster.TryGetValue(game.Champion, out var champion))
            return null;
        return ArenaChoiceRating.Badges(shards.Select(shard => shard.HasValue ? Rate(shard.Value, champion, profile) : null).ToArray());
    }

    private static ArenaChoiceRating Rate(ArenaShard shard, Champion champion, HashSet<string> profile)
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
            ArenaShard.Haste when durable && control => (Tier.S, "Control disponible más seguido"),
            ArenaShard.Haste => (casting || control ? Tier.A : Tier.B, "Reduce el enfriamiento de habilidades"),
            ArenaShard.Health or ArenaShard.HealthAndSize => (durable ? Tier.A : Tier.B, "Más vida para resistir daño"),
            ArenaShard.Armor => (durable ? Tier.A : Tier.B, "Mitiga daño físico"),
            ArenaShard.MagicResist => (durable ? Tier.A : Tier.B, "Mitiga daño mágico"),
            ArenaShard.Resistances => (durable ? Tier.A : Tier.B, "Protección contra daño físico y mágico"),
            ArenaShard.AttackDamage => (ad ? Tier.A : Tier.C, "Potencia el daño de ataque"),
            ArenaShard.Power => (ap && ad ? Tier.A : ap || ad ? Tier.B : Tier.C, "Daño de ataque y poder de habilidad"),
            ArenaShard.AbilityPower => (ap ? Tier.A : Tier.C, "Beneficia habilidades que escalan con PH"),
            ArenaShard.AttackSpeed => (attacks ? Tier.A : Tier.C, "Más ataques básicos por segundo"),
            ArenaShard.Swiftness => (attacks && casting ? Tier.A : attacks || casting || control ? Tier.B : Tier.C,
                "Velocidad de ataque y aceleración"),
            ArenaShard.Crit or ArenaShard.CritDamage => (crit ? Tier.A : attacks ? Tier.B : Tier.C,
                "Potencia una build de críticos"),
            ArenaShard.Lethality => (ad && profile.Contains("burst") ? Tier.A : ad ? Tier.B : Tier.C, "Penetración para daño físico"),
            ArenaShard.MagicPenetration => (ap ? Tier.A : Tier.C, "Penetración para daño mágico"),
            ArenaShard.Healing => (heals ? Tier.A : profile.Contains("sustain") ? Tier.B : Tier.C, "Curaciones y escudos más fuertes"),
            ArenaShard.Omnivamp => (profile.Contains("sustain") || attacks ? Tier.A : Tier.B, "Recuperas vida al infligir daño"),
            ArenaShard.Tenacity => (durable ? Tier.A : Tier.B, "Acorta el control que recibes"),
            ArenaShard.Movement => (profile.Contains("mobility") || control ? Tier.A : Tier.B, "Ayuda a entrar o reposicionarte"),
            ArenaShard.Shardholder => (Tier.B, "Mejora los demás fragmentos"),
            ArenaShard.Fortune => (Tier.B, "Oro ahora y en cada ronda"),
            ArenaShard.Care => (Tier.B, "Oro según las rondas perdidas"),
            ArenaShard.Random => (Tier.B, "Dos estadísticas aleatorias"),
            _ => throw new ArgumentOutOfRangeException(nameof(shard))
        };
        return new(Name(shard), tier, reason);
    }
}

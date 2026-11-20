using System;
using System.Collections.Generic;
using System.Linq;

namespace Aumentos;

internal sealed record ArenaAugmentFit(Tier? Tier, string Reason, int? Priority = null, bool Contextual = false,
    bool BaseOnly = false);

internal sealed class ArenaAugmentAffinity(Ratings ratings)
{
    private readonly Dictionary<string, Champion> champions = ratings.Catalog.Champions.ToDictionary(
        champion => champion.Key, StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, ArenaItem> items = (ratings.Catalog.ArenaEquipment ?? [])
        .Concat(ratings.Catalog.ArenaLegendaries ?? []).Concat(ratings.Catalog.ArenaPrismatics ?? [])
        .GroupBy(item => item.Id).ToDictionary(group => group.Key, group => group.OrderByDescending(item =>
            item.Categories?.Length ?? 0).First());

    internal ArenaAugmentFit? Evaluate(string champion, int id, int? level = null,
        IReadOnlyList<int>? equippedItems = null) => Context(champion, equippedItems) is { } context
        ? Evaluate(context, id, level) : null;

    internal void Apply(GameContext game, int?[] ids, int?[]? levels, IReadOnlyList<int>? equippedItems,
        CardComparison?[] badges)
    {
        if (game.Mode != Mode.Arena || badges.Length != ids.Length ||
            (levels is not null && (levels.Length != ids.Length || levels.Any(level => !IsSupportedLevel(level)))) ||
            Context(game.Champion, equippedItems) is not { } context)
            return;

        var fits = new ArenaAugmentFit?[ids.Length];
        for (int i = 0; i < ids.Length; i++)
        {
            if (ids[i] is not { } id || badges[i] is not { IsArenaAugment: true } badge ||
                Evaluate(context, id, levels?[i]) is not { } fit)
                continue;
            fits[i] = fit;
            var local = fit.Tier?.ToString() ?? badge.Local;
            badges[i] = badge with
            {
                Local = local,
                LocalReason = fit.Reason,
                UsesWiki = badge.UsesWiki || fit.Contextual || local != badge.Local || fit.Reason != badge.LocalReason
            };
        }

        if (badges.Length < 2 || badges.Any(badge => badge?.Local is not ("S" or "A" or "B" or "C")) ||
            !fits.Any(fit => fit is not null))
            return;

        var bestTier = badges.Min(badge => Enum.Parse<Tier>(badge!.Local));
        var leaders = Enumerable.Range(0, badges.Length).Where(i => badges[i]!.Local == bestTier.ToString()).ToArray();
        var isUpgradeOffer = levels?.Any(IsUpgrade) == true;
        var unknownDelta = fits.Any(fit => fit?.BaseOnly == true);
        var selected = unknownDelta ? null : SelectLeader(bestTier, leaders, fits, levels, isUpgradeOffer);
        var markedLower = badges.Any(badge => (badge!.RecommendedUpgrade || badge.RecommendedAugment) &&
            badge.Local != bestTier.ToString());
        if (selected is null && !unknownDelta && !markedLower && !leaders.Any(i => fits[i]?.Contextual == true))
            return;

        for (int i = 0; i < badges.Length; i++)
        {
            badges[i] = badges[i]! with
            {
                RecommendedUpgrade = i == selected && IsUpgrade(levels?[i]),
                RecommendedAugment = i == selected && !IsUpgrade(levels?[i])
            };
        }
    }

    private static int? SelectLeader(Tier tier, int[] leaders, ArenaAugmentFit?[] fits, int?[]? levels, bool upgrades)
    {
        if (tier is not (Tier.S or Tier.A))
            return null;
        if (leaders is [var single])
            return !upgrades || (IsUpgrade(levels?[single]) && fits[single]?.Priority is not null) ||
                fits[single]?.Contextual == true ? single : null;
        if (leaders.All(i => fits[i]?.Priority is not null) &&
            (!upgrades || leaders.All(i => IsUpgrade(levels?[i]))))
        {
            var priority = leaders.Max(i => fits[i]!.Priority);
            var best = leaders.Where(i => fits[i]!.Priority == priority).ToArray();
            return best is [var chosen] ? chosen : null;
        }
        return null;
    }

    private Profile? Context(string key, IReadOnlyList<int>? equipped)
    {
        if (ratings.Wiki is null || !champions.TryGetValue(key, out var champion))
            return null;
        var gear = (equipped ?? []).Where(id => id > 0)
            .Select(id => items.GetValueOrDefault(id) ?? items.GetValueOrDefault(id < 10000 ? id + 220000 : id))
            .OfType<ArenaItem>().DistinctBy(item => item.Id).ToArray();
        bool Has(string trait) => ratings.HasTrait(champion.Key, trait);
        int Count(string category) => gear.Count(item => item.Categories?.Contains(category) == true);
        return new(champion, Has("attacks"), Has("attackspeed"), Has("crit"), Has("range"), Has("ap"), Has("casting"),
            Has("burst"), Has("control"), Has("ally"), Has("durability"), Has("sustain"), Has("mobility"), Has("hybrid"),
            Count("OnHit"), Count("AttackSpeed"), Count("CriticalStrike"), Count("Health"),
            Count("SpellDamage"), Count("SpellVamp") + Count("LifeSteal"),
            gear.Any(item => item.Id == 223084), gear.Any(item => item.Id == 223116),
            gear.Any(item => item.Id is 223068 or 223115 or 223153 or 224633 or 226672));
    }

    private ArenaAugmentFit? Evaluate(Profile profile, int id, int? level)
    {
        if (!IsSupportedLevel(level) || ratings.VariableReason(Mode.Arena, id) is not null ||
            ratings.ArenaAugment(id) is not { } card || (card.MaxLevel is { } max && level > max) ||
            ratings.Wiki?.Find(card) is not { Disabled: not true, Removed: not true } wiki)
            return null;

        var baseline = ratings.RateEditorial(Mode.Arena, profile.Champion.Key, id);
        var fit = level is 2 or 3
            ? Upgrade(profile, id, level.Value, baseline, wiki.Signals) ?? BaseOfUpgrade(profile, id, baseline, wiki.Signals)
            : WithItems(profile, id, wiki.Signals) ?? RenataSupportFit(profile, id, baseline) ??
                (baseline is null ? Unlock(profile, id) : Describe(profile, id));
        if (fit is null)
            return null;
        return ratings.HasOverride(Mode.Arena, profile.Champion.Key, id)
            ? fit with { Tier = baseline } : fit;
    }

    private static ArenaAugmentFit? BaseOfUpgrade(Profile profile, int id, Tier? baseline, ArenaWikiSignal signals)
    {
        var fit = WithItems(profile, id, signals) ?? (baseline is null ? Unlock(profile, id) : null);
        return fit is null ? null : fit with { Reason = $"{fit.Reason} · Tier del efecto base", BaseOnly = true };
    }

    private static bool IsUpgrade(int? level) => level is 2 or 3;
    private static bool IsSupportedLevel(int? level) => level is null or 1 or 2 or 3;

    private static ArenaAugmentFit? WithItems(Profile p, int id, ArenaWikiSignal signals) => id switch
    {
        29 when p.OnHit > 0 && signals.HasFlag(ArenaWikiSignal.AppliesOnHitViaAbilities) => new(p.Casting && p.OnHit >= 2 ? Tier.S : Tier.A,
            "Tus habilidades activan los efectos al impacto de tu equipo", Contextual: true),
        41 when p.Durable && p.Health >= 2 => new(Tier.S,
            "Amplifica la vida máxima que ya acumulaste", Contextual: true),
        44 when p.Rylais => new(Tier.A, "Refuerza la ralentización del Cetro de Cristal de Rylai", Contextual: true),
        48 when p.SpellItems >= 2 && p.CritItems > 0 && signals.HasFlag(ArenaWikiSignal.AbilitiesCanCrit) => new(Tier.A,
            "El PH y crítico equipados refuerzan los críticos de tus habilidades", Contextual: true),
        75 when p.Frontline && p.Health >= 2 => new(Tier.S,
            "La quemadura cercana escala con la vida máxima que compraste", Contextual: true),
        87 when p.Attacks && p.OnHit >= 2 && signals.HasFlag(ArenaWikiSignal.AppliesOnHitToAdditionalTarget) => new(Tier.S,
            "Aplica tus efectos al impacto a un segundo objetivo", Contextual: true),
        92 when p.CritItems >= 2 && p.ItemDamage => new(Tier.A,
            "Tus críticos potencian efectos de objetos que infligen daño", Contextual: true),
        107 when p.Attacks && p.OnHit >= 2 && signals.HasFlag(ArenaWikiSignal.RepeatsOnHitOnAttacks) => new(Tier.S,
            "Repite los efectos al impacto de tus objetos cada pocos ataques", Contextual: true),
        118 when p.CritItems >= 2 && p.Ally => new(Tier.A,
            "Tu probabilidad de crítico potencia las curaciones compatibles", Contextual: true),
        152 when p.Heartsteel && signals.HasFlag(ArenaWikiSignal.RequiresHeartsteel) => new(Tier.A,
            "Ya tienes Corazón de Acero y puedes avanzar su misión", Contextual: true),
        181 when p.Health >= 2 && signals.HasFlag(ArenaWikiSignal.OwnMaxHealthDamageOnAttacks) => new(p.Attacks ? Tier.S : Tier.A,
            "Tus ataques aprovechan tu vida máxima para infligir más daño", Contextual: true),
        _ => null
    };

    // Upgrade deltas below were seen in the current game client. The wiki revision confirms the effects,
    // but its older figures for these cards are deliberately excluded from the rating.
    private static ArenaAugmentFit? Upgrade(Profile p, int id, int level, Tier? baseline, ArenaWikiSignal signals) => (id, level) switch
    {
        (22, 2) when p.Champion.Key == "Jax" && signals.HasFlag(ArenaWikiSignal.GrantsAttackSpeed) =>
            new(baseline, "Velocidad de ataque de 60 % a 80 %", 1),
        (22, 3) when p.Champion.Key == "Jax" && signals.HasFlag(ArenaWikiSignal.GrantsAttackSpeed) => new(p.OnHit >= 2 ? Tier.S : baseline,
            "Velocidad de ataque de 80 % a 130 % y sin límite", p.OnHit >= 2 ? 5 : 2, p.OnHit >= 2),
        (22, 2 or 3) when p.AttackSpeed && signals.HasFlag(ArenaWikiSignal.GrantsAttackSpeed) =>
            new(baseline, "Velocidad de ataque adicional desde el inicio", level - 1),
        (30, 2 or 3) when baseline is not null =>
            new(baseline, "Conversión de PH en aceleración", level - 1),
        (44, 2) when p.FrequentSlow || p.Rylais => new(baseline ?? Tier.A, "Refuerza todavía más tus ralentizaciones"),
        (ArenaUpgradeRatings.LightEmUp, 2) when p.Attacks && signals.HasFlag(ArenaWikiSignal.AttacksForBonusMagicDamage) => new(baseline,
            "Fuegos artificiales de 4 a 5 · Daño de 90 % a 100 %", 2),
        (52, 2) when p.AttackSpeed && signals.HasFlag(ArenaWikiSignal.AttackSpeedThresholdDamage) =>
            new(baseline, "Límite de ataques/s de 3.5 a 10", p.SpeedItems >= 2 ? 4 : 3,
            p.SpeedItems >= 2),
        (69, 2) => new(baseline, "Empuja más lejos a los rivales cercanos al perder vida"),
        (115, 2 or 3) when p.Attacks => new(baseline, "Aumenta el alcance de tus ataques básicos"),
        (181, 2) when p.Health >= 2 && signals.HasFlag(ArenaWikiSignal.OwnMaxHealthDamageOnAttacks) =>
            new(baseline, "El daño de tus ataques escala más con tu vida máxima"),
        (1328, 2) when p.AttackSpeed && p.Crit && signals.HasFlag(ArenaWikiSignal.GrantsAttackSpeed) => p.CritItems >= 2
            ? new(baseline, "Tus críticos acumulan velocidad hasta un límite doble", 4, true)
            : new(baseline, "Duplica el máximo de acumulaciones", 2),
        _ => null
    };

    private static ArenaAugmentFit? RenataSupportFit(Profile p, int id, Tier? baseline) =>
        p.Champion.Key != "Renata" ? null : id switch
        {
            94 => new(baseline, "Gastas parte de tu vida para escudar a un aliado cuando cae por debajo del 30 %", Priority: 1),
            198 => new(baseline, "Tus escudos queman a un enemigo cercano según su vida máxima", Priority: 2),
            215 => new(baseline ?? Tier.A,
                "Escudar aliados con tu E acumula aceleración permanente para volver a proteger antes", Priority: 3),
            _ => null
        };

    private static ArenaAugmentFit? Describe(Profile p, int id) => id switch
    {
        9 => new(null, "Da aceleración y hace que tus ataques quemen y ralenticen"),
        12 => new(null, "Intercambia posiciones, acelera y protege a tu aliado"),
        29 => new(null, "Tus habilidades aplican efectos al impacto"),
        30 => new(null, "Convierte parte de tu poder de habilidad en aceleración"),
        44 => new(null, "Tus ralentizaciones reducen más la velocidad enemiga"),
        64 => new(null, "Regenera más vida cuando estás herido"),
        79 => new(null, "Cadencia Letal y Conquistador favorecen el combate sostenido"),
        110 => new(null, "El vínculo con tu equipo daña a quienes lo atraviesan"),
        115 => new(null, "Aumenta el alcance de tus ataques básicos"),
        118 => new(null, "Las curaciones y escudos compatibles pueden asestar críticos"),
        181 => new(null, "Tus ataques infligen daño adicional según tu vida máxima"),
        218 => new(null, "Inmovilizar acumula armadura y resistencia mágica"),
        379 => new(null, "Lanza una bola de fuego que rebota entre enemigos y tú"),
        1333 => new(null, "Escudo al bajar al umbral de vida"),
        2087 when p.Ap => new(null, "Potencia el poder de habilidad"),
        _ => null
    };

    // Each entry binds a documented mechanic to a champion trait that can actually use it.
    // Choice-dependent and champion-locked effects without enough evidence stay unrated.
    private static ArenaAugmentFit? Unlock(Profile p, int id) => id switch
    {
        1 => Fit(p.Casting, "Lanzar habilidades acumula aceleración durante el combate"),
        4 when p.Champion.Key == "Rell" => new(Tier.C, "Bloquea Tormenta Magnética, clave para agrupar rivales"),
        5 => Fit(p.Ally, "Potencia temporalmente la vida, el DA, el PH y la velocidad de ataque de tu aliado"),
        7 => Fit(p.Attacks || p.Physical, "Aumenta el daño de ataque"),
        11 => new(Tier.B, "Lanzar tu definitiva te vuelve invulnerable brevemente"),
        13 => Fit(p.Frontline, "Más vida a cambio de infligir menos daño", Tier.C),
        15 => Fit(p.Sustain || p.Lifesteal > 0, "Curarte y regenerar vida daña a un enemigo cercano"),
        16 => Fit(p.Burst, "Electrocutar premia un combo y Fase Veloz facilita salir"),
        20 => new(Tier.B, "Recuperas vida al quedar por debajo del umbral"),
        21 => Fit(p.Ally, "Barrera y Curación para tu equipo"),
        23 => Fit(p.Attacks || p.Frontline, "Potencia ataques que recuperan vida"),
        26 => Fit(p.FrequentSlow, "Infliges más daño mientras seas más rápido que tu objetivo"),
        27 => Fit(p.Mobility, "Desplazarte deja una estela que detona"),
        28 => Fit(p.Attacks || p.Casting, "El daño repetido reduce ambas resistencias"),
        33 => new(Tier.B, "Lanza un gancho automático al enemigo cercano"),
        34 => new(Tier.B, "Comienzas la ronda con un Escudo Negro"),
        35 => Fit(p.Frontline, "Aplica Ignición y Extenuación a los rivales cercanos"),
        36 => Fit(p.Attacks, "Los ataques aplican quemaduras según la vida máxima del objetivo", Tier.C),
        38 => Fit(p.Burst || p.Range, "Premia iniciar el combate y rematar rivales heridos"),
        42 => Fit(p.Control, "Inmovilizar o anclar rivales te cura", Tier.C),
        44 when p.FrequentSlow => new(Tier.A, "Refuerza las ralentizaciones de tus habilidades"),
        46 => new(Tier.B, "Tus golpes activan el daño adicional del Alma Infernal"),
        47 => Fit(p.Crit, "Aumenta la probabilidad de crítico", Tier.C),
        60 => Fit(p.Sustain, "Dañar reactiva la regeneración de vida y maná"),
        63 => Fit(p.Mobility && p.Frontline, "Usar un desplazamiento acumula resistencias", Tier.C),
        66 => Fit(p.Frontline, "Golpear con el borde del corte ralentiza y te cura"),
        67 => Fit(p.Casting || p.Sustain, "Lanzar habilidades restaura vida"),
        72 => Fit(p.Casting, "Tus habilidades marcan para un golpe adicional de tu aliado"),
        74 => Fit(p.Attacks, "Tus ataques reducen el daño del objetivo", Tier.C),
        76 => Fit(p.Ally, "Curar o escudar al aliado daña y ralentiza a enemigos cercanos", Tier.C),
        77 => Fit(p.Crit, "Los golpes críticos también te curan", Tier.C),
        86 => Fit(p.LongRange, "Dañar desde lejos dispara Descarga de Tiros Certeros", Tier.C),
        88 => new(Tier.B, "Una vez por ronda puedes repetir tu definitiva"),
        92 => new(Tier.B, "Los efectos de objetos y el daño prolongado pueden asestar críticos"),
        98 => Fit(p.Casting, "Convierte la aceleración de habilidad en velocidad de movimiento"),
        112 => new(Tier.B, "Tu definitiva activa inmunidad breve a control"),
        133 => Fit(p.Casting || p.Ap, "Las habilidades disparan misiles que escalan con la vida del objetivo"),
        138 => Fit(p.Sustain || p.Attacks, "Tu daño restaura vida mediante omnivampirismo"),
        165 => Fit(p.Casting, "Las habilidades básicas se reinician periódicamente"),
        180 => Fit(p.Ap || p.SpellItems >= 2, "El escudo inicial aumenta con tu poder de habilidad"),
        192 => Fit(p.Ally, "Curar o escudar aliados progresa la misión", Tier.C),
        194 => Fit(p.Burst, "El daño de tu definitiva aplica polimorfia"),
        206 when !p.Hybrid => Fit(p.Physical, "Convierte poder de habilidad en daño de ataque", Tier.C),
        215 => Fit(p.Ally, "Curar o escudar a aliados acumula aceleración", Tier.C),
        216 => Fit(p.Frontline, "Permanecer junto a rivales acumula vida máxima"),
        219 => Fit(p.Casting || p.Ap, "Dañar con habilidades acumula fuerza adaptable"),
        221 => Fit(p.Mobility, "Desplazarte aplica una quemadura acumulable", Tier.C),
        227 => new(Tier.A, "Eliges mejoras en tres yunques de estadísticas"),
        228 => new(Tier.A, "Eliges mejoras en cuatro yunques de estadísticas"),
        231 => new(Tier.B, "Una luz protectora escuda a los aliados que atraviesa"),
        232 => Fit(p.Frontline, "Daña a enemigos cercanos y deja curación al impactar"),
        301 => new(Tier.B, "La estrella concede invulnerabilidad breve cerca de ti"),
        305 => Fit(p.Frontline && p.Control || p.Range && !p.Mobility,
            "Velocidad de movimiento constante y resistencia a ralentizaciones para colocarte"),
        316 => new(Tier.B, "Tu daño aplica Heridas Graves y reduce escudos"),
        321 => Fit(p.Mobility, "Tu desplazamiento te protege y puede escudar aliados", Tier.C),
        330 => Fit(p.Mobility, "Desplazarte cerca del enemigo acumula velocidad de movimiento", Tier.C),
        343 => Fit(p.Ally || p.Sustain, "Curar y escudar aumenta tu daño de habilidad", Tier.C),
        357 => Fit(p.Attacks && (p.Hybrid || p.Casting), "Alternar ataques y habilidades aumenta su daño"),
        1332 when p.Ap && p.Sustain => new(Tier.A, "Tu curación propia ayuda a pagar el costo de vida al lanzar"),
        1332 when p.Sustain || (p.Attacks && p.Lifesteal > 0) =>
            new(Tier.B, "La curación ayuda a compensar el costo de vida de cada habilidad"),
        1332 => new(Tier.C, "Ganas PH al perder vida, pero cada habilidad te cuesta vida"),
        1405 => Fit(p.Casting, "Dañar a un campeón con habilidades da un impulso breve de velocidad"),
        _ => null
    };

    private static ArenaAugmentFit Fit(bool active, string reason, Tier weak = Tier.B) =>
        new(active ? Tier.A : weak, reason);

    private sealed record Profile(Champion Champion, bool Attacks, bool AttackSpeed, bool Crit, bool Range, bool Ap,
        bool Casting, bool Burst, bool Control, bool Ally, bool Durable, bool Sustain, bool Mobility, bool Hybrid,
        int OnHit, int SpeedItems, int CritItems, int Health, int SpellItems, int Lifesteal,
        bool Heartsteel, bool Rylais, bool ItemDamage)
    {
        internal bool Frontline => Champion.Roles[0] is "Tank" or "Fighter";
        internal bool Physical => !Ap && Champion.Roles[0] is "Assassin" or "Fighter" or "Marksman";
        internal bool LongRange => Champion.Key is "Caitlyn" or "Ezreal" or "Hwei" or "Jayce" or "Jhin" or "Jinx" or
            "KogMaw" or "Lux" or "Nidalee" or "Varus" or "Velkoz" or "Xerath" or "Ziggs" or "Zoe";
        internal bool FrequentSlow => Champion.Key is "Anivia" or "Ashe" or "Chogath" or "Karma" or "Malphite" or
            "Nasus" or "Nunu" or "Olaf" or "Singed" or "Trundle" or "Zilean";
    }
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace Aumentos;

internal sealed class ArenaLegendaryRatings(ArenaItem[] items, Champion[] champions, Dictionary<string, HashSet<string>> profiles)
{
    private readonly ArenaItemTitles titles = new(items);
    private readonly Dictionary<string, Champion> roster = champions.ToDictionary(champion => champion.Key, StringComparer.OrdinalIgnoreCase);

    internal ArenaItem?[]? Identify(DetectionResult offer) => titles.Identify(offer);

    internal CardComparison?[]? ForOffer(GameContext game, DetectionResult offer)
    {
        if (game.Mode != Mode.Arena || !profiles.TryGetValue(game.Champion, out var profile) ||
            !roster.TryGetValue(game.Champion, out var champion) || Identify(offer) is not { } recognized)
            return null;
        return ArenaChoiceRating.Badges(recognized.Select(item => item is null ? null : Rate(item, champion, profile)).ToArray());
    }

    private static ArenaChoiceRating? Rate(ArenaItem item, Champion champion, HashSet<string> profile)
    {
        bool durable = profile.Contains("durability");
        bool control = profile.Contains("control");
        bool casting = profile.Contains("casting");
        bool heals = profile.Contains("ally") || profile.Contains("resistheals");
        bool attacks = profile.Contains("attacks");
        bool ap = profile.Contains("ap");
        bool ad = attacks || champion.Roles[0] is "Fighter" or "Marksman" ||
            (champion.Roles[0] == "Assassin" && !ap);
        bool closeRange = champion.Roles[0] is "Tank" or "Fighter" or "Assassin" ||
            (durable && profile.Contains("resistheals"));
        var categories = item.Categories ?? [];

        // Specific effects take precedence over stat categories from the client catalogue.
        var fit = item.Id switch
        {
            222524 => new Fit(control && durable ? Tier.S : control ? Tier.A : durable ? Tier.B : Tier.C,
                "Tu control potencia a aliados"),
            223050 => new Fit(durable && closeRange ? Tier.A : control && closeRange ? Tier.B : Tier.C,
                "Tu definitiva ralentiza cerca de ti"),
            222504 => new Fit(durable ? Tier.B : Tier.C, "Resistencia y escudo contra magia"),
            224401 => new Fit(durable ? Tier.B : Tier.C, "Defensa contra daño mágico repetido"),
            223068 or 226664 => new Fit(durable && closeRange ? Tier.A : durable ? Tier.B : Tier.C,
                "Resistencias y daño a enemigos cercanos"),
            223065 => new Fit(durable && (heals || profile.Contains("sustain")) ? Tier.A : durable ? Tier.B : Tier.C,
                "Amplifica curación y escudos recibidos"),
            223075 => new Fit(durable ? Tier.B : Tier.C, "Armadura y heridas graves"),
            223084 => new Fit(durable && profile.Contains("sustain") ? Tier.S : durable ? Tier.A : Tier.C,
                "Acumula vida máxima al combatir"),
            223119 => new Fit(durable && control ? Tier.A : durable ? Tier.B : Tier.C,
                "Maná y escudo al controlar enemigos"),
            224005 => new Fit(control && (ap || heals) ? Tier.A : control ? Tier.B : Tier.C,
                "Tu control prepara daño para aliados"),
            223109 => new Fit(durable || heals ? Tier.A : Tier.C, "Protege al aliado vinculado"),
            223190 => new Fit(durable || heals ? Tier.A : Tier.B, "Escudo grupal y resistencias"),
            223107 or 223222 or 223504 or 226616 or 226617 or 226620 or 226621 =>
                new Fit(heals ? Tier.A : casting ? Tier.B : Tier.C, "Refuerza el apoyo a aliados"),
            222065 => new Fit(heals ? Tier.A : casting ? Tier.B : Tier.C, "Movilidad para tu equipo"),
            224403 => new Fit(Tier.B, "Aporta estadísticas variadas"),
            228009 => new Fit(Tier.B, "Cambia de objeto cada fase de compras"),
            _ => FromCategories()
        };
        return fit is null ? null : new(item.Name, fit.Tier, fit.Reason);

        Fit? FromCategories()
        {
            bool armor = categories.Contains("Armor");
            bool magicResist = categories.Contains("SpellBlock") || categories.Contains("MagicResist");
            bool physicalDamage = categories.Contains("Damage");
            bool spellDamage = categories.Contains("SpellDamage");
            if (categories.Contains("CriticalStrike"))
                return new(profile.Contains("crit") ? Tier.A : attacks ? Tier.B : Tier.C, "Potencia ataques críticos");
            if (categories.Contains("OnHit") || categories.Contains("AttackSpeed"))
                return new(attacks || profile.Contains("hybrid") ? Tier.A : ad || (ap && spellDamage) ? Tier.B : Tier.C,
                    "Aprovecha ataques básicos y sus efectos");
            if (spellDamage)
                return new(ap ? Tier.A : durable && (armor || magicResist) ? Tier.B : Tier.C,
                    categories.Contains("MagicPenetration") ? "Penetración para daño mágico" : "Potencia habilidades con PH");
            if (physicalDamage)
                return new(ad ? Tier.A : Tier.C, categories.Contains("ArmorPenetration")
                    ? "Penetración para daño físico" : "Potencia el daño físico");
            if (armor && magicResist)
                return new(durable ? Tier.A : Tier.B, "Protección contra daño físico y mágico");
            if (armor || magicResist)
                return new(durable ? Tier.B : Tier.C, armor ? "Defensa contra daño físico" : "Defensa contra daño mágico");
            if (categories.Contains("Health"))
                return new(durable ? Tier.A : Tier.B, "Vida para permanecer en combate");
            return null;
        }
    }

    private sealed record Fit(Tier Tier, string Reason);
}

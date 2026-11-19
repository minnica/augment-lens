using System.Collections.Generic;
using System.Linq;

namespace Aumentos;

internal sealed class ArenaPrismaticRatings(ArenaItem[] items, Dictionary<string, HashSet<string>> profiles)
{
    private readonly ArenaItemTitles titles = new(items);

    internal ArenaItem?[]? Identify(DetectionResult offer) => titles.Identify(offer);

    internal CardComparison?[]? ForOffer(GameContext game, DetectionResult offer)
    {
        if (game.Mode != Mode.Arena || !profiles.TryGetValue(game.Champion, out var profile) ||
            Identify(offer) is not { } recognized)
            return null;

        return ArenaChoiceRating.Badges(recognized.Select(item => item is null ? null : Rate(item, profile)).ToArray());
    }

    private static ArenaChoiceRating? Rate(ArenaItem item, HashSet<string> profile)
    {
        var fit = item.Id switch
        {
            443054 => new Fit("Ataques al impacto y armadura", "attacks", "durability"),
            443055 => new Fit("Ataques energizados", "attacks"),
            443056 => new Fit("Escala con victorias de ronda", ""),
            443058 => new Fit("Armadura y bloqueo", "durability", Strong: "resistheals"),
            443059 => new Fit("Resistencia mágica y protección", "durability", Strong: "resistheals"),
            443060 or 443069 => new Fit("Daño de golpes críticos", "crit", "attacks"),
            443061 => new Fit("Prolonga inmovilizaciones", "control"),
            443062 => new Fit("Convierte daño en curación", "ap", "ally"),
            443063 => new Fit("Mejora curaciones y escudos", "ally", "durability", "resistheals"),
            443064 => new Fit("Estadísticas variables", ""),
            443079 => new Fit("Protección contra inmovilización", "durability sustain", "mobility"),
            443081 => new Fit("Aplica efectos al impacto", "attacks"),
            443083 => new Fit("Vida y regeneración", "durability", "sustain"),
            443090 => new Fit("Ataques consecutivos", "attacks"),
            443193 => new Fit("Resistencias y escudo", "durability", Strong: "resistheals"),
            444636 => new Fit("Daño mágico y movilidad", "ap", "casting"),
            444637 => new Fit("Vida y poder de habilidad", "ap", "durability", "sustain"),
            444644 => new Fit("Protección y poder de habilidad", "ap", "casting"),
            446632 => new Fit("Ataque tras habilidad y curación", "attacks", "sustain"),
            446656 => new Fit("Daño mágico e inmovilización", "ap control", "casting"),
            446667 => new Fit("La definitiva cura a aliados", "ally durability", "casting"),
            446671 => new Fit("Críticos y desplazamiento", "crit", "attacks"),
            446691 or 446693 or 447115 => new Fit("Letalidad y daño explosivo", "physicalburst", "attacks"),
            447100 => new Fit("Ataques reducen el enfriamiento de saltos", "attacks", "mobility"),
            447101 => new Fit("Ataques y habilidades acumulan oro", "attacks casting"),
            447102 => new Fit("Poder de habilidad y velocidad de ataque", "hybrid", "ap attacks"),
            447103 => new Fit("Daño y omnivampirismo", "sustain", "attacks"),
            447104 => new Fit("Escudo al acumular lanzamientos", "casting", "ap durability"),
            447105 => new Fit("Alcance al aliado y escudo", "ally", "casting"),
            447106 => new Fit("Escala con almas de dragón", ""),
            447107 => new Fit("Ataques potencian la definitiva", "attacks", "casting"),
            447108 => new Fit("Daño mágico acumulativo", "ap", "casting"),
            447109 => new Fit("Daño adicional al inmovilizar", "control", Strong: "ap"),
            447110 => new Fit("Habilidades potencian ataques", "hybrid", "attacks ap"),
            447112 => new Fit("Reduce resistencias al infligir daño", "attacks casting", "durability"),
            447113 => new Fit("Detona el daño de habilidades", "ap", "casting", "control"),
            447114 => new Fit("Inmovilizar activa impactos", "control attacks", "durability"),
            447116 => new Fit("Golpea puntos débiles cercanos", "mobility", "attacks durability"),
            447118 => new Fit("Quemaduras y curación", "sustain", "ap casting"),
            447119 => new Fit("Escudo y daño con vida máxima", "durability", "control"),
            447120 => new Fit("Amplía alcance y daño a distancia", "range", "attacks"),
            447121 => new Fit("Escala con daño y poder de habilidad", "hybrid"),
            447122 => new Fit("Vida y control de área", "durability", "control"),
            447123 => new Fit("Los escudos y curas fortalecen aliados", "ally", "casting"),
            _ => null
        };
        if (fit is null)
            return null;
        if (fit.Primary.Length == 0)
            return new(item.Name, Tier.B, fit.Reason);

        bool primary = Matches(fit.Primary);
        var tier = primary && Matches(fit.Strong) ? Tier.S : primary ? Tier.A :
            Matches(fit.Secondary) ? Tier.B : Tier.C;
        return new(item.Name, tier, fit.Reason);

        bool Matches(string tags) => tags.Split(' ', System.StringSplitOptions.RemoveEmptyEntries).Any(tag =>
            tag == "physicalburst" ? profile.Contains("burst") && !profile.Contains("ap") : profile.Contains(tag));
    }

    private sealed record Fit(string Reason, string Primary, string Secondary = "", string Strong = "");
}

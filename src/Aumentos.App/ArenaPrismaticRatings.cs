using System.Collections.Generic;
using System.Linq;

namespace Aumentos;

internal sealed record RankedPrismatic(int Id, ArenaChoiceRating Rating, int Matches);
internal sealed record ArenaPrismaticTop(RankedPrismatic[] Entries, int OfferedMask)
{
    internal bool IsOffered(int index) => (OfferedMask & (1 << index)) != 0;
}
internal sealed record ArenaPrismaticOffer(CardComparison?[] Badges, ArenaPrismaticTop Top);

internal sealed class ArenaPrismaticRatings(ArenaItem[] items, Dictionary<string, HashSet<string>> profiles)
{
    private readonly ArenaItemTitles titles = new(items);
    private readonly Dictionary<string, ChampionRanking> rankings = profiles.ToDictionary(
        profile => profile.Key, profile => Rank(items, profile.Value), profiles.Comparer);


    internal ArenaItem?[]? Identify(DetectionResult offer) => titles.Identify(offer);

    internal ArenaPrismaticTop? TopForChampion(string champion) =>
        rankings.TryGetValue(champion, out var ranking) ? new(ranking.Top, 0) : null;

    internal ArenaPrismaticOffer? ForOffer(GameContext game, DetectionResult offer)
    {
        if (game.Mode != Mode.Arena || !rankings.TryGetValue(game.Champion, out var ranking) ||
            Identify(offer) is not { } recognized)
            return null;

        var choices = recognized.Select(item => item is null ? null : ranking.ById.GetValueOrDefault(item.Id)).ToArray();
        var ordered = choices.OfType<RankedPrismatic>().OrderBy(choice => choice.Rating.Tier)
            .ThenByDescending(choice => choice.Matches).ToArray();
        if (ordered.Length == 0)
            return null;
        var best = ordered[0];
        bool distinctBest = ordered.Length == choices.Length && ordered.Length > 1 &&
            (best.Rating.Tier != ordered[1].Rating.Tier || best.Matches != ordered[1].Matches);
        var badges = choices.Select(choice =>
        {
            if (choice is null)
                return null;
            int position = System.Array.FindIndex(ranking.Top, item => item.Id == choice.Id) + 1;
            return new CardComparison(choice.Rating.Tier.ToString(), ArenaChoice: choice.Rating with
            {
                Recommended = distinctBest && choice.Id == best.Id,
                TopPosition = position == 0 ? null : position
            });
        }).ToArray();

        int offeredMask = 0;
        for (int index = 0; index < ranking.Top.Length; index++)
            if (recognized.Any(item => item?.Id == ranking.Top[index].Id))
                offeredMask |= 1 << index;
        return new(badges, new(ranking.Top, offeredMask));
    }

    private static ChampionRanking Rank(ArenaItem[] items, HashSet<string> profile)
    {
        var ranked = items.Select(item => Rate(item, profile)).OfType<RankedPrismatic>().ToArray();
        return new(ranked.ToDictionary(item => item.Id), ranked.OrderBy(item => item.Rating.Tier)
            .ThenByDescending(item => item.Matches).ThenBy(item => item.Id).Take(5).ToArray());
    }

    private static RankedPrismatic? Rate(ArenaItem item, HashSet<string> profile)
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
            443090 => new Fit("Velocidad de ataque y daño al impacto", "attacks", Strong: "attackspeed"),
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
            return new(item.Id, new(item.Name, Tier.B, fit.Reason), 0);

        int primary = Matches(fit.Primary), secondary = Matches(fit.Secondary), strong = Matches(fit.Strong);
        var tier = primary > 0 && strong > 0 ? Tier.S : primary > 0 ? Tier.A :
            secondary > 0 ? Tier.B : Tier.C;
        return new(item.Id, new(item.Name, tier, fit.Reason), primary + secondary + strong);

        int Matches(string tags) => tags.Split(' ', System.StringSplitOptions.RemoveEmptyEntries).Count(tag =>
            tag == "physicalburst" ? profile.Contains("burst") && !profile.Contains("ap") : profile.Contains(tag));
    }

    private sealed record ChampionRanking(Dictionary<int, RankedPrismatic> ById, RankedPrismatic[] Top);
    private sealed record Fit(string Reason, string Primary, string Secondary = "", string Strong = "");
}

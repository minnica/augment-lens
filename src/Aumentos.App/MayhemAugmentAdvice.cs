using System;
using System.Collections.Generic;
using System.Linq;

namespace Aumentos;

internal sealed class MayhemAugmentAdvice(Ratings ratings, IReadOnlyDictionary<int, MayhemItem> items)
{
    private const string Tiers = "SABCDE";
    private readonly Dictionary<int, Augment> cards = ratings.Catalog.Augments
        .Where(card => card.Mode == nameof(Mode.Caos)).ToDictionary(card => card.Id);
    private readonly Dictionary<string, Champion> champions = ratings.Catalog.Champions
        .ToDictionary(champion => champion.Key, StringComparer.OrdinalIgnoreCase);

    private enum Fit { Limited, Useful, Strong, Exceptional, Urgent }
    private readonly record struct Advice(Fit Fit, string Reason);

    internal void Apply(GameContext game, MayhemMatch? match, int?[] ids, CardComparison?[] badges)
    {
        if (game.Mode != Mode.Caos || match is null || ids.Any(id => id is null) || ids.Distinct().Count() != ids.Length)
            return;
        var ranks = badges.Select(badge => badge is { Loading: false, VisibleTierText: { Length: 1 } tier }
            ? Tiers.IndexOf(tier[0]) : -1).ToArray();
        if (ranks.Length < 2 || ranks.Contains(-1))
            return;
        var bestRank = ranks.Min();
        var tied = Enumerable.Range(0, ranks.Length).Where(index => ranks[index] == bestRank).ToArray();
        if (tied.Length < 2)
            return;

        var context = tied.Select(index => (Index: index, Advice: Assess(game.Champion, match, ids[index]!.Value)))
            .OrderByDescending(option => option.Advice?.Fit).ToArray();
        if (context.All(option => option.Advice.HasValue) && context[0].Advice is { Fit: >= Fit.Strong } lead &&
            lead.Fit > context[1].Advice!.Value.Fit)
        {
            Recommend(context[0].Index, lead.Reason);
            return;
        }

        var observed = tied.Select(index => (Index: index, Sample: badges[index]!.MayhemObserved))
            .OrderBy(option => option.Sample?.Tier).ToArray();
        if (observed.All(option => option.Sample is { IsPrevious: false }) && observed[0].Sample!.Tier < observed[1].Sample!.Tier)
            Recommend(observed[0].Index, "Mejor rendimiento observado para tu campeón en AramHex (China)");

        void Recommend(int index, string reason) =>
            badges[index] = badges[index]! with { RecommendedAugment = true, LocalReason = reason };
    }

    private Advice? Assess(string champion, MayhemMatch match, int id)
    {
        if (!cards.TryGetValue(id, out var card))
            return null;

        bool attacks = ratings.HasTrait(champion, "attacks");
        bool damageOverTime = champion is "Brand" or "Cassiopeia" or "Lillia" or "Malzahar" or "Singed" or "Teemo";
        var stats = match.CombatStats;
        return card.Key switch
        {
            "ARAM_Vulnerability" => Vulnerability(champion, damageOverTime, match),
            "LittleExtraHelp" when champion == "Sivir" && stats?.AttackRange is <= 525 &&
                match.Opponents.Count(rival => ratings.HasTrait(rival.Champion, "range")) >= 2 =>
                new(Fit.Strong, "Tu R te da alcance para atacar desde más lejos contra varios rivales a distancia"),
            "LittleExtraHelp" when champion == "Sivir" =>
                new(Fit.Strong, "Tu R te da alcance y velocidad de ataque en peleas de equipo"),
            "StillHere" when attacks && stats?.LifeSteal is <= .05 && match.Opponents.Count(IsDiver) >= 2 =>
                new(Fit.Urgent, "Con poco robo de vida, te ayuda a recuperarte al seguir atacando contra rivales que se te lanzan encima"),
            "StillHere" => attacks ? new(Fit.Useful, "Robo de vida al bajar tu vida") : new(Fit.Limited, ""),
            "ARAM_InfernalConduit" => damageOverTime
                ? new(Fit.Strong, "Tu daño prolongado aplica la quemadura repetidamente") : new(Fit.Useful, ""),
            "ARAM_JeweledGauntlet" when ratings.HasTrait(champion, "ap") && stats?.CritChance is >= .25 =>
                new(Fit.Strong, "Tus habilidades aprovechan el crítico que ya acumulaste"),
            "ARAM_JeweledGauntlet" => new(Fit.Useful, ""),
            "ARAM_ScopedWeapons" or "ARAM_ScopierWeapons" or "ARAM_ScopiestWeapons" when attacks &&
                stats?.AttackRange is > 0 and <= 525 && match.Opponents.Count(IsDiver) >= 2 =>
                new(Fit.Strong, "Más alcance para atacar desde una distancia segura frente a quienes buscan entrar"),
            "ARAM_ScopedWeapons" or "ARAM_ScopierWeapons" or "ARAM_ScopiestWeapons" => new(Fit.Useful, ""),
            "ARAM_CourageoftheColossus" when ratings.HasTrait(champion, "control") && ratings.HasTrait(champion, "durability") =>
                new(Fit.Strong, "Tu control te da un escudo al inmovilizar en primera línea"),
            "ARAM_CourageoftheColossus" => new(Fit.Limited, ""),
            _ => null
        };
    }

    private bool IsDiver(MayhemOpponent rival) => rival.Champion is "JarvanIV" or "Malphite" ||
        champions.TryGetValue(rival.Champion, out var champion) && MayhemBuilds.IsDiver(champion);

    private Advice Vulnerability(string champion, bool damageOverTime, MayhemMatch match)
    {
        var crit = match.CombatStats?.CritChance;
        if (champion == "Sivir" && crit is <= .75)
            return new(Fit.Exceptional, "25 puntos de crítico que refuerzan tu Q y los rebotes de tu W");
        if (champion == "Sivir" && crit is null)
            return new(Fit.Strong, "El crítico refuerza tu Q y los rebotes de tu W");
        var item = match.Items.Where(id => id is 3153 or 6672 or 6653 or 3118)
            .Select(id => items.GetValueOrDefault(id)).FirstOrDefault(item => item is { Finished: true });
        if (item is not null)
            return new(crit is <= .75 ? Fit.Exceptional : Fit.Strong, $"El daño de {item.Name} puede infligir golpes críticos");
        if (damageOverTime)
            return new(crit is <= .75 ? Fit.Exceptional : Fit.Strong, "Tu daño prolongado puede infligir golpes críticos");
        return new(crit is >= .95 ? Fit.Limited : Fit.Useful, "");
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Aumentos;

internal sealed record ArenaPlacementRating(Tier Tier, int Games, string Patch, bool Global, decimal AveragePlace,
    decimal Advantage, int Rarity)
{
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-MX");
    internal string ShortSample => $"{Games.ToString("N0", Spanish)} p.";
    internal string Sample => $"{Games.ToString("N0", Spanish)} partidas";
    internal string Source => Global ? "GGEZ global" : GgezArenaPage.Attribution;
    internal string AveragePlaceText => AveragePlace.ToString("0.00", Spanish);
}

internal static class ArenaPlacementRatings
{
    private const int MinimumChampionGames = 50;
    private const int MinimumGlobalGames = 100;
    private const int MinimumPeers = 3;
    private const int PriorGames = 100;
    private const int MinimumWinnerGames = 100;
    private const decimal RecommendationMargin = 0.20m;
    private static readonly IReadOnlyDictionary<int, ArenaPlacementRating> Empty = new Dictionary<int, ArenaPlacementRating>();
    private static readonly ConditionalWeakTable<GgezArenaSnapshot, Dictionary<int, ArenaPlacementRating>> RatingsBySample = new();

    internal static IReadOnlyDictionary<int, ArenaPlacementRating> Calculate(GgezArenaSnapshot? snapshot)
    {
        if (snapshot is not { IsPrevious: false, PlacementCount: >= 4 and <= 8 })
            return Empty;
        return RatingsBySample.GetValue(snapshot, CalculateSample);
    }

    private static Dictionary<int, ArenaPlacementRating> CalculateSample(GgezArenaSnapshot snapshot)
    {
        var result = new Dictionary<int, ArenaPlacementRating>();
        bool global = snapshot.Champion.Length == 0;
        var minimumGames = global ? MinimumGlobalGames : MinimumChampionGames;
        var groups = snapshot.Picks.Where(entry => entry.Value.GamesPicked >= minimumGames &&
            entry.Value.GamesPicked <= snapshot.SampleGames &&
            entry.Value.AveragePlace is >= 1 && entry.Value.AveragePlace <= snapshot.PlacementCount &&
            entry.Value.Rarity is >= 0 and <= 2).GroupBy(entry => entry.Value.Rarity!.Value);
        var sourcePatch = LeaguePatch.Public(snapshot.SourcePatch);
        foreach (var group in groups)
        {
            var cards = group.ToArray();
            if (cards.Length <= MinimumPeers)
                continue;
            decimal totalGames = cards.Sum(card => (decimal)card.Value.GamesPicked);
            decimal totalPlaces = cards.Sum(card => card.Value.GamesPicked * card.Value.AveragePlace!.Value);
            foreach (var (id, pick) in cards)
            {
                int games = pick.GamesPicked;
                decimal average = pick.AveragePlace!.Value;
                decimal peerAverage = (totalPlaces - games * average) / (totalGames - games);
                // Pull small samples toward the placement of other augments of the same rarity.
                decimal advantage = (peerAverage - average) * games / (games + (decimal)PriorGames);
                var tier = advantage switch
                {
                    >= 0.30m => Tier.S,
                    >= 0.12m => Tier.A,
                    <= -0.15m => Tier.C,
                    _ => Tier.B
                };
                result.Add(id, new(tier, games, sourcePatch, global, average, advantage, group.Key));
            }
        }
        return result;
    }

    internal static int? Recommend(CardComparison?[] badges)
    {
        if (badges.Length < 2 || badges.Any(badge => badge?.DisplayedArenaPlacement is not { Global: false }))
            return null;

        var ranked = badges.Select((badge, position) => (Badge: badge!, Position: position,
                Placement: badge!.DisplayedArenaPlacement!))
            .OrderByDescending(entry => entry.Placement.Advantage).ToArray();
        var best = ranked[0];
        var second = ranked[1];
        if (best.Placement.Games < MinimumWinnerGames ||
            ranked.Any(entry => entry.Placement.Rarity != best.Placement.Rarity) ||
            best.Placement.Advantage - second.Placement.Advantage < RecommendationMargin)
            return null;

        // A conflicting champion-fit assessment makes the observed leader a comparison point.
        if (best.Badge.Local == nameof(Tier.C) && ranked.Any(entry => entry.Badge.Local is "S" or "A"))
            return null;
        return best.Position;
    }
}

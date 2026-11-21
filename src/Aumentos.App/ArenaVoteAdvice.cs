using System.Linq;
using System.Windows;

namespace Aumentos;

internal static class ArenaVoteAdvice
{
    internal static ArenaVoteDisplay Create(Int32Rect bounds, nint target, ArenaVoteOffer offer, Ratings ratings, LiveMatch match)
    {
        var scores = ArenaGuestAdvice.Evaluate(ratings, offer.Options.Select(option => option.Guest).ToArray(), match,
            teamHealth: offer.TeamHealth, round: offer.Round);
        var ranked = scores.OfType<ArenaGuestRating>().OrderByDescending(rating => rating.Score).ToArray();

        int? recommended = ranked.Length == 3 && ranked.Length == scores.Length && ranked[0].Score >= 5 &&
            ranked[0].Score - ranked[1].Score >= 2 ? ranked[0].Score : null;
        var labels = offer.Options.Zip(scores, (option, score) => new ArenaVoteLabel(option.Bounds, score?.Tier.ToString(),
            score?.Reason ?? "Sin valoración para este invitado",
            recommended.HasValue && score?.Score == recommended)).ToArray();
        return new(bounds, target, labels);
    }
}

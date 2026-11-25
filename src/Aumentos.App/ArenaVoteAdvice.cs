using System.Linq;
using System.Windows;

namespace Aumentos;

internal static class ArenaVoteAdvice
{
    internal static ArenaVoteDisplay Create(Int32Rect bounds, nint target, ArenaVoteOffer offer, Ratings ratings, LiveMatch match)
    {
        var scores = ArenaGuestAdvice.Evaluate(ratings, offer.Options.Select(option => option.Guest).ToArray(), match,
            teamHealth: offer.TeamHealth, round: offer.Round);
        var labels = offer.Options.Zip(scores, (option, score) => new ArenaVoteLabel(option.Bounds, score?.Tier.ToString())).ToArray();
        return new(bounds, target, labels);
    }
}

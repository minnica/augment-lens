using System.Linq;

namespace Aumentos;

public sealed record ArenaItem(int Id, string Name, bool SharedName = false, string[]? Categories = null,
    bool Buyable = false);

internal sealed class ArenaItemTitles(ArenaItem[] items, bool allowUnlisted = false)
{
    private readonly ILookup<string, ArenaItem> byName = items.ToLookup(item => Ratings.Normalize(item.Name));

    internal ArenaItem?[]? Identify(DetectionResult offer)
    {
        if (!offer.PanelVisible || offer.Cards.Any(id => id.HasValue) || offer.Shards?.Any(shard => shard.HasValue) == true)
            return null;

        var recognized = offer.Texts.Select((text, index) => offer.Items[index] &&
            byName[Ratings.Normalize(text)].Take(2).ToArray() is [var item] ? item : null).ToArray();
        // An item name can be shared by both pools. Confirm it against another exact card in the offer.
        int exclusive = recognized.Count(item => item is { SharedName: false });
        bool unlisted = recognized.Where((item, index) => offer.Items[index] && item is null).Any();
        // Two exclusive prismatics still identify the offer when another item is missing from the pool.
        if (recognized.Count(item => item is not null) < 2 || exclusive == 0 ||
            unlisted && (!allowUnlisted || exclusive < 2))
            return null;
        return recognized;
    }
}

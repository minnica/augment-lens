using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Aumentos;

internal sealed record ShopItemDetails(int? Cost, string? Category, string? Stats, string? Effect, bool HasTrueDamage);

internal sealed record ShopItemCatalog(string Patch, Dictionary<int, ShopItemDetails> Items)
{
    internal static ShopItemCatalog Load(string assets, string patch)
    {
        try
        {
            var catalog = Ratings.Read<ShopItemCatalog>(Path.Combine(assets, "shop-items.json"));
            if (catalog.Patch == patch && catalog.Items is { Count: > 0 })
                return catalog;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { }
        return new(patch, []);
    }

    internal ShopItemDetails? Find(int id, Mode mode)
    {
        if (Items.TryGetValue(id, out var exact))
            return exact;
        return mode == Mode.Caos ? Items.GetValueOrDefault(MayhemBuilds.BaseId(id)) : null;
    }
}

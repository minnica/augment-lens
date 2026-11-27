using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Aumentos;

internal static class AramKitItemSets
{
    private const string Data = "https://data.aramkit.com/";
    private const int MinimumRouteGames = 200;
    private const double MinimumStyleShare = 0.02;
    private sealed record Index(Release[] Versions);
    private sealed record Release(string Version, string DataPath);
    private sealed record Rankings(ItemId[] Rows);
    internal sealed record ItemId(int Id);
    internal sealed record Gold(bool Purchasable);
    internal sealed record ShopItem(Dictionary<string, bool>? Maps, Gold? Gold, bool? InStore,
        bool HideFromAll, int SpecialRecipe, string[]? Tags, string? RequiredChampion);
    internal sealed record Start(int SampleCount, ItemId[] Items);
    internal sealed record Option(int SampleCount, ItemId Item);
    internal sealed record Route(int SampleCount, ItemId[] PurchaseOrder, Start[]? Starters, Option[]? Boots, Option[]? LaterItems);
    internal sealed record Profile(Route[]? Routes);
    internal sealed record Archetype(string Key, double PickRate, Profile[]? Profiles);
    private sealed record BuildStats(Archetype[]? Archetypes);
    private sealed record Builds(BuildStats? Unfiltered);
    private sealed record Detail(ItemId Champion, Builds? Builds);

    internal static async Task<ShopItemSetCatalog> Download(Catalog catalog, HttpClient http, CancellationToken token)
    {
        var patch = catalog.Patch;
        var versions = await Read<Index>(Data + "data/versions.json");
        var release = versions.Versions?.FirstOrDefault(entry => entry.Version == patch);
        if (release?.DataPath is not { } path ||
            !Regex.IsMatch(path, @"\Adata/" + Regex.Escape(patch) + @"-[0-9]{8}-[a-f0-9]+\z"))
            throw new InvalidDataException($"ARAMKit aún no publica las builds de {LeaguePatch.Public(patch)}.");
        string stats = $"{Data}{path}/stats/all/";
        string dragon = $"https://ddragon.leagueoflegends.com/cdn/{patch}.1/data/es_MX/";
        var ids = await ShopItemSetCatalog.ChampionIds(http, patch, token);
        var items = (await Read<DataDragonTable<ShopItem>>(dragon + "item.json")).Data
            .ToDictionary(entry => int.Parse(entry.Key, System.Globalization.CultureInfo.InvariantCulture), entry => entry.Value);
        var published = (await Read<Rankings>(stats + "champion-rankings.json")).Rows.Select(row => row.Id).ToHashSet();
        var result = new ConcurrentBag<ChampionItemSets>();
        await Parallel.ForEachAsync(catalog.Champions, new ParallelOptions { MaxDegreeOfParallelism = 3, CancellationToken = token },
            async (champion, cancellation) =>
            {
                if (!ids.TryGetValue(champion.Key, out var id) || !published.Contains(id))
                    return;
                var detail = await http.GetFromJsonAsync<Detail>($"{stats}champion-details/{id}.json", cancellation);
                if (detail?.Champion?.Id != id)
                    throw new InvalidDataException($"ARAMKit devolvió una ficha incompleta para {champion.Name}.");
                if (detail.Builds?.Unfiltered?.Archetypes is not { Length: > 0 } styles) return;
                var variants = Variants(styles, items, champion.Key);
                if (variants.Length > 0)
                    result.Add(new(champion.Key, id, variants));
            });
        if (result.IsEmpty)
            throw new InvalidDataException("ARAMKit no publicó rutas de compra utilizables.");
        return new(Mode.Caos, patch, path, result.OrderBy(champion => champion.Key, StringComparer.Ordinal).ToArray());

        async Task<T> Read<T>(string url) => await http.GetFromJsonAsync<T>(url, token)
            ?? throw new InvalidDataException("Falta una tabla de objetos o campeones.");
    }

    internal static ShopBuildVariant[] Variants(Archetype[] styles, Dictionary<int, ShopItem> items, string champion)
    {
        var result = new List<ShopBuildVariant>();
        foreach (var style in styles.Where(style => style.PickRate >= MinimumStyleShare && ShopBuildVariant.StyleName(style.Key) is not null)
                     .OrderByDescending(style => style.PickRate))
        {
            // A profile can have several purchase orders. Keep all four blocks from the same observed route.
            var routes = (style.Profiles ?? []).SelectMany(profile => profile.Routes ?? [])
                .Where(route => route.SampleCount >= MinimumRouteGames).OrderByDescending(route => route.SampleCount);
            foreach (var route in routes)
            {
                var core = PurchaseIds(route.PurchaseOrder);
                if (core.Length != 3 || core.Distinct().Count() != 3 || core.Any(id => id == 0 || IsBoot(id)))
                    continue;
                if (result.Any(existing => existing.Style == style.Key || existing.Core.ToHashSet().SetEquals(core)))
                    continue;
                var start = (route.Starters ?? []).Where(row => row.SampleCount > 0).OrderByDescending(row => row.SampleCount)
                    .Select(row => PurchaseIds(row.Items)).FirstOrDefault(ids => ids.Length > 0 && !ids.Contains(0)) ?? [];
                var boots = Options(route.Boots).Where(IsBoot).Take(2).ToArray();
                var later = Options(route.LaterItems).Where(id => !IsBoot(id) && !core.Contains(id)).Take(5).ToArray();
                result.Add(new(style.Key, route.SampleCount, start, core, boots, later));
                break;
            }
        }
        return result.ToArray();

        int[] PurchaseIds(ItemId[]? entries) => (entries ?? []).Select(item => Buyable(item.Id)).ToArray();
        IEnumerable<int> Options(Option[]? rows) => (rows ?? []).Where(row => row.SampleCount > 0)
            .OrderByDescending(row => row.SampleCount).Select(row => Buyable(row.Item.Id)).Where(id => id > 0).Distinct();
        bool IsBoot(int id) => items[id].Tags?.Contains("Boots") == true;
        int Buyable(int id)
        {
            if (!items.TryGetValue(id, out var item)) return 0;
            if (item.Gold?.Purchasable != true && item.SpecialRecipe > 0)
            {
                id = item.SpecialRecipe;
                if (!items.TryGetValue(id, out item)) return 0;
            }
            return item.Gold?.Purchasable == true && item.Maps?.GetValueOrDefault("12") == true &&
                item.InStore != false && !item.HideFromAll && (string.IsNullOrEmpty(item.RequiredChampion) ||
                string.Equals(item.RequiredChampion, champion, StringComparison.OrdinalIgnoreCase)) ? id : 0;
        }
    }
}

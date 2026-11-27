using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Aumentos;

internal static class LolArenaItemSets
{
    private const string Site = "https://lolarena.pro";
    private static readonly Regex Articles = HtmlPattern(@"<article\b[^>]*>(.*?)</article>");
    private static readonly Regex Summary = HtmlPattern(@"<h3\b[^>]*>[^<>]+? Arena ([^<>]+?) build</h3>\s*<p\b[^>]*>[^<>]*? · Core items: (.*?)(?: · Augments:|</p>)");
    private static readonly Regex Patch = HtmlPattern(@"<p\b[^>]*>Patch (\d+\.\d+) · updated ");

    private sealed record Index(string Patch, Dictionary<int, string> Champions);
    internal sealed record EnglishItem(string Name);
    internal sealed record Pick(int Id, int Games);
    internal sealed record Style([property: JsonPropertyName("style")] string Key, int Games, bool Graded);
    internal sealed record OverallBuild([property: JsonPropertyName("cohort_size")] int CohortSize, bool Limited,
        [property: JsonPropertyName("core_items")] Pick[]? Core,
        [property: JsonPropertyName("flex_items")] Pick[]? Flex, [property: JsonPropertyName("pepite_items")] Pick[]? Situational);
    internal sealed record Page(int ChampionId, int Queue, Style[] Styles, OverallBuild? InitialBuild, Pick[]? Boots);
    internal sealed record Stock(Dictionary<string, int> Names, Dictionary<int, ArenaItem> Shop, HashSet<int> Prismatics);

    internal static async Task<ShopItemSetCatalog> Download(Catalog catalog, HttpClient http, CancellationToken token)
    {
        var index = await http.GetFromJsonAsync<Index>(Site + "/api/champions", token);
        if (index?.Patch != catalog.Patch || index.Champions is not { Count: > 0 })
            throw new InvalidDataException("LoLArena aún no publica los estilos del parche instalado.");
        var ids = await ShopItemSetCatalog.ChampionIds(http, catalog.Patch, token);
        var names = await http.GetFromJsonAsync<DataDragonTable<EnglishItem>>(
            $"https://ddragon.leagueoflegends.com/cdn/{catalog.Patch}.1/data/en_US/item.json", token);
        if (names?.Data is not { Count: > 0 }) throw new InvalidDataException("Falta la tabla de objetos de Riot.");
        var shop = (catalog.ArenaLegendaries ?? []).Concat(catalog.ArenaEquipment ?? []).Where(item => item.Buyable)
            .ToDictionary(item => item.Id);
        var prismatics = (catalog.ArenaPrismatics ?? []).Select(item => item.Id).ToHashSet();
        var byName = shop.Keys.Concat(prismatics).Distinct()
            .Select(id => (Id: id, Name: names.Data.GetValueOrDefault(id.ToString(System.Globalization.CultureInfo.InvariantCulture))?.Name))
            .Where(item => !string.IsNullOrEmpty(item.Name)).GroupBy(item => item.Name!, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() == 1).ToDictionary(group => group.Key, group => group.First().Id, StringComparer.OrdinalIgnoreCase);
        var stock = new Stock(byName, shop, prismatics);
        var result = new ConcurrentBag<ChampionItemSets>();
        await Parallel.ForEachAsync(catalog.Champions, new ParallelOptions { MaxDegreeOfParallelism = 2, CancellationToken = token },
            async (champion, cancellation) =>
            {
                if (!ids.TryGetValue(champion.Key, out var id) || !index.Champions.TryGetValue(id, out var name)) return;
                var slug = Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9]", "");
                var html = await ChampionPage(http, slug, cancellation);
                var variants = Read(html, catalog.Patch, id, stock);
                if (variants.Length > 0) result.Add(new(champion.Key, id, variants));
            });
        if (result.IsEmpty) throw new InvalidDataException("LoLArena no publicó conjuntos de objetos utilizables.");
        return new(Mode.Arena, catalog.Patch, $"arena/{catalog.Patch}/lolarena",
            result.OrderBy(champion => champion.Key, StringComparer.Ordinal).ToArray());
    }

    private static async Task<string> ChampionPage(HttpClient http, string slug, CancellationToken token)
    {
        string url = $"{Site}/champion/{slug}";
        using var response = await http.GetAsync(url, token);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var retry = response.Headers.RetryAfter;
            var delay = retry?.Date - DateTimeOffset.UtcNow ?? retry?.Delta ?? TimeSpan.FromMinutes(1);
            await Task.Delay(delay > TimeSpan.Zero ? delay : TimeSpan.FromSeconds(1), token);
            return await http.GetStringAsync(url, token);
        }
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(token);
    }

    internal static ShopBuildVariant[] Read(string html, string patch, int championId, Stock stock)
    {
        if (Patch.Match(html).Groups[1].Value != LeaguePatch.Public(patch)) return [];
        using var rows = new StringReader(NextPage.ComponentStream(html));
        while (rows.ReadLine() is { } row)
        {
            int offset = row.IndexOf(':') + 1;
            if (offset == 0 || !row.AsSpan(offset).StartsWith("[\"$\"") || !row.Contains("\"initialBuild\"", StringComparison.Ordinal))
                continue;
            using var json = JsonDocument.Parse(row.AsMemory(offset));
            if (FindPage(json.RootElement) is not { } props) continue;
            var page = props.Deserialize<Page>(Ratings.JsonOptions);
            if (page?.ChampionId != championId || page.Queue is not (1700 or 1710 or 1750) || page.Styles is null) return [];
            return Variants(html, page, stock);
        }
        return [];

        static JsonElement? FindPage(JsonElement node)
        {
            if (node.ValueKind == JsonValueKind.Object)
            {
                if (node.TryGetProperty("initialBuild", out _) && node.TryGetProperty("championId", out _)) return node;
                if (node.TryGetProperty("children", out var children)) return FindPage(children);
            }
            if (node.ValueKind == JsonValueKind.Array)
                foreach (var child in node.EnumerateArray())
                    if (FindPage(child) is { } match) return match;
            return null;
        }
    }

    internal static ShopBuildVariant[] Variants(string html, Page page, Stock stock)
    {
        var results = new List<ShopBuildVariant>();
        var overall = page.InitialBuild;
        var common = (overall?.Core ?? []).Concat(overall?.Flex ?? []).Where(Legendary).Select(pick => pick.Id).Distinct().ToArray();
        var popular = common.Concat((overall?.Situational ?? []).Where(Legendary).Select(pick => pick.Id)).Distinct().ToArray();
        var boots = (page.Boots ?? []).Where(pick => pick.Games >= 20 && stock.Shop.TryGetValue(pick.Id, out var item) &&
            Has(item, "Boots")).OrderByDescending(pick => pick.Games).Select(pick => pick.Id).Distinct().ToArray();
        double minimumGames = Math.Max(100, page.Styles.Sum(row => (long)row.Games) * 0.02);
        foreach (Match article in Articles.Matches(html))
        {
            var summary = Summary.Match(WebUtility.HtmlDecode(article.Groups[1].Value.Replace("<!-- -->", "")));
            if (!summary.Success) continue;
            var (style, key) = summary.Groups[1].Value switch
            {
                "Tank" => ("tank", "tank"), "AP" => ("ap", "ap"), "AP burn" => ("ap_burn", "ap_burn"),
                "AD" => ("ad", "ad"), "Bruiser" => ("bruiser", "bruiser"), "Support" => ("support", "support"),
                "On-hit" => ("onhit", "on_hit"), "Lethality" => ("letha", "lethality"), _ => ("", "")
            };
            var sample = page.Styles.FirstOrDefault(row => row.Key == style && row.Graded && row.Games >= minimumGames);
            if (sample is null || key.Length == 0) continue;
            // Searching whole names keeps the comma in "Jak'Sho, The Protean" inside one item.
            var list = ", " + summary.Groups[2].Value.Trim() + ", ";
            var observed = stock.Names.Select(entry => (Id: entry.Value, Name: entry.Key, Index:
                    list.IndexOf(", " + entry.Key + ", ", StringComparison.OrdinalIgnoreCase)))
                .Where(item => item.Index >= 0).OrderBy(item => item.Index).ThenByDescending(item => item.Name.Length)
                .DistinctBy(item => item.Index).Select(item => item.Id).ToArray();
            var core = observed.Where(id => stock.Shop.TryGetValue(id, out var item) && !Has(item, "Boots")).Take(3).ToArray();
            var prism = observed.Where(stock.Prismatics.Contains).ToArray();
            if (core.Length < 2 || results.Any(other => other.Style == key || other.Core.ToHashSet().SetEquals(core))) continue;
            if (key is "ap" or "ap_burn" or "ad" && !core.Any(id => Fits(key, stock.Shop[id]))) continue;
            var later = popular.Except(core).Where(id => Fits(key, stock.Shop[id])).Take(4).ToArray();
            var footwear = observed.Where(id => stock.Shop.TryGetValue(id, out var item) && Has(item, "Boots"))
                .Concat(boots).Distinct().Take(3).ToArray();
            results.Add(new(key, sample.Games, [], core, footwear, later, prism));
        }
        if (results.Count == 0 && overall is { CohortSize: >= 100, Limited: false } && common.Length >= 2)
        {
            var core = common.Take(3).ToArray();
            results.Add(new("general", overall.CohortSize, [], core, boots.Take(3).ToArray(), popular.Except(core).Take(4).ToArray()));
        }
        return results.OrderByDescending(row => row.Games).ToArray();

        bool Legendary(Pick pick) => pick.Games >= 20 && stock.Shop.TryGetValue(pick.Id, out var item) && !Has(item, "Boots");
    }

    private static bool Has(ArenaItem item, string category) => item.Categories?.Contains(category) == true;
    private static bool Fits(string style, ArenaItem item) => !Has(item, "Boots") && style switch
    {
        "ap" or "ap_burn" => Has(item, "SpellDamage"),
        "tank" => !Has(item, "Damage") && !Has(item, "SpellDamage") && (Has(item, "Health") || Has(item, "Armor") || Has(item, "SpellBlock")),
        "on_hit" => Has(item, "OnHit") || Has(item, "AttackSpeed"),
        "bruiser" => Has(item, "Damage") && (Has(item, "Health") || Has(item, "Armor") || Has(item, "SpellBlock")),
        "lethality" => Has(item, "ArmorPenetration"), "ad" => Has(item, "Damage"),
        "support" => Has(item, "HealAndShieldPower") || Has(item, "Aura"), _ => false
    };
    private static Regex HtmlPattern(string pattern) => new(pattern, RegexOptions.CultureInvariant | RegexOptions.Singleline, TimeSpan.FromSeconds(2));
}

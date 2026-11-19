using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;

namespace Aumentos;

internal sealed record ArenaChampionTier(string Champion, string Tier, decimal TopThreePercent, int Games);

internal sealed record ArenaChampionTierList(string ClientPatch, string SourcePatch, DateTimeOffset FetchedAt,
    int SampleMatches, Dictionary<string, ArenaChampionTier> Champions)
{
    internal bool IsPrevious => ClientPatch != SourcePatch;
}

internal static class ArenaChampionTierPage
{
    private static readonly Regex Canonical = Pattern("""<link\b[^>]*\brel="canonical"[^>]*\bhref="([^"]+)"[^>]*>""");
    private static readonly Regex Table = Pattern("""
        \bdata:\{\s*list:\{\s*patch:"(\d{1,3}\.\d{1,3})",\s*matches:(\d+),\s*players:\d+,\s*rows:\[(.*?)\]\s*\},\s*names:\{(.*?)\}\s*,\s*filters:\{region:"all",\s*window:"patch"\}
        """);
    private static readonly Regex Name = Pattern("""
        "(\d+)":\{\s*id:"([A-Za-z0-9]+)",\s*name:"(?:[^"\\]|\\.)*",\s*key:"\1"\s*\}
        """);
    private static readonly Regex Row = Pattern("""
        \{\s*id:(\d+),\s*games:(\d+),\s*top3Rate:((?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?),[^{}]*?\btier:"([SABCD])"[^{}]*\}
        """);

    internal static Dictionary<string, string> Aliases(Catalog catalog)
    {
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var champion in catalog.Champions)
            aliases.TryAdd(Ratings.Normalize(champion.Key), champion.Key);
        foreach (var champion in catalog.Champions)
            aliases.TryAdd(Ratings.Normalize(champion.Name), champion.Key);
        return aliases;
    }

    internal static ArenaChampionTierList Read(string html, string patch, IReadOnlyDictionary<string, string> aliases)
    {
        var canonical = Canonical.Match(html);
        if (!Uri.TryCreate(WebUtility.HtmlDecode(canonical.Groups[1].Value), UriKind.Absolute, out var address) ||
            address != ArenaChampionTierSource.Address)
            throw new InvalidDataException("GGEZ.GG devolvió otra página de Arena.");

        var table = Table.Match(html);
        if (!table.Success || !Number(table.Groups[2], out var matches) || matches <= 0)
            throw new InvalidDataException("GGEZ.GG no publicó una lista de campeones de Arena de un solo parche.");
        var sourcePatch = table.Groups[1].Value;
        if (sourcePatch != patch && sourcePatch != LeaguePatch.Previous(patch))
            throw new InvalidDataException($"GGEZ.GG publica la lista de Arena para {LeaguePatch.Public(sourcePatch)}.");

        var keys = new Dictionary<int, string>();
        foreach (Match name in Name.Matches(table.Groups[4].Value))
        {
            if (!Number(name.Groups[1], out var id) || !aliases.TryGetValue(Ratings.Normalize(name.Groups[2].Value), out var key))
                continue;
            if (!keys.TryAdd(id, key) && keys[id] != key)
                throw new InvalidDataException("GGEZ.GG publicó dos campeones para el mismo identificador.");
        }

        var champions = new Dictionary<string, ArenaChampionTier>(StringComparer.OrdinalIgnoreCase);
        foreach (Match row in Row.Matches(table.Groups[3].Value))
        {
            if (!Number(row.Groups[1], out var id) || !keys.TryGetValue(id, out var key) ||
                !Number(row.Groups[2], out var games) || games is < 20 || games > matches ||
                !decimal.TryParse(row.Groups[3].ValueSpan, NumberStyles.Float, CultureInfo.InvariantCulture, out var topThree) ||
                topThree is < 0 or > 100)
                continue;
            var rating = new ArenaChampionTier(key, row.Groups[4].Value, topThree, games);
            if (!champions.TryAdd(key, rating) && champions[key] != rating)
                throw new InvalidDataException("GGEZ.GG publicó dos tiers distintos para el mismo campeón.");
        }
        if (champions.Count == 0)
            throw new InvalidDataException("GGEZ.GG no publicó tiers de campeones reconocibles.");
        return new(patch, sourcePatch, DateTimeOffset.UtcNow, matches, champions);
    }

    private static bool Number(Group group, out int value) =>
        int.TryParse(group.ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out value);

    private static Regex Pattern(string expression) =>
        new(expression, RegexOptions.CultureInvariant | RegexOptions.Singleline, TimeSpan.FromMilliseconds(700));
}

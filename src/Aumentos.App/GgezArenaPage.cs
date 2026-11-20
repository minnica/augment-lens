using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;

namespace Aumentos;

internal sealed record GgezArenaPick(decimal Percent, int GamesPicked, decimal? AveragePlace = null, int? Rarity = null)
{
    internal GgezArenaPick ValidatePlacement(int? placementCount) => this with
    {
        AveragePlace = GamesPicked > 0 && placementCount is >= 2 and <= 8 && AveragePlace >= 1 && AveragePlace <= placementCount
            ? AveragePlace : null,
        Rarity = Rarity is >= 0 and <= 2 ? Rarity : null
    };
}

internal sealed record GgezArenaSnapshot(string Champion, string ClientPatch, string SourcePatch,
    DateTimeOffset FetchedAt, int SampleGames, Dictionary<int, GgezArenaPick> Picks,
    int? PlacementCount = null, bool PlacementSchemaRead = false, Dictionary<int, int>? ItemGames = null,
    bool ItemSchemaRead = false)
{
    internal bool IsPrevious => SourcePatch != ClientPatch;
}

internal static class GgezArenaPage
{
    internal const string Attribution = "GGEZ.GG";
    internal const string ArenaUrl = "https://ggez.gg/tierlist/arena";

    private static readonly Regex Canonical = Pattern("""<link\b[^>]*\brel="canonical"[^>]*\bhref="([^"]+)"[^>]*>""");
    private static readonly Regex ArenaData = Pattern("""
        \bdata:\{\s*champion:"([A-Za-z0-9]+)",\s*id:\d+,\s*filters:\{region:"all",\s*window:"patch"\},\s*available:true,\s*patch:"(\d{1,3}\.\d{1,3})"
        """);
    private static readonly Regex Table = Pattern("""
        \baugments:\s*\[(.*?)\],\s*augOk:true,\s*[^{}]*?\baugTotal:(\d+)\b
        """);
    private static readonly Regex Placements = Pattern("""
        \brow:\{\s*id:\d+,\s*games:(\d+),[^{}]*?\bplaces:\[([\d,\s]+)\]
        """);
    private static readonly Regex ItemTable = Pattern("""
        \bitems:\s*\[(.*?)\],\s*itemsOk:true,\s*itemTotal:(\d+)\b
        """);
    private static readonly Regex PickRow = Pattern("""
        \{\s*id:(\d+),\s*games:(\d+),((?:[^{}"]|"(?:[^"\\]|\\.)*")*)\}
        """);
    // Consume string literals as units so a title or icon URL cannot supply a numeric property.
    private static readonly Regex PickField = Pattern("""
        "(?:[^"\\]|\\.)*"|\b(pickShare|avgPlace|rarity):\s*((?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?)(?=[,\s]|$)
        """);

    internal static Uri Address(string champion)
    {
        var slug = string.Equals(champion, "MonkeyKing", StringComparison.OrdinalIgnoreCase) ? "Wukong" : champion;
        return new($"https://ggez.gg/champion/{Uri.EscapeDataString(slug)}/arena");
    }

    internal static GgezArenaSnapshot Read(string html, string champion, string patch, IReadOnlySet<int> catalog)
    {
        if (!IsCanonical(html, Address(champion)))
            throw new InvalidDataException($"{Attribution} devolvió otra página de Arena.");

        // The Arena tab uses this table from the public HTML.
        var arena = ArenaData.Match(html);
        if (!arena.Success)
            throw new InvalidDataException($"{Attribution} · Sin muestra de Arena de un solo parche.");
        if (!string.Equals(arena.Groups[1].Value, champion, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"{Attribution} devolvió estadísticas de otro campeón.");
        var sourcePatch = arena.Groups[2].Value;
        if (sourcePatch != patch && sourcePatch != LeaguePatch.Previous(patch))
            throw new InvalidDataException($"{Attribution} publica el parche {LeaguePatch.Public(sourcePatch)}. Se necesita {LeaguePatch.Public(patch)} o el anterior.");

        var start = arena.Index + arena.Length;
        var end = html.IndexOf("</script>", start, StringComparison.OrdinalIgnoreCase);
        var table = end < 0 ? Match.Empty : Table.Match(html, start, end - start);
        if (!table.Success || !Number(table.Groups[2], out var sample) || sample <= 0)
            throw new InvalidDataException($"{Attribution} · Sin muestra de aumentos para el campeón.");

        int? placementCount = ReadPlacementCount(html, start, table.Index - start);
        var picks = ReadPicks(table.Groups[1].Value, sample, catalog, placementCount);
        if (picks.Count == 0)
            throw new InvalidDataException($"{Attribution} · Sin aumentos reconocibles.");
        var itemTable = ItemTable.Match(html, start, end - start);
        return new(champion, patch, sourcePatch, DateTimeOffset.UtcNow, sample, picks, placementCount,
            PlacementSchemaRead: true, ItemGames: ReadItems(itemTable), ItemSchemaRead: true);
    }

    private static Dictionary<int, int>? ReadItems(Match table)
    {
        if (!table.Success || !Number(table.Groups[2], out int total) || total <= 0)
            return null;
        var items = new Dictionary<int, int>();
        foreach (Match row in PickRow.Matches(table.Groups[1].Value))
        {
            if (Number(row.Groups[1], out int id) && id > 0 && Number(row.Groups[2], out int games) &&
                games >= 30 && games <= total && games >= 0.08m * total)
                items.TryAdd(id, games);
        }
        return items.Count == 0 ? null : items;
    }

    internal static bool IsCanonical(string html, Uri address) =>
        Uri.TryCreate(WebUtility.HtmlDecode(Canonical.Match(html).Groups[1].Value), UriKind.Absolute, out var page) &&
        string.Equals(page.AbsoluteUri, address.AbsoluteUri, StringComparison.OrdinalIgnoreCase);

    internal static Dictionary<int, GgezArenaPick> ReadPicks(string rows, int sample, IReadOnlySet<int> catalog, int? placementCount)
    {
        var picks = new Dictionary<int, GgezArenaPick>();
        var conflicts = new HashSet<int>();
        foreach (Match row in PickRow.Matches(rows))
        {
            if (!Number(row.Groups[1], out var id) || !catalog.Contains(id) || conflicts.Contains(id) ||
                !Number(row.Groups[2], out var games) || games < 0 || games > sample)
                continue;

            decimal? percent = null, averagePlace = null;
            int? rarity = null;
            foreach (Match field in PickField.Matches(row.Groups[3].Value))
            {
                switch (field.Groups[1].Value)
                {
                    case "pickShare" when DecimalNumber(field.Groups[2], out var share):
                        percent = share;
                        break;
                    case "avgPlace" when DecimalNumber(field.Groups[2], out var place):
                        averagePlace = place;
                        break;
                    case "rarity" when Number(field.Groups[2], out var category):
                        rarity = category;
                        break;
                }
            }
            if (percent is not (>= 0 and <= 100) || Math.Abs(percent.Value - 100m * games / sample) > 0.05m)
                continue;

            var pick = new GgezArenaPick(percent.Value, games, averagePlace, rarity).ValidatePlacement(placementCount);
            if (!picks.TryAdd(id, pick) && picks[id] != pick)
            {
                picks.Remove(id);
                conflicts.Add(id);
            }
        }
        return picks;
    }

    private static int? ReadPlacementCount(string html, int start, int length)
    {
        var match = Placements.Match(html, start, length);
        if (!match.Success || !Number(match.Groups[1], out var games) || games <= 0)
            return null;
        var bins = match.Groups[2].Value.Split(',');
        if (bins.Length is < 2 or > 8)
            return null;
        long total = 0;
        foreach (var bin in bins)
        {
            if (!int.TryParse(bin.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var count) || count < 0)
                return null;
            total += count;
        }
        return total == games ? bins.Length : null;
    }

    private static bool Number(Group group, out int value) =>
        int.TryParse(group.ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out value);

    private static bool DecimalNumber(Group group, out decimal value) =>
        decimal.TryParse(group.ValueSpan, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static Regex Pattern(string expression) =>
        new(expression, RegexOptions.CultureInvariant | RegexOptions.Singleline, TimeSpan.FromMilliseconds(700));
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;

namespace Aumentos;

internal sealed record GgezArenaPick(decimal Percent, int GamesPicked);

internal sealed record GgezArenaSnapshot(string Champion, string ClientPatch, string SourcePatch,
    DateTimeOffset FetchedAt, int SampleGames, Dictionary<int, GgezArenaPick> Picks)
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
    private static readonly Regex Pick = Pattern("""
        \{\s*id:(\d+),\s*games:(\d+),[^{}]*?\bpickShare:((?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?)(?=[,}\s])
        """);

    internal static Uri Address(string champion)
    {
        var slug = string.Equals(champion, "MonkeyKing", StringComparison.OrdinalIgnoreCase) ? "Wukong" : champion;
        return new($"https://ggez.gg/champion/{Uri.EscapeDataString(slug)}/arena");
    }

    internal static GgezArenaSnapshot Read(string html, string champion, string patch, IReadOnlySet<int> catalog)
    {
        var canonical = Canonical.Match(html);
        if (!Uri.TryCreate(WebUtility.HtmlDecode(canonical.Groups[1].Value), UriKind.Absolute, out var page) ||
            !string.Equals(page.AbsoluteUri, Address(champion).AbsoluteUri, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"{Attribution} devolvió otra página de Arena.");

        // The Arena tab uses this table from the public HTML. Only read numeric pick and sample fields.
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

        var picks = new Dictionary<int, GgezArenaPick>();
        foreach (Match row in Pick.Matches(table.Groups[1].Value))
        {
            if (!Number(row.Groups[1], out var id) || !catalog.Contains(id) ||
                !Number(row.Groups[2], out var games) ||
                !decimal.TryParse(row.Groups[3].ValueSpan, NumberStyles.Float, CultureInfo.InvariantCulture, out var percent) ||
                games < 0 || games > sample || percent is < 0 or > 100 ||
                Math.Abs(percent - 100m * games / sample) > 0.05m)
                continue;
            var pick = new GgezArenaPick(percent, games);
            if (!picks.TryAdd(id, pick) && picks[id] != pick)
                throw new InvalidDataException($"{Attribution} publicó dos valores para el mismo aumento.");
        }
        if (picks.Count == 0)
            throw new InvalidDataException($"{Attribution} · Sin aumentos reconocibles.");
        return new(champion, patch, sourcePatch, DateTimeOffset.UtcNow, sample, picks);
    }

    private static bool Number(Group group, out int value) =>
        int.TryParse(group.ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out value);

    private static Regex Pattern(string expression) =>
        new(expression, RegexOptions.CultureInvariant | RegexOptions.Singleline, TimeSpan.FromMilliseconds(700));
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;

namespace Aumentos;

internal enum OpGgBuildSection { Start, Boots, Core, Prismatic }

internal sealed record OpGgBuildItem(int Id, string Name);
internal sealed record OpGgBuildGroup(OpGgBuildSection Section, OpGgBuildItem[][] Choices);
internal sealed record OpGgBuildSnapshot(Mode Mode, string Champion, string ClientPatch, string SourcePatch,
    DateTimeOffset FetchedAt, OpGgBuildGroup[] Groups)
{
    internal bool IsPrevious => SourcePatch != ClientPatch;
}

internal static class OpGgBuildPage
{
    private static readonly Regex Link = HtmlPattern(@"<link\b[^>]*>");
    private static readonly Regex Title = HtmlPattern(@"<title\b[^>]*>[^<]*?\b(?:parche|patch)\s+(\d+\.\d+)\b[^<]*</title>");
    private static readonly Regex Table = HtmlPattern(@"<table\b[^>]*>(.*?)</table\s*>");
    private static readonly Regex Heading = HtmlPattern(@"<th\b[^>]*>(.*?)</th\s*>");
    private static readonly Regex Caption = HtmlPattern(@"<caption\b[^>]*>(.*?)</caption\s*>");
    private static readonly Regex Body = HtmlPattern(@"<tbody\b[^>]*>(.*?)</tbody\s*>");
    private static readonly Regex Row = HtmlPattern(@"<tr\b[^>]*>(.*?)</tr\s*>");
    private static readonly Regex Cell = HtmlPattern(@"<td\b[^>]*>(.*?)</td\s*>");
    private static readonly Regex Image = HtmlPattern(@"<img\b[^>]*>");
    private static readonly Regex Rel = Attribute("rel");
    private static readonly Regex Href = Attribute("href");
    private static readonly Regex Src = Attribute("src");
    private static readonly Regex Alt = Attribute("alt");
    private static readonly Regex Tags = HtmlPattern(@"<[^>]*>");
    private static readonly Regex ItemPath = HtmlPattern(@"^/meta/images/lol/[^/]+/item/(\d+)\.png$");

    internal static Uri Address(GameContext game)
    {
        var mode = game.Mode == Mode.Caos ? "aram-mayhem" : "arena";
        return new($"https://op.gg/es-419/lol/modes/{mode}/{Uri.EscapeDataString(game.Champion.ToLowerInvariant())}/build");
    }

    internal static OpGgBuildSnapshot Read(string html, GameContext game, string clientPatch)
    {
        var canonical = Link.Matches(html).Cast<Match>().FirstOrDefault(link =>
            string.Equals(Rel.Match(link.Value).Groups[2].Value, "canonical", StringComparison.OrdinalIgnoreCase));
        if (canonical is null ||
            WebUtility.HtmlDecode(Href.Match(canonical.Value).Groups[2].Value) != Address(game).AbsoluteUri)
            throw new InvalidDataException("OP.GG devolvió otra página de builds.");

        var title = Title.Match(html);
        if (!title.Success)
            throw new InvalidDataException("OP.GG no indicó el parche de la build.");
        var sourcePatch = title.Groups[1].Value;
        if (sourcePatch != clientPatch && sourcePatch != LeaguePatch.Previous(clientPatch))
            throw new InvalidDataException($"OP.GG publica la build para el parche {sourcePatch}; el cliente usa {clientPatch}.");

        var groups = new Dictionary<OpGgBuildSection, List<OpGgBuildItem[]>>();
        foreach (Match table in Table.Matches(html))
        {
            var markup = table.Groups[1].Value;
            var section = Classify(markup);
            if (section is null || (section == OpGgBuildSection.Prismatic && game.Mode != Mode.Arena))
                continue;

            var rows = Row.Matches(Body.Match(markup).Groups[1].Value).Cast<Match>()
                .Select(row => ReadItems(Cell.Match(row.Groups[1].Value).Groups[1].Value))
                .Where(items => items is { Length: >= 1 and <= 6 }).ToArray();
            foreach (var items in rows)
            {
                // Arena gives its boots table the same heading as its core routes.
                var category = section.Value;
                if (game.Mode == Mode.Arena && category == OpGgBuildSection.Core && items.Length == 1)
                {
                    if (!IsBoot(items[0].Id)) continue;
                    category = OpGgBuildSection.Boots;
                }

                if (!groups.TryGetValue(category, out var choices))
                    groups.Add(category, choices = []);
                if (choices.Count < 2 && !choices.Any(choice =>
                    choice.Select(item => item.Id).SequenceEqual(items.Select(item => item.Id))))
                    choices.Add(items);
            }
        }

        var result = groups.OrderBy(group => group.Key).Select(group => new OpGgBuildGroup(group.Key, group.Value.ToArray())).ToArray();
        if (result.Length == 0)
            throw new InvalidDataException("OP.GG no publicó una tabla de builds reconocible.");
        return new(game.Mode, game.Champion, clientPatch, sourcePatch, DateTimeOffset.UtcNow, result);
    }

    private static OpGgBuildSection? Classify(string table)
    {
        var heading = PlainText(Heading.Match(table).Groups[1].Value);
        var caption = PlainText(Caption.Match(table).Groups[1].Value);
        return Ratings.Normalize(heading) switch
        {
            "objetos iniciales" or "starter items" or "starting items" => OpGgBuildSection.Start,
            "botas" or "boots" => OpGgBuildSection.Boots,
            "builds principales" or "core builds" => OpGgBuildSection.Core,
            "objetos prismaticos" or "prism items" or "prismatic items" => OpGgBuildSection.Prismatic,
            _ => Ratings.Normalize(caption) switch
            {
                "items table" => OpGgBuildSection.Start,
                "boots table" => OpGgBuildSection.Boots,
                "builds table" => OpGgBuildSection.Core,
                _ => null
            }
        };
    }

    private static OpGgBuildItem[] ReadItems(string cell)
    {
        var items = new List<OpGgBuildItem>();
        foreach (Match image in Image.Matches(cell))
        {
            var src = WebUtility.HtmlDecode(Src.Match(image.Value).Groups[2].Value);
            if (!Uri.TryCreate(src, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
                uri.Host != "opgg-static.akamaized.net")
                continue;
            var path = ItemPath.Match(uri.AbsolutePath);
            if (!path.Success) continue;
            var name = WebUtility.HtmlDecode(Alt.Match(image.Value).Groups[2].Value).Trim();
            if (!int.TryParse(path.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int id) ||
                id <= 0 || name.Length is < 1 or > 120)
                return [];
            items.Add(new(id, name));
        }
        return items.ToArray();
    }

    private static bool IsBoot(int id) => (id >= 220000 ? id - 220000 : id) is
        3005 or 3006 or 3009 or 3010 or 3013 or 3020 or 3047 or 3111 or 3117 or 3158;

    private static string PlainText(string markup) => WebUtility.HtmlDecode(Tags.Replace(markup, " ")).Trim();

    private static Regex Attribute(string name) => HtmlPattern($"""(?<![\w-]){name}\s*=\s*(["'])(.*?)\1""");

    private static Regex HtmlPattern(string pattern) =>
        new(pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
}

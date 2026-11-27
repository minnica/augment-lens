using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Aumentos;

internal sealed record OpGgCard(string? Tier = null, decimal? PickPercent = null);
internal sealed record OpGgSnapshot(string Patch, Mode Mode, string Champion, DateTimeOffset FetchedAt,
    Dictionary<int, OpGgCard> Cards, string? SourcePatch = null)
{
    internal bool IsPrevious => SourcePatch is not null && SourcePatch != Patch;
    internal string DataPatch => SourcePatch ?? Patch;
}

internal static class OpGgPage
{
    // The six badges displayed on OP.GG's Mayhem page, in the provider's numeric order.
    private const string MayhemTiers = "SABCDE";

    internal static Uri Address(GameContext game, bool localized = false)
    {
        var mode = game.Mode == Mode.Caos ? "aram-mayhem" : "arena";
        var locale = localized ? "/es-419" : "";
        return new Uri($"https://op.gg{locale}/lol/modes/{mode}/{Uri.EscapeDataString(game.Champion.ToLowerInvariant())}/augments");
    }

    internal static OpGgSnapshot Read(string html, GameContext game, string patch,
        IReadOnlyDictionary<int, Augment> catalog)
    {
        var canonical = Regex.Match(html, """<link\b[^>]*\brel="canonical"[^>]*\bhref="([^"]+)"[^>]*>""");
        var address = WebUtility.HtmlDecode(canonical.Groups[1].Value);
        if (address != Address(game).AbsoluteUri && address != Address(game, localized: true).AbsoluteUri)
            throw new InvalidDataException("OP.GG devolvió otra página de aumentos.");
        var title = Regex.Match(html, @"<title>[^<]*?\b(?:patch|parche)\s+(\d+\.\d+)\b[^<]*</title>",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!title.Success)
            throw new InvalidDataException("OP.GG no indicó el parche.");
        var sourcePatch = title.Groups[1].Value;
        if (sourcePatch != patch && (game.Mode != Mode.Arena || sourcePatch != LeaguePatch.Previous(patch)))
            throw new InvalidDataException($"OP.GG publica el parche {sourcePatch}. Se necesita {patch}.");

        // The public page includes the data used to render its badges and Arena tables.
        // Reassemble the stream before reading records: one table can span several script tags.
        using var records = new StringReader(NextPage.ComponentStream(html));
        while (records.ReadLine() is { } line)
        {
            var offset = line.IndexOf(':') + 1;
            if (offset == 0 || !line.AsSpan(offset).StartsWith("[\"$\"") ||
                !line.Contains("\"data\":", StringComparison.Ordinal))
                continue;
            using var row = JsonDocument.Parse(line.AsMemory(offset));
            var node = row.RootElement;
            if (node.GetArrayLength() < 4 || node[3].ValueKind != JsonValueKind.Object ||
                !node[3].TryGetProperty("data", out var data))
                continue;
            var cards = new Dictionary<int, OpGgCard>();
            if (game.Mode == Mode.Caos && data.ValueKind == JsonValueKind.Array)
                ReadCards(data, game.Mode, catalog, cards);
            else if (game.Mode == Mode.Arena && data.ValueKind == JsonValueKind.Object &&
                node[3].TryGetProperty("mode", out var mode) && mode.ValueKind == JsonValueKind.String && mode.ValueEquals("arena"))
            {
                foreach (var rarity in data.EnumerateObject())
                    if (rarity.Name is "1" or "4" or "8" && rarity.Value.ValueKind == JsonValueKind.Array)
                        ReadCards(rarity.Value, game.Mode, catalog, cards);
            }
            if (cards.Count > 0)
                return new OpGgSnapshot(patch, game.Mode, game.Champion, DateTimeOffset.UtcNow, cards,
                    sourcePatch == patch ? null : sourcePatch);
        }
        throw new InvalidDataException("OP.GG no publicó una tabla de aumentos reconocible.");
    }

    private static void ReadCards(JsonElement rows, Mode mode, IReadOnlyDictionary<int, Augment> catalog,
        Dictionary<int, OpGgCard> cards)
    {
        foreach (var row in rows.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object || !row.TryGetProperty("id", out var id) ||
                id.ValueKind != JsonValueKind.Number || !id.TryGetInt32(out var number) ||
                !catalog.TryGetValue(number, out var card))
                continue;
            OpGgCard value;
            if (mode == Mode.Caos && row.TryGetProperty("key", out var key) && key.ValueKind == JsonValueKind.String && key.ValueEquals(card.Key) &&
                row.TryGetProperty("tier", out var tier) && tier.ValueKind == JsonValueKind.Number &&
                tier.TryGetInt32(out var index) && index >= 0 && index < MayhemTiers.Length)
                value = new OpGgCard(Tier: MayhemTiers[index].ToString());
            else if (mode == Mode.Arena && row.TryGetProperty("pick_rate", out var pick) &&
                pick.ValueKind == JsonValueKind.Number && pick.TryGetDecimal(out var percent) && percent is >= 0 and <= 100)
                value = new OpGgCard(PickPercent: percent);
            else
                continue;
            if (!cards.TryAdd(number, value) && cards[number] != value)
                throw new InvalidDataException("OP.GG publicó dos valores para la misma carta.");
        }
    }
}

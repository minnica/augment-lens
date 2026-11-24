using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Aumentos;

internal sealed record ShopItemRate(decimal PickPercent, decimal WinPercent);
internal sealed record ShopItemRates(string Patch, DateTimeOffset FetchedAt, Dictionary<int, ShopItemRate> Items);

internal sealed class ShopItemStats(string patch, string settings, HttpClient http)
{
    internal const string MayhemUrl = "https://hexmayhem.com/items";
    private static readonly Regex MayhemPatch = Pattern(@"<title\b[^>]*>[^<]*item win rates\s+(\d+\.\d+)\b");
    private static readonly Regex Body = Pattern(@"<tbody\b[^>]*>(.*?)</tbody\s*>");
    private static readonly Regex Row = Pattern(@"<tr\b[^>]*>(.*?)</tr\s*>");
    private static readonly Regex Cell = Pattern(@"<td\b[^>]*>(.*?)</td\s*>");
    private static readonly Regex MayhemItem = Pattern(@"\bsrc=""/icons/items/(\d+)[_.]");
    private static readonly Regex Tags = Pattern(@"<[^>]+>");
    private static readonly Regex Percent = Pattern(@"^\s*(\d{1,3}(?:[.,]\d+)?)\s*%");
    private readonly object gate = new();
    private readonly Dictionary<GameContext, Entry> entries = [];

    private sealed class Entry
    {
        internal ShopItemRates? Data;
        internal DateTimeOffset Next;
        internal Task Work = Task.CompletedTask;
    }

    internal ShopItemRates? Current(GameContext game, CancellationToken cancellation)
    {
        if (game.Mode == Mode.Caos)
            game = new(Mode.Caos, "global");
        else if (!game.Champion.All(char.IsAsciiLetterOrDigit))
            return null;
        lock (gate)
        {
            if (!entries.TryGetValue(game, out var entry))
                entries.Add(game, entry = new());
            if (entry.Work.IsCompleted && DateTimeOffset.UtcNow >= entry.Next && !cancellation.IsCancellationRequested)
                entry.Work = Task.Run(() => Refresh(game, entry, cancellation));
            return entry.Data;
        }
    }

    private async Task Refresh(GameContext game, Entry entry, CancellationToken cancellation)
    {
        var path = Path.Combine(settings, "shop-item-stats", patch, $"{game.Mode}-{game.Champion.ToLowerInvariant()}.json");
        var data = entry.Data;
        if (data is null)
        {
            try
            {
                var cached = Ratings.Read<ShopItemRates>(path);
                if (Compatible(cached.Patch, patch) && cached.FetchedAt <= DateTimeOffset.UtcNow &&
                    cached.Items is { Count: > 0 } && cached.Items.All(pair => pair.Key > 0 &&
                        pair.Value is { PickPercent: >= 0 and <= 100, WinPercent: >= 0 and <= 100 }))
                    data = cached;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException) { }
        }

        var next = data is null ? (DateTimeOffset?)null : data.FetchedAt.AddHours(data.Patch == patch ? 12 : 1);
        lock (gate) entry.Data = data;
        if (next > DateTimeOffset.UtcNow)
        {
            lock (gate) entry.Next = next.Value;
            return;
        }

        bool refreshed = false;
        try
        {
            var address = game.Mode == Mode.Caos ? new Uri(MayhemUrl) : OpGgBuildPage.Address(game, "items");
            var html = await http.GetStringAsync(address, cancellation).ConfigureAwait(false);
            var updated = game.Mode == Mode.Caos ? ReadMayhemItems(html, patch) : OpGgBuildPage.ReadArenaItems(html, game, patch);
            if (updated.Items.Count > 0 && (data?.Patch != patch || updated.Patch == patch))
            {
                data = updated;
                StatsCache.Write(path, updated);
                refreshed = true;
            }
        }
        catch (Exception error) when (error is HttpRequestException or IOException or InvalidDataException or
            OperationCanceledException or RegexMatchTimeoutException) { }

        lock (gate)
        {
            entry.Data = data;
            entry.Next = DateTimeOffset.UtcNow.AddMinutes(!refreshed ? 15 : data!.Patch == patch ? 720 : 60);
        }
    }

    internal static ShopItemRates ReadMayhemItems(string html, string patch)
    {
        var sourcePatch = MayhemPatch.Match(html).Groups[1].Value;
        if (!Compatible(sourcePatch, patch))
            throw new InvalidDataException("La tabla de HexMayhem corresponde a otro parche.");
        var items = new Dictionary<int, ShopItemRate>();
        foreach (Match row in Row.Matches(Body.Match(html).Groups[1].Value))
        {
            var cells = Cell.Matches(row.Groups[1].Value);
            if (cells.Count != 6 || !int.TryParse(MayhemItem.Match(cells[1].Groups[1].Value).Groups[1].Value, out int id) ||
                id <= 0 || ReadPercent(Plain(cells[2])) is not { } win || ReadPercent(Plain(cells[3])) is not { } pick)
                continue;
            items.TryAdd(id, new(pick, win));
        }
        return new(sourcePatch, DateTimeOffset.UtcNow, items);
    }

    private static string Plain(Match cell) => WebUtility.HtmlDecode(Tags.Replace(cell.Groups[1].Value, " "));

    internal static decimal? ReadPercent(string value) =>
        decimal.TryParse(Percent.Match(value).Groups[1].Value.Replace(',', '.'), NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out var percent) && percent is >= 0 and <= 100 ? percent : null;

    private static bool Compatible(string source, string patch) =>
        source == patch || source == LeaguePatch.Previous(patch);

    private static Regex Pattern(string pattern) =>
        new(pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Aumentos;

internal sealed record MayhemChampionTierList(string SourcePatch, DateTimeOffset FetchedAt, Dictionary<string, int> Tiers)
{
    internal string? Letter(string champion) => Tiers.GetValueOrDefault(champion.ToLowerInvariant()) switch
    {
        1 => "S", 2 => "A", 3 => "B", 4 => "C", 5 => "D", _ => null
    };
}

internal sealed record MayhemChampionTierView(MayhemChampionTierList? Data, string Status);

internal sealed class MayhemChampionTiers(Catalog catalog, string settings, HttpClient http)
{
    internal static readonly Uri Address = new("https://op.gg/es-419/lol/modes/aram-mayhem");
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(12);
    private static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(15);
    private static readonly Regex Canonical = Pattern(@"<link\b[^>]*\brel=""canonical""[^>]*\bhref=""([^""]+)""");
    private static readonly Regex Title = Pattern(@"<title\b[^>]*>[^<]*?\b(?:parche|patch)\s+(\d+\.\d+)\b");
    private static readonly Regex Flight = Pattern(@"<script\b[^>]*>self\.__next_f\.push\((\[1,""[^<]*?)\)</script>");
    private static readonly Regex Champions = Pattern(@"""champions""\s*:\s*(\[\s*\{[^\]]+\])");

    private readonly HashSet<string> keys = catalog.Champions.Select(champion => champion.Key.ToLowerInvariant()).ToHashSet();
    private readonly string cache = Path.Combine(settings, "opgg-mayhem-tiers", catalog.Patch, "champion-tiers.json");
    private readonly object gate = new();
    private MayhemChampionTierView view = new(null, "Consultando tiers de OP.GG…");
    private Task work = Task.CompletedTask;
    private DateTimeOffset refreshAt;

    internal MayhemChampionTierView Current(CancellationToken cancellation)
    {
        lock (gate)
        {
            Start(cancellation);
            return view;
        }
    }

    internal async Task<MayhemChampionTierView> ForCaptureAsync(CancellationToken cancellation = default)
    {
        Task pending;
        lock (gate)
        {
            Start(cancellation);
            pending = work;
        }
        await pending.WaitAsync(cancellation).ConfigureAwait(false);
        lock (gate) return view;
    }

    private void Start(CancellationToken cancellation)
    {
        if (work.IsCompleted && DateTimeOffset.UtcNow >= refreshAt && !cancellation.IsCancellationRequested)
            work = Task.Run(() => Refresh(cancellation));
    }

    private async Task Refresh(CancellationToken cancellation)
    {
        var patch = catalog.Patch;
        var data = view.Data ?? ReadCache(cache, patch) ??
            ReadCache(Path.Combine(settings, "opgg-mayhem-tiers", LeaguePatch.Previous(patch) ?? patch, "champion-tiers.json"), patch);
        if (data is not null)
        {
            var next = data.FetchedAt + Interval(data, patch);
            Set(data, next);
            if (next > DateTimeOffset.UtcNow)
                return;
        }

        var retryAt = DateTimeOffset.UtcNow + RetryInterval;
        try
        {
            using var response = await http.GetAsync(Address, cancellation).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.TooManyRequests && response.Headers.RetryAfter is { } retry)
            {
                var requested = retry.Date ?? DateTimeOffset.UtcNow + (retry.Delta ?? RetryInterval);
                if (requested > retryAt) retryAt = requested;
            }
            response.EnsureSuccessStatusCode();
            var fetched = Read(await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false), patch, keys);
            if (data?.SourcePatch == patch && fetched.SourcePatch != patch)
                throw new InvalidDataException("OP.GG devolvió el parche anterior.");
            data = fetched;
            StatsCache.Write(cache, data);
            Set(data, data.FetchedAt + Interval(data, patch));
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Set(data, DateTimeOffset.UtcNow, "Consultando tiers de OP.GG…");
        }
        catch (Exception error) when (error is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException or
            JsonException or RegexMatchTimeoutException or OperationCanceledException)
        {
            Set(data, retryAt, "Tiers de OP.GG no disponibles");
        }
    }

    internal static MayhemChampionTierList Read(string html, string patch, IReadOnlySet<string> keys)
    {
        var sourcePatch = Title.Match(html).Groups[1].Value;
        if (Canonical.Match(html).Groups[1].Value != Address.AbsoluteUri || !IsCompatible(sourcePatch, patch))
            throw new InvalidDataException("OP.GG devolvió otra lista de campeones o un parche incompatible.");

        foreach (Match script in Flight.Matches(html))
        {
            var envelope = script.Groups[1].Value;
            if (!envelope.Contains("champions", StringComparison.Ordinal))
                continue;
            using var flight = JsonDocument.Parse(envelope);
            var payload = flight.RootElement[1].GetString() ?? "";
            foreach (Match array in Champions.Matches(payload))
            {
                using var rows = JsonDocument.Parse(array.Groups[1].Value);
                var tiers = new Dictionary<string, int>();
                foreach (var row in rows.RootElement.EnumerateArray())
                {
                    if (row.ValueKind != JsonValueKind.Object || !row.TryGetProperty("key", out var key) ||
                        key.ValueKind != JsonValueKind.String || key.GetString() is not { } champion || !keys.Contains(champion) ||
                        !row.TryGetProperty("tier", out var value) || value.ValueKind != JsonValueKind.Number ||
                        !value.TryGetInt32(out int tier) || tier is < 1 or > 5)
                        continue;
                    if (!tiers.TryAdd(champion, tier))
                        throw new InvalidDataException("OP.GG devolvió un campeón repetido.");
                }
                if (tiers.Count > 0)
                    return new(sourcePatch, DateTimeOffset.UtcNow, tiers);
            }
        }
        throw new InvalidDataException("OP.GG no publicó una lista de tiers reconocible.");
    }

    private MayhemChampionTierList? ReadCache(string path, string patch)
    {
        try
        {
            var data = Ratings.Read<MayhemChampionTierList>(path);
            return IsCompatible(data.SourcePatch, patch) && data.FetchedAt <= DateTimeOffset.UtcNow &&
                data.Tiers is { Count: > 0 } && data.Tiers.All(entry => keys.Contains(entry.Key) && entry.Value is >= 1 and <= 5)
                ? data : null;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException) { return null; }
    }

    private static Regex Pattern(string pattern) => new(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(2));

    private static bool IsCompatible(string? source, string patch) =>
        source is not null && (source == patch || source == LeaguePatch.Previous(patch));

    private static TimeSpan Interval(MayhemChampionTierList data, string patch) =>
        data.SourcePatch == patch ? RefreshInterval : RetryInterval;

    private void Set(MayhemChampionTierList? data, DateTimeOffset next, string status = "")
    {
        lock (gate)
        {
            view = new(data, data is null ? status : Caption(data, catalog.Patch));
            refreshAt = next;
        }
    }

    internal static string Caption(MayhemChampionTierList data, string patch) =>
        $"OP.GG · {(data.SourcePatch == patch ? "Parche" : "Parche anterior")} {LeaguePatch.Public(data.SourcePatch)}";
}

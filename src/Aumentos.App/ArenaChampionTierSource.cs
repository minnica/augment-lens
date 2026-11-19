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

internal sealed record ArenaChampionTierView(ArenaChampionTierList? Data, string Status);

internal sealed class ArenaChampionTierSource(Catalog catalog, string settings, HttpClient http)
{
    internal const string Attribution = GgezArenaPage.Attribution;
    internal static readonly Uri Address = new(GgezArenaPage.ArenaUrl);
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(12);
    private static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(15);

    private readonly Dictionary<string, string> aliases = ArenaChampionTierPage.Aliases(catalog);
    private readonly object gate = new();
    private ArenaChampionTierView view = new(null, $"Consultando {Attribution}…");
    private Task work = Task.CompletedTask;
    private DateTimeOffset nextCheck;

    internal ArenaChampionTierView Current(CancellationToken cancellation = default)
    {
        lock (gate)
        {
            Start(cancellation);
            return view;
        }
    }

    internal async Task<ArenaChampionTierView> ForCaptureAsync(CancellationToken cancellation = default)
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

    internal ArenaChampionTier? Find(ArenaChampionTierList? data, string? champion) =>
        data is not null && champion is not null && aliases.TryGetValue(Ratings.Normalize(champion), out var canonical)
            ? data.Champions.GetValueOrDefault(canonical) : null;

    private void Start(CancellationToken cancellation)
    {
        if (work.IsCompleted && DateTimeOffset.UtcNow >= nextCheck && !cancellation.IsCancellationRequested)
            work = Task.Run(() => Refresh(cancellation));
    }

    private async Task Refresh(CancellationToken cancellation)
    {
        var patch = catalog.Patch;
        var previous = LeaguePatch.Previous(patch);
        ArenaChampionTierList? data;
        lock (gate) data = view.Data;
        data ??= ReadCache(patch);
        if (data is null && previous is not null && ReadCache(previous) is { IsPrevious: false } saved)
            data = saved with { ClientPatch = patch };
        if (data is not null)
        {
            var freshUntil = data.FetchedAt + (data.IsPrevious ? RetryInterval : RefreshInterval);
            Set(data, Describe(data), freshUntil);
            if (freshUntil > DateTimeOffset.UtcNow)
                return;
        }

        DateTimeOffset retryAt = DateTimeOffset.MinValue;
        string errorStatus;
        try
        {
            using var response = await http.GetAsync(Address, cancellation).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.TooManyRequests && response.Headers.RetryAfter is { } retry)
                retryAt = retry.Date ?? DateTimeOffset.UtcNow + (retry.Delta ?? RetryInterval);
            response.EnsureSuccessStatusCode();
            var html = await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false);
            var fetched = ArenaChampionTierPage.Read(html, patch, aliases);
            if (data is { IsPrevious: false } && fetched.IsPrevious)
                throw new InvalidDataException($"{Attribution} devolvió una lista del parche anterior.");
            StatsCache.Write(CachePath(patch), fetched);
            Set(fetched, Describe(fetched), fetched.FetchedAt + (fetched.IsPrevious ? RetryInterval : RefreshInterval));
            return;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Set(data, data is null ? $"Consultando {Attribution}…" : Describe(data), DateTimeOffset.UtcNow);
            return;
        }
        catch (InvalidDataException error) { errorStatus = error.Message; }
        catch (Exception error) when (error is HttpRequestException or IOException or OperationCanceledException or
            RegexMatchTimeoutException or InvalidOperationException or FormatException)
        {
            errorStatus = $"No se pudo consultar la lista de Arena en {Attribution}.";
        }
        var next = DateTimeOffset.UtcNow + RetryInterval;
        Set(data, data is null ? errorStatus : $"{Describe(data)} · Copia guardada", retryAt > next ? retryAt : next);
    }

    private ArenaChampionTierList? ReadCache(string patch)
    {
        try
        {
            var data = Ratings.Read<ArenaChampionTierList>(CachePath(patch));
            return data.ClientPatch == patch && data.SourcePatch is not null &&
                (data.SourcePatch == patch || data.SourcePatch == LeaguePatch.Previous(patch)) &&
                data.FetchedAt <= DateTimeOffset.UtcNow && data.SampleMatches > 0 && data.Champions is { Count: > 0 } &&
                data.Champions.All(pair => pair.Value is { Tier: "S" or "A" or "B" or "C" or "D", Games: >= 20,
                        TopThreePercent: >= 0 and <= 100 } entry && entry.Games <= data.SampleMatches &&
                    pair.Key == entry.Champion && aliases.GetValueOrDefault(Ratings.Normalize(pair.Key)) == pair.Key)
                ? data : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            return null;
        }
    }

    private string CachePath(string patch) => Path.Combine(settings, "ggez-arena", patch, "champion-tiers.json");

    private static string Describe(ArenaChampionTierList data) =>
        $"{Attribution} · {(data.IsPrevious ? "Parche anterior" : "Parche")} {LeaguePatch.Public(data.SourcePatch)}";

    private void Set(ArenaChampionTierList? data, string status, DateTimeOffset next)
    {
        lock (gate)
        {
            view = new(data, status);
            nextCheck = next;
        }
    }
}

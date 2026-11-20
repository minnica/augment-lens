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

internal sealed record GgezArenaView(GgezArenaSnapshot? Data, string Status);

internal sealed class GgezArenaRatings
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(12);
    private static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(15);
    private readonly string patch;
    private readonly string? previousPatch;
    private readonly string directory;
    private readonly HttpClient client;
    private readonly HashSet<int> cardIds;
    private readonly Dictionary<string, string> champions;
    private readonly Dictionary<string, Entry> entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly object gate = new();
    private readonly SemaphoreSlim downloads = new(1);
    private DateTimeOffset nextRequest;
    private Uri? recentPage;

    private sealed class Entry(Uri address)
    {
        internal readonly Uri Address = address;
        internal GgezArenaView View = new(null, $"Consultando {GgezArenaPage.Attribution}…");
        internal Task Work = Task.CompletedTask;
        internal DateTimeOffset NextCheck;
    }

    internal GgezArenaRatings(Catalog catalog, string settings, HttpClient client)
    {
        patch = catalog.Patch;
        previousPatch = LeaguePatch.Previous(patch);
        directory = Path.Combine(settings, "ggez-arena", patch);
        this.client = client;
        cardIds = catalog.Augments.Where(card => card.Mode == nameof(Mode.Arena)).Select(card => card.Id).ToHashSet();
        champions = catalog.Champions.ToDictionary(item => item.Key, item => item.Key, StringComparer.OrdinalIgnoreCase);
    }

    internal string SourceUrl
    {
        get { lock (gate) return recentPage?.AbsoluteUri ?? GgezArenaPage.ArenaUrl; }
    }

    internal GgezArenaView? Current(GameContext game, CancellationToken cancellation)
    {
        lock (gate) return Start(game, cancellation)?.View;
    }

    internal async Task<GgezArenaView?> ForCaptureAsync(GameContext game, CancellationToken cancellation)
    {
        Entry? entry;
        Task? pending;
        lock (gate)
        {
            entry = Start(game, cancellation);
            pending = entry?.Work;
        }
        if (pending is null) return null;
        await pending.ConfigureAwait(false);
        lock (gate) return entry!.View;
    }

    private Entry? Start(GameContext game, CancellationToken cancellation)
    {
        if (game.Mode != Mode.Arena || !champions.TryGetValue(game.Champion, out var champion))
            return null;
        if (!entries.TryGetValue(champion, out var entry))
            entries.Add(champion, entry = new Entry(GgezArenaPage.Address(champion)));
        recentPage = entry.Address;
        if (entry.Work.IsCompleted && DateTimeOffset.UtcNow >= entry.NextCheck && !cancellation.IsCancellationRequested)
            entry.Work = Task.Run(() => Refresh(entry, champion, cancellation));
        return entry;
    }

    private async Task Refresh(Entry entry, string champion, CancellationToken cancellation)
    {
        const string attribution = GgezArenaPage.Attribution;
        var path = Path.Combine(directory, $"{champion}.json");
        var data = entry.View.Data ?? ReadCache(path, champion);
        var interval = data?.IsPrevious == true ? RetryInterval : RefreshInterval;
        if (data is { PlacementSchemaRead: true, ItemSchemaRead: true } && data.FetchedAt + interval > DateTimeOffset.UtcNow)
        {
            SetView(entry, data, Describe(data), data.FetchedAt + interval);
            return;
        }
        if (data is not null)
            SetView(entry, data, Describe(data), entry.NextCheck);

        DateTimeOffset retryAt = DateTimeOffset.MinValue;
        string status;
        try
        {
            await downloads.WaitAsync(cancellation).ConfigureAwait(false);
            try
            {
                var delay = nextRequest - DateTimeOffset.UtcNow;
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, cancellation).ConfigureAwait(false);
                nextRequest = DateTimeOffset.UtcNow.AddSeconds(1);
                using var response = await client.GetAsync(entry.Address, cancellation).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.TooManyRequests && response.Headers.RetryAfter is { } retry)
                    retryAt = retry.Date ?? DateTimeOffset.UtcNow + (retry.Delta ?? RetryInterval);
                response.EnsureSuccessStatusCode();
                var html = await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false);
                var fetched = GgezArenaPage.Read(html, champion, patch, cardIds);
                if (data is not null && !data.IsPrevious && fetched.IsPrevious)
                    throw new InvalidDataException($"{attribution} devolvió un parche anterior.");
                data = fetched;
                StatsCache.Write(path, data);
            }
            finally { downloads.Release(); }
            SetView(entry, data, Describe(data), data.FetchedAt + (data.IsPrevious ? RetryInterval : RefreshInterval));
            return;
        }
        catch (InvalidDataException error) { status = error.Message; }
        catch (HttpRequestException error)
        {
            status = $"{attribution} no disponible{(error.StatusCode is { } code ? $" ({(int)code})" : "")}.";
        }
        catch (OperationCanceledException) { status = $"Consulta de {attribution} interrumpida."; }
        catch (Exception error) when (error is IOException or RegexMatchTimeoutException or InvalidOperationException or FormatException)
        {
            status = $"No se pudo leer la tabla de {attribution}.";
        }
        var next = DateTimeOffset.UtcNow + RetryInterval;
        SetView(entry, data, data is null ? status : $"{Describe(data)} · Usando copia guardada", retryAt > next ? retryAt : next);
    }

    private GgezArenaSnapshot? ReadCache(string path, string champion)
    {
        try
        {
            var cached = Ratings.Read<GgezArenaSnapshot>(path);
            if (cached.ClientPatch != patch || cached.SourcePatch is null ||
                (cached.SourcePatch != patch && cached.SourcePatch != previousPatch) ||
                !string.Equals(cached.Champion, champion, StringComparison.OrdinalIgnoreCase) ||
                cached.FetchedAt > DateTimeOffset.UtcNow || cached.SampleGames <= 0 || cached.Picks is not { Count: > 0 } ||
                !cached.Picks.All(item => cardIds.Contains(item.Key) &&
                    item.Value is { Percent: >= 0 and <= 100, GamesPicked: >= 0 } && item.Value.GamesPicked <= cached.SampleGames))
                return null;

            var placementCount = cached.PlacementCount is >= 2 and <= 8 ? cached.PlacementCount : null;
            return cached with
            {
                PlacementCount = placementCount,
                Picks = cached.Picks.ToDictionary(item => item.Key, item => item.Value.ValidatePlacement(placementCount))
            };
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            return null;
        }
    }

    private static string Describe(GgezArenaSnapshot data) =>
        $"{GgezArenaPage.Attribution} · {(data.IsPrevious ? "Parche anterior" : "Parche")} {LeaguePatch.Public(data.SourcePatch)}";

    private void SetView(Entry entry, GgezArenaSnapshot? data, string status, DateTimeOffset next)
    {
        lock (gate)
        {
            entry.View = new(data, status);
            entry.NextCheck = next;
        }
    }
}

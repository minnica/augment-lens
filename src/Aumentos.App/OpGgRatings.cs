using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Aumentos;

internal sealed record OpGgView(OpGgSnapshot? Data, string Status, bool Loading = false);

internal sealed class OpGgRatings
{
    private static readonly TimeSpan FreshFor = TimeSpan.FromHours(12);
    private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(15);
    private readonly string directory;
    private readonly string patch;
    private readonly HttpClient client;
    private readonly Dictionary<Mode, Dictionary<int, Augment>> cards;
    private readonly HashSet<string> champions;
    private readonly Dictionary<GameContext, Entry> entries = [];
    private readonly object gate = new();

    private sealed class Entry
    {
        internal OpGgView View = new(null, "Consultando OP.GG…", Loading: true);
        internal DateTimeOffset NextCheck;
        internal Task Work = Task.CompletedTask;
    }

    internal OpGgRatings(Catalog catalog, string settings, HttpClient client)
    {
        patch = catalog.Patch;
        directory = Path.Combine(settings, "opgg", patch);
        this.client = client;
        cards = catalog.Augments.GroupBy(card => Enum.Parse<Mode>(card.Mode))
            .ToDictionary(group => group.Key, group => group.ToDictionary(card => card.Id));
        champions = catalog.Champions.Select(c => c.Key.ToLowerInvariant()).ToHashSet();
    }

    internal static HttpClient CreateClient()
    {
        var http = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All })
        {
            Timeout = TimeSpan.FromSeconds(12), MaxResponseContentBufferSize = 6 * 1024 * 1024
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("AumentosLoL/1.0");
        return http;
    }

    // The OCR loop only reads a completed snapshot. Disk access, HTTP, and page parsing run off that loop.
    internal OpGgView Current(GameContext game, CancellationToken cancellation)
    {
        lock (gate) return Start(game, cancellation).View;
    }

    internal async Task<OpGgView> ForCaptureAsync(GameContext game, CancellationToken cancellation)
    {
        Entry entry;
        Task work;
        lock (gate)
        {
            entry = Start(game, cancellation);
            work = entry.Work;
        }
        await work.ConfigureAwait(false);
        lock (gate) return entry.View;
    }

    private Entry Start(GameContext game, CancellationToken cancellation)
    {
        if (!entries.TryGetValue(game, out var entry))
            entries.Add(game, entry = new Entry());
        if (entry.Work.IsCompleted && DateTimeOffset.UtcNow >= entry.NextCheck && !cancellation.IsCancellationRequested)
        {
            if (entry.View.Data is null)
                entry.View = new(null, "Consultando OP.GG…", Loading: true);
            entry.Work = Task.Run(() => Refresh(game, entry, cancellation));
        }
        return entry;
    }

    private async Task Refresh(GameContext game, Entry entry, CancellationToken cancellation)
    {
        var slug = game.Champion.ToLowerInvariant();
        if (!champions.Contains(slug))
        {
            lock (gate)
            {
                entry.View = new(null, "OP.GG · Campeón sin datos");
                entry.NextCheck = DateTimeOffset.MaxValue;
            }
            return;
        }
        var path = Path.Combine(directory, $"{game.Mode}-{slug}.json");
        var previousPath = Path.Combine(directory, $"{game.Mode}-{slug}-previous.json");
        var data = entry.View.Data ?? ReadCache(path, game, previous: false) ??
            (game.Mode == Mode.Arena ? ReadCache(previousPath, game, previous: true) : null);
        var nextCheck = DateTimeOffset.UtcNow + RetryAfterFailure;
        DateTimeOffset? retryAt = null;
        string status;
        try
        {
            if (data is null || ExpiresAt(data) <= DateTimeOffset.UtcNow)
            {
                if (data is not null)
                    lock (gate) entry.View = new(data, "Actualizando la copia de OP.GG…");
                var fetched = await DownloadPreferred().ConfigureAwait(false);
                if (data is { IsPrevious: false } && fetched.IsPrevious)
                    throw new InvalidDataException($"OP.GG devolvió el parche anterior {LeaguePatch.Public(fetched.DataPatch)}.");
                data = fetched;
                StatsCache.Write(data.IsPrevious ? previousPath : path, data);
            }
            var period = data.IsPrevious ? $"Parche anterior {LeaguePatch.Public(data.DataPatch)}" : $"Parche {patch}";
            status = $"OP.GG · {period} · Actualizado {data.FetchedAt.ToLocalTime():dd/MM HH:mm}";
            nextCheck = ExpiresAt(data);
        }
        catch (InvalidDataException error) { status = error.Message; }
        catch (HttpRequestException error) { status = $"OP.GG no disponible{(error.StatusCode is { } code ? $" ({(int)code})" : "")}."; }
        catch (OperationCanceledException) { status = "Consulta de OP.GG interrumpida."; }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException)
        {
            status = "No se pudo leer el formato de OP.GG.";
        }
        if (data is not null && ExpiresAt(data) <= DateTimeOffset.UtcNow)
            status += $" Usando copia del {data.FetchedAt.ToLocalTime():dd/MM HH:mm}.";
        lock (gate)
        {
            entry.View = new(data, status);
            entry.NextCheck = retryAt is { } retry && retry > nextCheck ? retry : nextCheck;
        }

        async Task<OpGgSnapshot> DownloadPreferred()
        {
            var localized = OpGgPage.Address(game, localized: true);
            OpGgSnapshot first;
            try { first = await Download(OpGgPage.Address(game)).ConfigureAwait(false); }
            catch (InvalidDataException) { return await Download(localized).ConfigureAwait(false); }
            if (!first.IsPrevious)
                return first;
            try
            {
                var alternative = await Download(localized).ConfigureAwait(false);
                return alternative.IsPrevious ? first : alternative;
            }
            catch (Exception error) when (!cancellation.IsCancellationRequested && error is InvalidDataException or
                HttpRequestException or JsonException or InvalidOperationException or FormatException or OperationCanceledException)
            {
                return first;
            }
        }

        async Task<OpGgSnapshot> Download(Uri address)
        {
            using var response = await client.GetAsync(address, cancellation).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.TooManyRequests && response.Headers.RetryAfter is { } retry)
            {
                retryAt = retry.Date ?? DateTimeOffset.UtcNow + (retry.Delta ?? RetryAfterFailure);
            }
            response.EnsureSuccessStatusCode();
            var html = await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false);
            return OpGgPage.Read(html, game, patch, cards[game.Mode]);
        }
    }

    private static DateTimeOffset ExpiresAt(OpGgSnapshot data) =>
        data.FetchedAt + (data.IsPrevious ? RetryAfterFailure : FreshFor);

    private OpGgSnapshot? ReadCache(string path, GameContext game, bool previous)
    {
        try
        {
            var cached = Ratings.Read<OpGgSnapshot>(path);
            if (cached.Patch != patch || cached.Mode != game.Mode || cached.IsPrevious != previous ||
                (cached.IsPrevious && (game.Mode != Mode.Arena || cached.DataPatch != LeaguePatch.Previous(patch))) ||
                !string.Equals(cached.Champion, game.Champion, StringComparison.OrdinalIgnoreCase) ||
                cached.FetchedAt > DateTimeOffset.UtcNow || cached.Cards is not { Count: > 0 })
                return null;
            foreach (var (id, value) in cached.Cards)
            {
                if (!cards[game.Mode].ContainsKey(id) || value is null) return null;
                if (game.Mode == Mode.Caos && (value.Tier is not ("S" or "A" or "B" or "C" or "D" or "E") ||
                    value.PickPercent.HasValue)) return null;
                if (game.Mode == Mode.Arena && (value.Tier is not null || value.PickPercent is not (>= 0 and <= 100)))
                    return null;
            }
            return cached;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            return null;
        }
    }

}

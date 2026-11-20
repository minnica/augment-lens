using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Aumentos;

internal sealed record AramHexTier(Tier Tier, int Games);

internal sealed record AramHexSnapshot(string ClientPatch, string SourcePatch, int Sequence,
    DateTimeOffset FetchedAt, Dictionary<string, Dictionary<int, AramHexTier>> Champions)
{
    internal bool IsPrevious => ClientPatch != SourcePatch;
    internal AramHexTier? Find(string champion, int cardId) =>
        Champions.GetValueOrDefault(champion)?.GetValueOrDefault(cardId);
}

internal sealed record AramHexView(AramHexSnapshot? Data, string Status);

internal sealed class AramHexRatings
{
    internal const string Attribution = "AramHex";
    internal const string AttributionUrl = "https://github.com/czgreat/aramhex-data";
    private const string ExportBase = "https://raw.githubusercontent.com/czgreat/aramhex-data/";
    private const int MinimumGames = 300;
    private const int PriorGames = 1000;
    private const long FileLimit = 8 * 1024 * 1024;
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(12);
    private static readonly TimeSpan RetryInterval = TimeSpan.FromHours(1);

    private readonly string patch;
    private readonly string? previousPatch;
    private readonly string cachePath;
    private readonly HttpClient client;
    private readonly HashSet<string> champions;
    private readonly HashSet<int> cards;
    private readonly HashSet<int> previousCards;
    private readonly object gate = new();
    private AramHexView view = new(null, $"Consultando {Attribution}…");
    private Task work = Task.CompletedTask;
    private DateTimeOffset nextCheck;

    internal sealed record Compatibility(string ClientPatch, string SourcePatch, int[] Cards);
    private sealed record Manifest([property: JsonPropertyName("schema_version")] int Schema, string Patch,
        string Mode, string Path, int Seq, Dictionary<string, ExportFile> Files);
    private sealed record ExportFile(string Sha256, long Size);
    private sealed record ChampionIndex(Dictionary<string, ChampionId> Data);
    private sealed record ChampionId(string Key);
    internal sealed record MatchCounts(int Games, int Wins, [property: JsonPropertyName("low_sample")] bool? LowSample);

    internal AramHexRatings(Catalog catalog, string settings, HttpClient client)
        : this(catalog, settings, client, ReadCompatibility())
    {
    }

    internal AramHexRatings(Catalog catalog, string settings, HttpClient client, Compatibility? compatibility)
    {
        patch = catalog.Patch;
        previousPatch = LeaguePatch.Previous(patch);
        cachePath = Path.Combine(settings, "aramhex", $"{patch}.json");
        this.client = client;
        champions = catalog.Champions.Select(champion => champion.Key).ToHashSet(StringComparer.Ordinal);
        cards = catalog.Augments.Where(card => card.Mode == nameof(Mode.Caos)).Select(card => card.Id).ToHashSet();
        previousCards = compatibility is { Cards: not null } && compatibility.ClientPatch == patch &&
            compatibility.SourcePatch == previousPatch
            ? compatibility.Cards.Where(cards.Contains).ToHashSet() : [];
    }

    internal AramHexView Current(CancellationToken cancellation)
    {
        lock (gate)
        {
            Start(cancellation);
            return view;
        }
    }

    internal async Task<AramHexView> ForCaptureAsync(CancellationToken cancellation)
    {
        Task pending;
        lock (gate)
        {
            Start(cancellation);
            pending = work;
        }
        await pending.ConfigureAwait(false);
        lock (gate) return view;
    }

    private void Start(CancellationToken cancellation)
    {
        if (work.IsCompleted && DateTimeOffset.UtcNow >= nextCheck && !cancellation.IsCancellationRequested)
            work = Task.Run(() => Refresh(cancellation));
    }

    private async Task Refresh(CancellationToken cancellation)
    {
        var data = view.Data ?? ReadCache();
        if (data is not null)
        {
            var refresh = data.FetchedAt + (data.IsPrevious ? RetryInterval : RefreshInterval);
            SetView(data, Describe(data), refresh);
            if (refresh > DateTimeOffset.UtcNow) return;
        }

        DateTimeOffset retryAt = DateTimeOffset.UtcNow + RetryInterval;
        string status;
        try
        {
            using var request = await client.GetAsync(ExportBase + "main/latest.json", cancellation).ConfigureAwait(false);
            if (request.StatusCode == HttpStatusCode.TooManyRequests && request.Headers.RetryAfter is { } retry)
                retryAt = retry.Date ?? DateTimeOffset.UtcNow + (retry.Delta ?? RetryInterval);
            request.EnsureSuccessStatusCode();
            var manifest = await request.Content.ReadFromJsonAsync<Manifest>(Ratings.JsonOptions, cancellation)
                .ConfigureAwait(false);
            var allowed = manifest?.Patch == patch ? cards : manifest?.Patch == previousPatch ? previousCards : null;
            if (manifest is not { Schema: 1, Seq: > 0, Mode: "normal", Files: not null } || allowed is not { Count: > 0 } ||
                manifest.Path != $"patches/{manifest.Patch}/normal")
                throw new InvalidDataException($"{Attribution} · Sin tabla compatible para {LeaguePatch.Public(patch)}.");
            if (data is not null && (!data.IsPrevious && manifest.Patch != patch || manifest.Seq < data.Sequence))
                throw new InvalidDataException($"{Attribution} devolvió una versión anterior a la guardada.");

            var refreshed = data is not null && data.SourcePatch == manifest.Patch && data.Sequence == manifest.Seq
                ? data with { FetchedAt = DateTimeOffset.UtcNow }
                : await Download(manifest, allowed, cancellation).ConfigureAwait(false);
            StatsCache.Write(cachePath, refreshed);
            SetView(refreshed, Describe(refreshed), refreshed.FetchedAt + (refreshed.IsPrevious ? RetryInterval : RefreshInterval));
            return;
        }
        catch (InvalidDataException error) { status = error.Message; }
        catch (HttpRequestException error)
        {
            status = $"{Attribution} no disponible{(error.StatusCode is { } code ? $" ({(int)code})" : "")}.";
        }
        catch (OperationCanceledException) { status = $"Consulta de {Attribution} interrumpida."; }
        catch (Exception error) when (error is IOException or JsonException or NotSupportedException)
        {
            status = $"No se pudo leer la tabla de {Attribution}.";
        }
        SetView(data, data is null ? status : $"{Describe(data)} · Usando copia guardada", retryAt);
    }

    private async Task<AramHexSnapshot> Download(Manifest manifest, HashSet<int> allowed, CancellationToken cancellation)
    {
        var championFile = client.GetFromJsonAsync<ChampionIndex>(
            $"https://ddragon.leagueoflegends.com/cdn/{patch}.1/data/en_US/champion.json", Ratings.JsonOptions, cancellation);
        var baseCounts = DownloadFile<Dictionary<int, MatchCounts>>(manifest, "heroes.json", cancellation);
        var augmentCounts = DownloadFile<Dictionary<int, Dictionary<int, MatchCounts>>>(manifest, "hero_augments.json", cancellation);
        await Task.WhenAll(championFile, baseCounts, augmentCounts).ConfigureAwait(false);

        var result = new Dictionary<string, Dictionary<int, AramHexTier>>(StringComparer.Ordinal);
        if (await championFile is { Data: { } index })
        {
            var baselines = await baseCounts;
            var rows = await augmentCounts;
            foreach (var (champion, value) in index)
            {
                if (!champions.Contains(champion) || !int.TryParse(value?.Key, NumberStyles.None,
                    CultureInfo.InvariantCulture, out var id) || id <= 0 || !baselines.TryGetValue(id, out var baseline) ||
                    !rows.TryGetValue(id, out var outcomes) || outcomes is null)
                    continue;
                var tiers = new Dictionary<int, AramHexTier>();
                foreach (var (cardId, card) in outcomes)
                    if (allowed.Contains(cardId) && Rate(baseline, card) is { } tier)
                        tiers.Add(cardId, tier);
                if (tiers.Count > 0)
                    result.Add(champion, tiers);
            }
        }
        if (result.Count == 0)
            throw new InvalidDataException($"{Attribution} · Sin resultados utilizables para el catálogo.");
        return new AramHexSnapshot(patch, manifest.Patch, manifest.Seq, DateTimeOffset.UtcNow, result);
    }

    private async Task<T> DownloadFile<T>(Manifest manifest, string file, CancellationToken cancellation)
    {
        if (manifest.Files.GetValueOrDefault(file) is not { Size: > 0 and <= FileLimit, Sha256.Length: 64 } expected)
            throw new InvalidDataException($"{Attribution} · Falta el manifiesto de {file}.");
        var url = $"{ExportBase}data-v{manifest.Seq}/{manifest.Path}/{file}";
        using var request = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
        request.EnsureSuccessStatusCode();
        await request.Content.LoadIntoBufferAsync(expected.Size, cancellation).ConfigureAwait(false);
        var bytes = await request.Content.ReadAsByteArrayAsync(cancellation).ConfigureAwait(false);
        if (bytes.LongLength != expected.Size ||
            !Convert.ToHexString(SHA256.HashData(bytes)).Equals(expected.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"{Attribution} · {file} no coincide con el manifiesto.");
        return JsonSerializer.Deserialize<T>(bytes, Ratings.JsonOptions) ??
            throw new JsonException($"Archivo vacío: {file}.");
    }

    internal static AramHexTier? Rate(MatchCounts? baseline, MatchCounts? card)
    {
        if (baseline is not { LowSample: false } || card is not { LowSample: false, Games: >= MinimumGames, Wins: >= 0 } ||
            baseline.Games <= card.Games || baseline.Wins < card.Wins || card.Wins > card.Games ||
            baseline.Wins - card.Wins > baseline.Games - card.Games)
            return null;

        // Compare with the other games for this champion. A prior tempers differences from small samples.
        double reference = (baseline.Wins - card.Wins) / (double)(baseline.Games - card.Games);
        double observed = card.Wins / (double)card.Games;
        double adjusted = (observed - reference) * card.Games / (card.Games + (double)PriorGames);
        double samplingNoise = Math.Sqrt(.25 / card.Games + .25 / (baseline.Games - card.Games));
        double threshold = Math.Max(.02, 1.96 * samplingNoise);
        return new AramHexTier(adjusted >= threshold ? Tier.A : adjusted <= -threshold ? Tier.C : Tier.B, card.Games);
    }

    private AramHexSnapshot? ReadCache()
    {
        try
        {
            var cached = Ratings.Read<AramHexSnapshot>(cachePath);
            var allowed = cached.SourcePatch == patch ? cards : cached.SourcePatch == previousPatch ? previousCards : null;
            return cached.ClientPatch == patch && allowed is { Count: > 0 } && cached.Sequence > 0 &&
                cached.FetchedAt <= DateTimeOffset.UtcNow && cached.Champions is { Count: > 0 } &&
                cached.Champions.All(champion => champions.Contains(champion.Key) && champion.Value is { Count: > 0 } &&
                    champion.Value.All(card => allowed.Contains(card.Key) &&
                        card.Value is { Games: >= MinimumGames, Tier: Tier.A or Tier.B or Tier.C }))
                ? cached : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            return null;
        }
    }

    private static Compatibility? ReadCompatibility()
    {
        try { return Ratings.Read<Compatibility>(Path.Combine(AppContext.BaseDirectory, "Assets", "aramhex-compatibility.json")); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            return null;
        }
    }

    private static string Describe(AramHexSnapshot data) =>
        $"{Attribution} · China · {(data.IsPrevious ? "Parche anterior" : "Parche")} {LeaguePatch.Public(data.SourcePatch)}";

    private void SetView(AramHexSnapshot? data, string status, DateTimeOffset next)
    {
        lock (gate)
        {
            view = new AramHexView(data, status);
            nextCheck = next;
        }
    }
}

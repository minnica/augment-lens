using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Aumentos;

internal sealed record AramMayhemSnapshot(string ClientPatch, string Patch, DateOnly? StatsDate,
    DateTimeOffset FetchedAt, Dictionary<int, decimal> Picks)
{
    internal bool IsPrevious => Patch != LeaguePatch.Public(ClientPatch);
}

internal sealed record AramMayhemView(AramMayhemSnapshot? Data, string Status);

internal sealed class AramMayhemRatings
{
    internal const string Attribution = "ARAMMayhem.com";
    internal const string AttributionUrl = "https://arammayhem.com/data/";
    private const string ExportBase = AttributionUrl + "v1/";
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(12);
    private static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(15);
    private readonly string clientPatch;
    private readonly string patch;
    private readonly string? previousPatch;
    private readonly string cachePath;
    private readonly HttpClient client;
    private readonly HashSet<int> cardIds;
    private readonly Dictionary<string, int[]> names;
    private readonly Dictionary<string, int[]> keys;
    private readonly object gate = new();
    private AramMayhemView view = new(null, $"Consultando {Attribution}…");
    private Task work = Task.CompletedTask;
    private DateTimeOffset nextCheck;

    private sealed record Index(int SchemaVersion, string? LatestPatch, PatchEntry[]? Patches);
    private sealed record PatchEntry(string Patch);
    private sealed record Export(int SchemaVersion, ExportMetadata? Meta, ExportRow[]? Rows);
    private sealed record ExportMetadata(string Patch, DateOnly? StatsDate);
    private sealed record ExportRow(string? AugmentId, Dictionary<string, string>? Name, decimal? PickRate);

    internal AramMayhemRatings(Catalog catalog, string settings, HttpClient client)
    {
        clientPatch = catalog.Patch;
        patch = LeaguePatch.Public(clientPatch);
        previousPatch = LeaguePatch.Previous(patch);
        cachePath = Path.Combine(settings, "arammayhem", $"{clientPatch}.json");
        this.client = client;
        var cards = catalog.Augments.Where(card => card.Mode == nameof(Mode.Caos)).ToArray();
        cardIds = cards.Select(card => card.Id).ToHashSet();
        names = Lookup(cards.SelectMany(card => new[] { card.Name }.Concat(card.Aliases ?? [])
            .Select(name => (Ratings.Normalize(name), card.Id))));
        keys = Lookup(cards.Select(card => (NormalizeKey(card.Key), card.Id)));
    }

    internal AramMayhemView? Current(Mode mode, CancellationToken cancellation)
    {
        if (mode != Mode.Caos) return null;
        lock (gate)
        {
            Start(cancellation);
            return view;
        }
    }

    internal async Task<AramMayhemView?> ForCaptureAsync(Mode mode, CancellationToken cancellation)
    {
        if (mode != Mode.Caos) return null;
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
        var interval = data?.IsPrevious == true ? RetryInterval : RefreshInterval;
        if (data is not null && data.FetchedAt + interval > DateTimeOffset.UtcNow)
        {
            SetView(data, Describe(data), data.FetchedAt + interval);
            return;
        }

        if (data is not null)
            SetView(data, Describe(data), nextCheck);
        try
        {
            var index = await client.GetFromJsonAsync<Index>(ExportBase + "index.json", Ratings.JsonOptions, cancellation)
                .ConfigureAwait(false);
            if (index is not { SchemaVersion: 1, Patches: { } releases } ||
                !Version.TryParse(index.LatestPatch, out _))
                throw new InvalidDataException($"No se pudo leer el índice de {Attribution}.");
            var available = releases.OfType<PatchEntry>().Select(release => release.Patch).ToHashSet(StringComparer.Ordinal);
            available.Add(index.LatestPatch!);
            var selected = available.Contains(patch) ? patch :
                previousPatch is not null && available.Contains(previousPatch) ? previousPatch : null;
            if (selected is null)
                throw new InvalidDataException($"{Attribution} · Sin datos para {patch} ni para el parche anterior.");
            if (data is not null && !data.IsPrevious && selected != patch)
                throw new InvalidDataException($"{Attribution} · El parche {patch} falta en el índice.");

            var url = ExportBase + selected.Replace('.', '-') + "/augments.json";
            var export = await client.GetFromJsonAsync<Export>(url, Ratings.JsonOptions, cancellation).ConfigureAwait(false);
            if (export is not { SchemaVersion: 1, Meta: { } header, Rows: { } rows } || header.Patch != selected)
                throw new InvalidDataException($"{Attribution} · El archivo no corresponde al parche {selected}.");
            var picks = ReadPicks(rows);
            if (picks.Count == 0)
                throw new InvalidDataException($"{Attribution} · Sin aumentos reconocibles.");
            data = new AramMayhemSnapshot(clientPatch, selected, header.StatsDate, DateTimeOffset.UtcNow, picks);
            StatsCache.Write(cachePath, data);
            SetView(data, Describe(data), data.FetchedAt + (data.IsPrevious ? RetryInterval : RefreshInterval));
        }
        catch (InvalidDataException error) { Retry(data, error.Message); }
        catch (HttpRequestException error)
        {
            Retry(data, $"{Attribution} no disponible{(error.StatusCode is { } code ? $" ({(int)code})" : "")}.");
        }
        catch (OperationCanceledException) { Retry(data, $"Consulta de {Attribution} interrumpida."); }
        catch (IOException) { Retry(data, $"No se pudo descargar el archivo de {Attribution}."); }
        catch (Exception error) when (error is JsonException or InvalidOperationException or NotSupportedException)
        {
            Retry(data, $"No se pudo leer el formato de {Attribution}.");
        }
    }

    private AramMayhemSnapshot? ReadCache()
    {
        try
        {
            var cached = Ratings.Read<AramMayhemSnapshot>(cachePath);
            return cached.ClientPatch == clientPatch && cached.Patch is not null &&
                (cached.Patch == patch || cached.Patch == previousPatch) && cached.FetchedAt <= DateTimeOffset.UtcNow &&
                cached.Picks is { Count: > 0 } && cached.Picks.All(pick => cardIds.Contains(pick.Key) && pick.Value is >= 0 and <= 100)
                ? cached : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            return null;
        }
    }

    private Dictionary<int, decimal> ReadPicks(ExportRow[] rows)
    {
        var picks = new Dictionary<int, decimal>();
        var seen = new HashSet<int>();
        foreach (var row in rows)
        {
            if (row?.AugmentId is not { Length: > 0 } augmentId || row.PickRate is not (>= 0 and <= 100) ||
                ResolveCard(augmentId, row.Name?.GetValueOrDefault("es-MX")) is not { } id)
                continue;
            if (seen.Add(id))
                picks.Add(id, row.PickRate.Value);
            else
                picks.Remove(id);
        }
        return picks;
    }

    private int? ResolveCard(string augmentId, string? name)
    {
        var byName = name is null ? null : names.GetValueOrDefault(Ratings.Normalize(name));
        if (keys.TryGetValue(NormalizeKey(augmentId), out var byKey) && byKey is [var id])
            return byName is null || byName.Contains(id) ? id : null;
        return byName is [var only] ? only : null;
    }

    private static Dictionary<string, int[]> Lookup(IEnumerable<(string Value, int Id)> entries) =>
        entries.Where(entry => entry.Value.Length > 0).GroupBy(entry => entry.Value)
            .ToDictionary(group => group.Key, group => group.Select(entry => entry.Id).Distinct().ToArray());

    private static string NormalizeKey(string value)
    {
        if (value.StartsWith("ARAM_", StringComparison.OrdinalIgnoreCase))
            value = value[5..];
        return string.Concat(value.Where(char.IsAsciiLetterOrDigit)).ToLowerInvariant();
    }

    private static string Describe(AramMayhemSnapshot data) =>
        $"{Attribution} · {(data.IsPrevious ? "Parche anterior" : "Parche")} {data.Patch}" +
        (data.StatsDate is { } date ? $" · Estadísticas del {date:dd/MM}" : "");

    private void Retry(AramMayhemSnapshot? data, string status) =>
        SetView(data, data is null ? status : $"{Describe(data)} · Usando copia guardada", DateTimeOffset.UtcNow + RetryInterval);

    private void SetView(AramMayhemSnapshot? data, string status, DateTimeOffset next)
    {
        lock (gate)
        {
            view = new AramMayhemView(data, status);
            nextCheck = next;
        }
    }
}

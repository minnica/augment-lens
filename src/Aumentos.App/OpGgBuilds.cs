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

internal sealed record OpGgBuildView(OpGgBuildSnapshot? Data, string Status);

internal sealed class OpGgBuilds
{
    private static readonly TimeSpan FreshFor = TimeSpan.FromHours(12);
    private static readonly TimeSpan RecheckAfter = TimeSpan.FromMinutes(15);
    private static readonly OpGgBuildView Loading = new(null, "Consultando OP.GG…");
    private static readonly OpGgBuildView Unknown = new(null, "OP.GG · Campeón sin build disponible");
    private readonly string directory;
    private readonly string patch;
    private readonly string? previousPatch;
    private readonly HttpClient client;
    private readonly Dictionary<string, string> champions;
    private readonly Dictionary<GameContext, Entry> entries = [];
    private readonly object gate = new();
    private string sourceUrl = "https://op.gg/es-419/lol/modes/aram-mayhem";

    private sealed class Entry(GameContext game)
    {
        internal GameContext Game { get; } = game;
        internal Uri Address { get; } = OpGgBuildPage.Address(game);
        internal OpGgBuildView View = Loading;
        internal DateTimeOffset NextCheck;
        internal Task Work = Task.CompletedTask;
    }

    internal OpGgBuilds(Catalog catalog, string settings, HttpClient client)
    {
        patch = catalog.Patch;
        previousPatch = LeaguePatch.Previous(patch);
        directory = Path.Combine(settings, "opgg-builds", patch);
        this.client = client;
        champions = catalog.Champions.ToDictionary(champion => champion.Key, champion => champion.Key, StringComparer.OrdinalIgnoreCase);
    }

    internal string SourceUrl { get { lock (gate) return sourceUrl; } }

    internal OpGgBuildView Current(GameContext game, CancellationToken cancellation)
    {
        lock (gate) return Start(game, cancellation)?.View ?? Unknown;
    }

    internal async Task<OpGgBuildView> ForCaptureAsync(GameContext game, CancellationToken cancellation)
    {
        Entry? entry;
        Task? work;
        lock (gate)
        {
            entry = Start(game, cancellation);
            work = entry?.Work;
        }
        if (entry is null || work is null) return Unknown;
        await work.ConfigureAwait(false);
        lock (gate) return entry.View;
    }

    private Entry? Start(GameContext game, CancellationToken cancellation)
    {
        if (!champions.TryGetValue(game.Champion, out var champion))
            return null;
        game = game with { Champion = champion };
        if (!entries.TryGetValue(game, out var entry))
            entries.Add(game, entry = new Entry(game));
        sourceUrl = entry.Address.AbsoluteUri;
        if (entry.Work.IsCompleted && DateTimeOffset.UtcNow >= entry.NextCheck && !cancellation.IsCancellationRequested)
        {
            if (entry.View.Data is null) entry.View = Loading;
            entry.Work = Task.Run(() => Refresh(entry, cancellation));
        }
        return entry;
    }

    private async Task Refresh(Entry entry, CancellationToken cancellation)
    {
        var game = entry.Game;
        var path = Path.Combine(directory, $"{game.Mode}-{game.Champion.ToLowerInvariant()}.json");
        var data = entry.View.Data ?? ReadCache(path, game);
        if (data is not null)
        {
            var cachedUntil = data.FetchedAt + (data.IsPrevious ? RecheckAfter : FreshFor);
            if (cachedUntil > DateTimeOffset.UtcNow && (game.Mode != Mode.Caos || data.Spells is not null))
            {
                SetView(entry, data, Describe(data), cachedUntil);
                return;
            }
            SetView(entry, data, $"{Describe(data)} · Copia guardada", entry.NextCheck);
        }

        var nextCheck = DateTimeOffset.UtcNow + RecheckAfter;
        string status;
        try
        {
            using var response = await client.GetAsync(entry.Address, cancellation).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.TooManyRequests && response.Headers.RetryAfter is { } retry)
            {
                var retryAt = retry.Date ?? DateTimeOffset.UtcNow + (retry.Delta ?? RecheckAfter);
                if (retryAt > nextCheck) nextCheck = retryAt;
            }
            response.EnsureSuccessStatusCode();
            var html = await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false);
            var updated = OpGgBuildPage.Read(html, game, patch);
            if (data is { IsPrevious: false } && updated.IsPrevious)
            {
                SetView(entry, data, $"{Describe(data)} · Copia guardada", nextCheck);
                return;
            }
            if (updated.Groups.Length == 0 && data is { Groups.Length: > 0 } && data.SourcePatch == updated.SourcePatch)
                updated = updated with { Groups = data.Groups };
            data = updated;
            StatsCache.Write(path, data);
            SetView(entry, data, Describe(data), data.FetchedAt + (data.IsPrevious ? RecheckAfter : FreshFor));
            return;
        }
        catch (InvalidDataException error) { status = error.Message; }
        catch (HttpRequestException error)
        {
            status = $"Build de OP.GG no disponible{(error.StatusCode is { } code ? $" ({(int)code})" : "")}.";
        }
        catch (OperationCanceledException) { status = "Consulta de OP.GG interrumpida."; }
        catch (Exception error) when (error is IOException or InvalidOperationException or RegexMatchTimeoutException)
        {
            status = "No se pudo leer la build de OP.GG.";
        }
        SetView(entry, data, data is null ? status : $"{Describe(data)} · Copia guardada", nextCheck);
    }

    private OpGgBuildSnapshot? ReadCache(string path, GameContext game)
    {
        try
        {
            var data = Ratings.Read<OpGgBuildSnapshot>(path);
            if (data.Mode != game.Mode || data.ClientPatch != patch || data.SourcePatch is null ||
                (data.SourcePatch != patch && data.SourcePatch != previousPatch) ||
                !string.Equals(data.Champion, game.Champion, StringComparison.OrdinalIgnoreCase) ||
                data.FetchedAt > DateTimeOffset.UtcNow || data.Groups is not { Length: <= 4 } ||
                (data.Groups.Length == 0 && data.Spells is not { Length: > 0 }) ||
                (data.Spells is { } spells && (spells.Length > 2 || (game.Mode == Mode.Arena && spells.Length != 0) ||
                    spells.Any(pair => pair is null || !OpGgBuildPage.ValidSpell(pair.First) || !OpGgBuildPage.ValidSpell(pair.Second)))))
                return null;
            var sections = new HashSet<OpGgBuildSection>();
            foreach (var group in data.Groups)
            {
                if (group is null || !Enum.IsDefined(group.Section) || !sections.Add(group.Section) ||
                    (game.Mode == Mode.Caos && group.Section == OpGgBuildSection.Prismatic) ||
                    group.Choices is not { Length: >= 1 and <= 2 } ||
                    group.Choices.Any(choice => choice is not { Length: >= 1 and <= 6 } ||
                        choice.Any(item => item is not { Id: > 0, Name.Length: >= 1 and <= 120 })))
                    return null;
            }
            return data;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            return null;
        }
    }

    private static string Describe(OpGgBuildSnapshot data) =>
        $"OP.GG · {(data.IsPrevious ? "Parche anterior" : "Parche")} {LeaguePatch.Public(data.SourcePatch)}";

    private void SetView(Entry entry, OpGgBuildSnapshot? data, string status, DateTimeOffset nextCheck)
    {
        lock (gate)
        {
            entry.View = new(data, status);
            entry.NextCheck = nextCheck;
        }
    }
}

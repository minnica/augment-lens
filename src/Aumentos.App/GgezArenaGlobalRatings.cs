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

internal sealed class GgezArenaGlobalRatings(Catalog catalog, string settings, HttpClient http)
{
    internal static readonly Uri Address = new(GgezArenaPage.ArenaUrl + "/augments");
    private const string Attribution = GgezArenaPage.Attribution;
    private const int TeamCount = 6;
    private const int PlayersPerTeam = 3;
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(12);
    private static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(15);
    private static readonly Regex Table = new("""
        \bresult:\{\s*patch:"(\d{1,3}\.\d{1,3})",\s*total:(\d+),\s*matches:(\d+),\s*rows:\[(.*?)\]\s*\},\s*filters:\{\s*region:"all",\s*window:"patch"\s*\}
        """, RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(700));

    private readonly HashSet<int> cardIds = catalog.Augments.Where(card => card.Mode == nameof(Mode.Arena))
        .Select(card => card.Id).ToHashSet();
    private readonly string cachePath = Path.Combine(settings, "ggez-arena", catalog.Patch, "global-augments.json");
    private readonly object gate = new();
    private GgezArenaView view = new(null, $"Consultando aumentos globales de {Attribution}…");
    private Task work = Task.CompletedTask;
    private DateTimeOffset nextCheck;

    internal GgezArenaView Current(CancellationToken cancellation)
    {
        lock (gate)
        {
            Start(cancellation);
            return view;
        }
    }

    internal async Task<GgezArenaView> ForCaptureAsync(CancellationToken cancellation)
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

    internal static GgezArenaSnapshot Read(string html, string patch, IReadOnlySet<int> catalog)
    {
        if (!GgezArenaPage.IsCanonical(html, Address))
            throw new InvalidDataException($"{Attribution} devolvió otra página de aumentos de Arena.");
        var table = Table.Match(html);
        if (!table.Success)
            throw new InvalidDataException($"{Attribution} · Sin muestra global de Arena de un solo parche.");
        var sourcePatch = table.Groups[1].Value;
        if (sourcePatch != patch && sourcePatch != LeaguePatch.Previous(patch))
            throw new InvalidDataException($"{Attribution} · El parche global {LeaguePatch.Public(sourcePatch)} queda fuera de la ventana admitida.");

        // The provider lists teams of three and currently reports six full teams per match.
        const string teamPage = "href=\"/tierlist/arena/teams\"";
        if (!Positive(table.Groups[2], out int sampleGames) || !Positive(table.Groups[3], out int matches) ||
            sampleGames != (long)matches * TeamCount * PlayersPerTeam ||
            !html.Contains(teamPage, StringComparison.Ordinal))
            throw new InvalidDataException($"{Attribution} · Formato global de Arena sin confirmar.");

        var picks = GgezArenaPage.ReadPicks(table.Groups[4].Value, sampleGames, catalog, TeamCount);
        if (!picks.Values.Any(pick => pick.AveragePlace is not null && pick.Rarity is not null))
            throw new InvalidDataException($"{Attribution} · Sin posiciones globales de aumentos válidas.");
        return new("", patch, sourcePatch, DateTimeOffset.UtcNow, sampleGames, picks, TeamCount, PlacementSchemaRead: true);

        static bool Positive(Group group, out int value) =>
            int.TryParse(group.ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value > 0;
    }

    private void Start(CancellationToken cancellation)
    {
        if (work.IsCompleted && DateTimeOffset.UtcNow >= nextCheck && !cancellation.IsCancellationRequested)
            work = Task.Run(() => Refresh(cancellation));
    }

    private async Task Refresh(CancellationToken cancellation)
    {
        GgezArenaSnapshot? data;
        lock (gate) data = view.Data;
        data ??= ReadCache();
        if (data is not null)
        {
            var freshUntil = RefreshAt(data);
            Set(data, Describe(data), freshUntil);
            if (freshUntil > DateTimeOffset.UtcNow)
                return;
        }

        DateTimeOffset retryAt = DateTimeOffset.MinValue;
        string status;
        try
        {
            using var response = await http.GetAsync(Address, cancellation).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.TooManyRequests && response.Headers.RetryAfter is { } retry)
                retryAt = retry.Date ?? DateTimeOffset.UtcNow + (retry.Delta ?? RetryInterval);
            response.EnsureSuccessStatusCode();
            var html = await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false);
            var fetched = Read(html, catalog.Patch, cardIds);
            if (data is { IsPrevious: false } && fetched.IsPrevious)
                throw new InvalidDataException($"{Attribution} devolvió una muestra global del parche anterior.");
            StatsCache.Write(cachePath, fetched);
            Set(fetched, Describe(fetched), RefreshAt(fetched));
            return;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Set(data, data is null ? $"Consultando aumentos globales de {Attribution}…" : Describe(data), DateTimeOffset.UtcNow);
            return;
        }
        catch (InvalidDataException error) { status = error.Message; }
        catch (Exception error) when (error is HttpRequestException or IOException or OperationCanceledException or RegexMatchTimeoutException)
        {
            status = $"No se pudo consultar los aumentos globales de {Attribution}.";
        }
        var next = DateTimeOffset.UtcNow + RetryInterval;
        Set(data, data is null ? status : $"{Describe(data)} · Copia guardada", retryAt > next ? retryAt : next);
    }

    private GgezArenaSnapshot? ReadCache()
    {
        try
        {
            var data = Ratings.Read<GgezArenaSnapshot>(cachePath);
            if (data is not { Champion: "", PlacementSchemaRead: true, PlacementCount: TeamCount, SampleGames: > 0,
                    Picks.Count: > 0 } ||
                data.ClientPatch != catalog.Patch || data.SourcePatch is null ||
                (data.SourcePatch != catalog.Patch && data.SourcePatch != LeaguePatch.Previous(catalog.Patch)) ||
                data.FetchedAt > DateTimeOffset.UtcNow || data.SampleGames % (TeamCount * PlayersPerTeam) != 0 ||
                data.Picks.Any(pair => !cardIds.Contains(pair.Key) ||
                    pair.Value is not { Percent: >= 0 and <= 100, GamesPicked: >= 0 } pick ||
                    pick.GamesPicked > data.SampleGames ||
                    Math.Abs(pick.Percent - 100m * pick.GamesPicked / data.SampleGames) > 0.05m ||
                    pick.ValidatePlacement(TeamCount) != pick))
                return null;
            return data.Picks.Values.Any(pick => pick.AveragePlace is not null && pick.Rarity is not null) ? data : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            return null;
        }
    }

    private static DateTimeOffset RefreshAt(GgezArenaSnapshot data) =>
        data.FetchedAt + (data.IsPrevious ? RetryInterval : RefreshInterval);

    private static string Describe(GgezArenaSnapshot data) =>
        $"{Attribution} global · {(data.IsPrevious ? "Parche anterior" : "Parche")} {LeaguePatch.Public(data.SourcePatch)}";

    private void Set(GgezArenaSnapshot? data, string status, DateTimeOffset next)
    {
        lock (gate)
        {
            view = new(data, status);
            nextCheck = next;
        }
    }
}

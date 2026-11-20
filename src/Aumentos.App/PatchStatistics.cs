using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Aumentos;

internal sealed class PatchStatistics(Catalog catalog, string settings, OpGgRatings opgg, OpGgBuilds builds,
    GgezArenaRatings ggez, GgezArenaGlobalRatings ggezGlobal, ArenaChampionTierSource arenaTiers,
    ArenaTeamSource teams, MayhemChampionTiers mayhemTiers, AramMayhemRatings mayhem, AramHexRatings hex)
{
    private sealed record Progress(DateTimeOffset FirstSeen, int Day, DateTimeOffset LastAttempt, bool Review = true);
    private readonly string receipt = Path.Combine(settings, "patch-statistics", $"{catalog.Patch}.json");

    internal async Task Watch(Action<string> status, CancellationToken cancellation)
    {
        Progress? progress = null;
        while (!cancellation.IsCancellationRequested)
        {
            if (LeaguePatch.Installed() == catalog.Patch)
            {
                var now = DateTimeOffset.UtcNow;
                if (progress is null)
                {
                    progress = Read() ?? new Progress(now, 0, DateTimeOffset.MinValue);
                    StatsCache.Write(receipt, progress);
                }
                int day = RefreshDay(progress.FirstSeen, progress.Day, progress.LastAttempt, now, progress.Review);
                if (day > 0 && !PatchCatalog.MatchRunning())
                {
                    var (current, review) = await Refresh(status, cancellation).ConfigureAwait(false);
                    progress = progress with
                    {
                        Day = current || day == 2 && now >= progress.FirstSeen.AddDays(3) ? day : progress.Day,
                        LastAttempt = now,
                        Review = review
                    };
                    StatsCache.Write(receipt, progress);
                }
            }
            await Task.Delay(TimeSpan.FromMinutes(15), cancellation).ConfigureAwait(false);
        }
    }

    internal static int RefreshDay(DateTimeOffset first, int completed, DateTimeOffset lastAttempt, DateTimeOffset now, bool review = true)
    {
        if (now < lastAttempt.AddHours(12))
            return 0;
        var age = now - first;
        if (completed < 7 && review && age >= TimeSpan.FromDays(7) && age < TimeSpan.FromDays(10))
            return 7;
        return completed == 0 && age >= TimeSpan.FromDays(2) && age < TimeSpan.FromDays(7) ? 2 : 0;
    }

    private async Task<(bool Current, bool Review)> Refresh(Action<string> status, CancellationToken cancellation)
    {
        var arena = await arenaTiers.ForCaptureAsync(cancellation).ConfigureAwait(false);
        var trios = await teams.Prefetch(cancellation).ConfigureAwait(false);
        var augments = await ggezGlobal.ForCaptureAsync(cancellation).ConfigureAwait(false);
        var champions = await mayhemTiers.ForCaptureAsync(cancellation).ConfigureAwait(false);
        var popularity = await mayhem.ForCaptureAsync(Mode.Caos, cancellation).ConfigureAwait(false);
        var secondary = await hex.ForCaptureAsync(cancellation).ConfigureAwait(false);

        foreach (var game in RecentChampions())
        {
            cancellation.ThrowIfCancellationRequested();
            if (PatchCatalog.MatchRunning())
                break;
            var cards = await opgg.ForCaptureAsync(game, cancellation).ConfigureAwait(false);
            var build = await builds.ForCaptureAsync(game, cancellation).ConfigureAwait(false);
            if (game.Mode == Mode.Arena)
            {
                var observed = await ggez.ForCaptureAsync(game, cancellation).ConfigureAwait(false);
                if (CompareBuild(build.Data, observed?.Data) is { } comparison)
                    status($"{game.Champion} · {comparison}");
            }
            else if (cards.Data is { IsPrevious: false } published && secondary.Data is { IsPrevious: false } hexData &&
                hexData.Champions.TryGetValue(game.Champion, out var hexCards))
            {
                int shared = published.Cards.Count(pair => pair.Value.Tier is not null && hexCards.ContainsKey(pair.Key));
                if (shared > 0)
                    status($"{game.Champion} · {shared} aumentos cubiertos por OP.GG y AramHex · {LeaguePatch.Public(catalog.Patch)}");
            }
            await Task.Delay(600, cancellation).ConfigureAwait(false);
        }

        bool current = arena.Data?.SourcePatch == catalog.Patch && trios?.Patch == catalog.Patch &&
            augments.Data?.SourcePatch == catalog.Patch && champions.Data?.SourcePatch == catalog.Patch &&
            popularity?.Data?.IsPrevious == false;
        status($"Estadísticas {LeaguePatch.Public(catalog.Patch)} · {(current ? "Caché revisada" : "Fuentes pendientes del parche")} · " +
            $"{trios?.Teams.Count ?? 0} tríos · {secondary.Status}");
        bool review = !current || secondary.Data is not { IsPrevious: false } ||
            trios?.Teams.Values.Any(team => team.Games >= 50) != true;
        return (current, review);
    }

    internal static string? CompareBuild(OpGgBuildSnapshot? build, GgezArenaSnapshot? observed)
    {
        if (build is not { Mode: Mode.Arena, IsPrevious: false } || observed is not { IsPrevious: false, ItemGames.Count: > 0 } ||
            build.SourcePatch != observed.SourcePatch || !build.Champion.Equals(observed.Champion, StringComparison.OrdinalIgnoreCase))
            return null;
        var items = build.Groups.Where(group => group.Section is OpGgBuildSection.Core or OpGgBuildSection.Prismatic)
            .SelectMany(group => group.Choices.Take(1)).SelectMany(choice => choice).DistinctBy(item => item.Id).ToArray();
        if (items.Length == 0)
            return null;
        var overlap = items.Where(item => observed.ItemGames.ContainsKey(item.Id)).ToArray();
        string names = overlap.Length == 0 ? "" : $" · {string.Join(", ", overlap.Select(item => item.Name))}";
        return $"OP.GG y GGEZ.GG · {LeaguePatch.Public(build.SourcePatch)} · " +
            $"{overlap.Length}/{items.Length} objetos de las rutas principales también frecuentes en GGEZ.GG{names}";
    }

    private IEnumerable<GameContext> RecentChampions()
    {
        var champions = catalog.Champions.ToDictionary(champion => champion.Key, StringComparer.OrdinalIgnoreCase);
        var previous = LeaguePatch.Previous(catalog.Patch);
        return RecentFiles().OrderByDescending(file => file.LastWriteTimeUtc)
            .Select(file => Path.GetFileNameWithoutExtension(file.Name).Split('-'))
            .Where(name => name.Length is 2 or 3 && Enum.TryParse<Mode>(name[0], out _) && champions.ContainsKey(name[1]))
            .Select(name => new GameContext(Enum.Parse<Mode>(name[0]), champions[name[1]].Key)).Distinct().Take(6).ToArray();

        IEnumerable<FileInfo> RecentFiles()
        {
            foreach (var source in new[] { "opgg", "opgg-builds" })
            {
                foreach (var patch in new[] { catalog.Patch, previous }.OfType<string>())
                {
                    var folder = Path.Combine(settings, source, patch);
                    if (Directory.Exists(folder))
                        foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*.json"))
                            yield return file;
                }
            }
        }
    }

    private Progress? Read()
    {
        try
        {
            var progress = Ratings.Read<Progress>(receipt);
            return progress.FirstSeen <= DateTimeOffset.UtcNow && progress.Day is 0 or 2 or 7 ? progress : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            return null;
        }
    }
}

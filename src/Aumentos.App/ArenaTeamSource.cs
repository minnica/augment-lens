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

internal sealed record ArenaTeamResult(string Patch, int Games, decimal AveragePlace)
{
    internal string Tier => ((AveragePlace * Games + 3.5m * 50) / (Games + 50)) switch
    {
        <= 3.05m => "S", <= 3.32m => "A", < 3.68m => "B", < 3.95m => "C", _ => "D"
    };
}

internal sealed record ArenaTeamPage(string Patch, DateTimeOffset FetchedAt, int Matches,
    Dictionary<string, int> ChampionIds, Dictionary<string, ArenaTeamResult> Teams);

internal sealed class ArenaTeamSource(Catalog catalog, string settings, HttpClient http)
{
    internal static readonly Uri Address = new(GgezArenaPage.ArenaUrl + "/teams");
    internal const int MinimumGames = 20;
    private static readonly TimeSpan FreshFor = TimeSpan.FromHours(12);
    private static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(30);
    private static readonly Regex Table = Pattern("""
        \bresult:\{patch:"(\d{1,3}\.\d{1,3})",matches:(\d+),players:(\d+),teams:\[(.*?)\],pairs:\[.*?\]\},names:\{(.*?)\},filters:\{region:"all",window:"patch"\},champ:(null|\d+),min:(\d+)
        """);
    private static readonly Regex Name = Pattern("""
        "(\d+)":\{id:"([A-Za-z0-9]+)",name:"(?:[^"\\]|\\.)*",key:"\1"\}
        """);
    private static readonly Regex Row = Pattern("""
        \{ids:\[(\d+),(\d+),(\d+)\],top3:(\d+),first:(\d+),games:(\d+),avgPlace:([0-9.eE+\-]+),[^{}]*\}
        """);

    private readonly Dictionary<string, string> aliases = ArenaChampionTierPage.Aliases(catalog);
    private readonly Dictionary<int, PageState> pages = new();
    private readonly object gate = new();

    private sealed class PageState
    {
        internal ArenaTeamPage? Data;
        internal Task Work = Task.CompletedTask;
        internal DateTimeOffset Next;
    }

    internal ArenaTeamResult? Find(string? selected, ArenaDraftTeam? team, CancellationToken cancellation)
    {
        if (selected is null || team is not { Size: 3 } || !aliases.TryGetValue(Ratings.Normalize(selected), out var own))
            return null;
        var allies = team.Teammates.Select(champion => aliases.GetValueOrDefault(Ratings.Normalize(champion)))
            .OfType<string>().Where(champion => champion != own).Distinct(StringComparer.Ordinal).ToArray();
        if (allies.Length != 2)
            return null;
        var key = TeamKey(allies.Append(own));
        lock (gate)
        {
            var global = Start(0, cancellation).Data;
            if (global is null)
                return null;
            if (global.Teams.TryGetValue(key, out var result))
                return result;
            // Anchor the query on an ally so changing the preselection keeps using one cached list.
            int anchor = allies.Select(champion => global.ChampionIds.GetValueOrDefault(champion))
                .Where(id => id > 0).DefaultIfEmpty().Min();
            return anchor == 0 ? null : Start(anchor, cancellation).Data?.Teams.GetValueOrDefault(key);
        }
    }

    internal async Task<ArenaTeamPage?> Prefetch(CancellationToken cancellation)
    {
        PageState state;
        Task work;
        lock (gate) { state = Start(0, cancellation); work = state.Work; }
        await work.WaitAsync(cancellation).ConfigureAwait(false);
        lock (gate) return state.Data;
    }

    private PageState Start(int champion, CancellationToken cancellation)
    {
        if (!pages.TryGetValue(champion, out var state))
            pages.Add(champion, state = new());
        if (state.Work.IsCompleted && DateTimeOffset.UtcNow >= state.Next && !cancellation.IsCancellationRequested)
            state.Work = Task.Run(() => Refresh(champion, state, cancellation));
        return state;
    }

    private async Task Refresh(int champion, PageState state, CancellationToken cancellation)
    {
        var file = Path.Combine(settings, "ggez-arena", catalog.Patch,
            champion == 0 ? "teams.json" : $"teams-{champion}.json");
        var saved = ReadCache(file, champion);
        if (saved is not null)
        {
            Set(saved, saved.FetchedAt + FreshFor);
            if (saved.FetchedAt + FreshFor > DateTimeOffset.UtcNow)
                return;
        }
        var next = DateTimeOffset.UtcNow + RetryAfter;
        try
        {
            var url = $"{Address}?min={MinimumGames}" + (champion == 0 ? "" : $"&champ={champion}");
            using var response = await http.GetAsync(url, cancellation).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.TooManyRequests && response.Headers.RetryAfter is { } retry)
            {
                var offered = retry.Date ?? DateTimeOffset.UtcNow + (retry.Delta ?? RetryAfter);
                if (offered > next)
                    next = offered;
            }
            response.EnsureSuccessStatusCode();
            var fetched = Read(await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false),
                catalog.Patch, champion, aliases);
            StatsCache.Write(file, fetched);
            Set(fetched, fetched.FetchedAt + FreshFor);
        }
        catch (Exception error) when (error is HttpRequestException or IOException or InvalidDataException or
            OperationCanceledException or RegexMatchTimeoutException or InvalidOperationException)
        {
            lock (gate) state.Next = next;
        }

        void Set(ArenaTeamPage data, DateTimeOffset at)
        {
            lock (gate) { state.Data = data; state.Next = at; }
        }
    }

    private ArenaTeamPage? ReadCache(string file, int champion)
    {
        try
        {
            var data = Ratings.Read<ArenaTeamPage>(file);
            if (data.Patch == catalog.Patch && data.Matches > 0 && data.ChampionIds is { Count: > 0 } &&
                data.FetchedAt <= DateTimeOffset.UtcNow && data.Teams is not null && data.Teams.All(team =>
                    team.Value is { Games: >= MinimumGames, AveragePlace: >= 1 and <= 6 } result &&
                    result.Patch == data.Patch && result.Games <= data.Matches &&
                    team.Key.Split('|') is { Length: 3 } members && members.All(data.ChampionIds.ContainsKey) &&
                    (champion == 0 || members.Any(member => data.ChampionIds[member] == champion))))
                return data;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException) { }
        return null;
    }

    internal static ArenaTeamPage Read(string html, string patch, int champion, IReadOnlyDictionary<string, string> aliases)
    {
        var table = Table.Match(html);
        if (!GgezArenaPage.IsCanonical(html, Address) || !table.Success || table.Groups[1].Value != patch ||
            !Number(table.Groups[2], out int matches) || matches <= 0 || !Number(table.Groups[3], out int players) ||
            players != 18L * matches || table.Groups[6].Value != (champion == 0 ? "null" : champion.ToString(CultureInfo.InvariantCulture)) ||
            !Number(table.Groups[7], out int min) || min != MinimumGames)
            throw new InvalidDataException("GGEZ.GG no devolvió los tríos de Arena 3×6 del parche solicitado.");

        var names = new Dictionary<int, string>();
        foreach (Match name in Name.Matches(table.Groups[5].Value))
        {
            if (Number(name.Groups[1], out int id) && aliases.TryGetValue(Ratings.Normalize(name.Groups[2].Value), out var key))
                names.TryAdd(id, key);
        }
        if (names.Count == 0 || (!string.IsNullOrWhiteSpace(table.Groups[4].Value) && !Row.IsMatch(table.Groups[4].Value)))
            throw new InvalidDataException("GGEZ.GG no devolvió una tabla de tríos reconocible.");
        var teams = new Dictionary<string, ArenaTeamResult>(StringComparer.Ordinal);
        foreach (Match row in Row.Matches(table.Groups[4].Value))
        {
            if (!Number(row.Groups[1], out int firstId) || !Number(row.Groups[2], out int secondId) ||
                !Number(row.Groups[3], out int thirdId))
                continue;
            int[] ids = [firstId, secondId, thirdId];
            if (ids.Distinct().Count() != 3 || ids.Any(id => !names.ContainsKey(id)) ||
                champion != 0 && !ids.Contains(champion) || !Number(row.Groups[6], out int games) || games < MinimumGames || games > matches ||
                !Number(row.Groups[4], out int topThree) || !Number(row.Groups[5], out int first) || first > topThree || topThree > games ||
                !decimal.TryParse(row.Groups[7].ValueSpan, NumberStyles.Float, CultureInfo.InvariantCulture, out var average) ||
                average is < 1 or > 6)
                continue;
            var key = TeamKey(ids.Select(id => names[id]));
            var result = new ArenaTeamResult(patch, games, average);
            if (!teams.TryAdd(key, result) && teams[key] != result)
                throw new InvalidDataException("GGEZ.GG devolvió dos resultados para el mismo trío.");
        }
        var championIds = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var pair in names)
            championIds.TryAdd(pair.Value, pair.Key);
        return new(patch, DateTimeOffset.UtcNow, matches, championIds, teams);
    }

    internal static string TeamKey(IEnumerable<string> champions) => string.Join('|', champions.Order(StringComparer.Ordinal));

    private static bool Number(Group group, out int value) =>
        int.TryParse(group.ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out value);

    private static Regex Pattern(string expression) =>
        new(expression, RegexOptions.CultureInvariant | RegexOptions.Singleline, TimeSpan.FromMilliseconds(700));
}

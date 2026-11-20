using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Aumentos;

internal record GameContext(Mode Mode, string Champion);
internal sealed record MayhemOpponent(string Champion, int[] Items);
internal sealed record MayhemMatch(int[] Items, MayhemOpponent[] Opponents);
internal sealed record ArenaParticipant(string Champion, int[] Items);
internal sealed record ArenaMatch(int[] Items, ArenaParticipant[] Opponents, bool InventoryKnown = true,
    ArenaParticipant[]? Roster = null);
internal sealed record LiveMatchState(Mode Mode, LiveMatch? Match = null);
internal sealed record LiveMatch(GameContext Game, MayhemMatch? Mayhem = null, ArenaMatch? Arena = null,
    double? GameTime = null)
{
    internal bool IsNewSession(LiveMatch previous) =>
        Game != previous.Game || (GameTime is { } time && previous.GameTime is { } earlier && time + 5 < earlier);
}

internal sealed class LiveGame : IDisposable
{
    private readonly X509Certificate2 root;
    private readonly HttpClient client;

    public LiveGame(string rootPath)
    {
        root = X509Certificate2.CreateFromPem(File.ReadAllText(rootPath));
        client = new HttpClient(LocalHandler(root, 2999))
        {
            BaseAddress = new Uri("https://127.0.0.1:2999/liveclientdata/"),
            Timeout = TimeSpan.FromMilliseconds(750)
        };
    }

    internal static HttpClientHandler LocalHandler(X509Certificate2 root, int port) => new()
    {
        UseProxy = false,
        AllowAutoRedirect = false,
        ServerCertificateCustomValidationCallback = (request, certificate, _, errors) =>
            ValidateLocal(request, certificate, errors, root, port)
    };

    private static bool ValidateLocal(HttpRequestMessage request, X509Certificate2? certificate, SslPolicyErrors errors,
        X509Certificate2 root, int port)
    {
        if (request.RequestUri is not { Scheme: "https", Host: "127.0.0.1" } address || address.Port != port || certificate is null)
            return false;
        if (errors == SslPolicyErrors.None)
            return true;
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(root);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.DisableCertificateDownloads = true;
        return chain.Build(certificate);
    }

    internal async Task<GameContext?> Read(CancellationToken cancellation) =>
        (await ReadMatch(cancellation).ConfigureAwait(false))?.Match?.Game;

    internal Task<LiveMatchState?> ReadMatch(CancellationToken cancellation) => ReadMatch(client, cancellation);

    internal static async Task<LiveMatchState?> ReadMatch(HttpClient client, CancellationToken cancellation)
    {
        using var game = await ReadJson("gamestats").ConfigureAwait(false);
        var mode = ParseMode(Identity(game.RootElement, "gameMode"));
        if (mode is null)
            return null;
        var gameTime = ReadGameTime(game.RootElement);
        if (mode == Mode.Arena)
        {
            try
            {
                using var snapshot = await ReadJson("allgamedata").ConfigureAwait(false);
                if (ParseArenaMatch(snapshot.RootElement) is { } arena)
                    return new(mode.Value, arena with { GameTime = arena.GameTime ?? gameTime });
            }
            catch (Exception error) when (!cancellation.IsCancellationRequested &&
                error is HttpRequestException or JsonException or TaskCanceledException) { }
        }

        try
        {
            using var name = await ReadJson("activeplayername").ConfigureAwait(false);
            using var players = await ReadJson("playerlist").ConfigureAwait(false);
            var active = name.RootElement.ValueKind == JsonValueKind.String ? name.RootElement.GetString() : null;
            var match = ParseMatch(mode.Value, players.RootElement, active);
            return new(mode.Value, match is null ? null : match with { GameTime = gameTime });
        }
        catch (Exception error) when (!cancellation.IsCancellationRequested &&
            error is HttpRequestException or JsonException or TaskCanceledException)
        {
            return new(mode.Value);
        }

        async Task<JsonDocument> ReadJson(string path)
        {
            using var response = await client.GetAsync(path, cancellation).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false);
            return JsonDocument.Parse(json);
        }
    }

    internal static Mode? ParseMode(string? mode) => mode switch
    {
        "KIWI" => Mode.Caos,
        "CHERRY" => Mode.Arena,
        _ => null
    };

    internal static LiveMatch? ParseMatch(Mode mode, JsonElement players, string? active)
    {
        if (string.IsNullOrWhiteSpace(active) || players.ValueKind != JsonValueKind.Array)
            return null;
        var roster = players.EnumerateArray().Where(player => player.ValueKind == JsonValueKind.Object).ToArray();
        if (mode == Mode.Arena)
        {
            var aliases = ArenaAliases(roster);
            if (!aliases.TryGetValue(active, out int current) || ChampionKey(roster[current]) is not { } champion)
                return null;
            var items = ReadItems(roster[current]);
            var participants = roster.Select(ReadArenaParticipant).OfType<ArenaParticipant>().ToArray();
            return new(new(mode, champion), Arena: new(items ?? [], [], items is not null, participants));
        }

        foreach (var player in roster)
        {
            if (Identity(player, "riotId") != active && Identity(player, "summonerName") != active ||
                ChampionKey(player) is not { } champion)
                continue;
            var game = new GameContext(mode, champion);
            var team = Identity(player, "team");
            if (team is not ("ORDER" or "CHAOS") || ReadItems(player) is not { } owned)
                return new(game);

            var enemyTeam = team == "ORDER" ? "CHAOS" : "ORDER";
            var opponents = players.EnumerateArray().Where(rival => Identity(rival, "team") == enemyTeam)
                .Select(rival => (Champion: ChampionKey(rival), Items: ReadItems(rival)))
                .Where(rival => rival.Champion is not null)
                .Select(rival => new MayhemOpponent(rival.Champion!, rival.Items ?? []))
                .ToArray();
            return new(game, new MayhemMatch(owned, opponents));
        }
        return null;
    }

    internal static LiveMatch? ParseArenaMatch(JsonElement snapshot)
    {
        if (snapshot.ValueKind != JsonValueKind.Object ||
            !snapshot.TryGetProperty("gameData", out var game) || ParseMode(Identity(game, "gameMode")) != Mode.Arena ||
            !snapshot.TryGetProperty("activePlayer", out var active) ||
            !snapshot.TryGetProperty("allPlayers", out var players) || players.ValueKind != JsonValueKind.Array)
            return null;

        var roster = players.EnumerateArray().Where(player => player.ValueKind == JsonValueKind.Object).ToArray();
        var aliases = ArenaAliases(roster);
        var identity = ArenaIdentity(active);
        if (identity is null || !aliases.TryGetValue(identity, out int current) ||
            ChampionKey(roster[current]) is not { } champion)
            return null;

        var context = new GameContext(Mode.Arena, champion);
        var items = ReadItems(roster[current]);
        var participants = roster.Select(ReadArenaParticipant).ToArray();
        if (!snapshot.TryGetProperty("events", out var eventData) || eventData.ValueKind != JsonValueKind.Object ||
            !eventData.TryGetProperty("Events", out var events) || events.ValueKind != JsonValueKind.Array)
            return CreateMatch([]);

        var kills = ReadArenaKills(events, aliases).ToArray();
        var teams = Enumerable.Range(0, roster.Length).ToArray();
        foreach (var kill in kills)
            foreach (int ally in kill.Assisters)
                teams[TeamOf(ally)] = TeamOf(kill.Killer);

        // Arena's roster can report ORDER for every player. Only joint kills establish team ties.
        var conflicting = kills.Where(kill => TeamOf(kill.Killer) == TeamOf(kill.Victim))
            .Select(kill => TeamOf(kill.Killer)).ToHashSet();
        int ownTeam = TeamOf(current);
        if (conflicting.Contains(ownTeam))
            return CreateMatch([]);

        var lastSeen = new Dictionary<int, double>();
        foreach (var kill in kills)
        {
            int killerTeam = TeamOf(kill.Killer), victimTeam = TeamOf(kill.Victim);
            if (killerTeam != ownTeam && victimTeam != ownTeam)
                continue;
            int opponentTeam = killerTeam == ownTeam ? victimTeam : killerTeam;
            if (!conflicting.Contains(opponentTeam) && kill.Time > lastSeen.GetValueOrDefault(opponentTeam, -1))
                lastSeen[opponentTeam] = kill.Time;
        }

        var opponents = participants.Select((player, index) => (Player: player, Team: TeamOf(index)))
            .Where(player => lastSeen.ContainsKey(player.Team))
            .OrderByDescending(player => lastSeen[player.Team])
            .Select(player => player.Player).OfType<ArenaParticipant>().ToArray();
        return CreateMatch(opponents);

        LiveMatch CreateMatch(ArenaParticipant[] rivals) => new(context,
            Arena: new(items ?? [], rivals, items is not null, participants.OfType<ArenaParticipant>().ToArray()),
            GameTime: ReadGameTime(game));

        int TeamOf(int index)
        {
            while (teams[index] != index)
                index = teams[index];
            return index;
        }
    }

    private static ArenaParticipant? ReadArenaParticipant(JsonElement player) => ChampionKey(player) is { } champion
        ? new(champion, ReadItems(player) ?? []) : null;

    private sealed record ArenaKill(int Killer, int Victim, int[] Assisters, double Time);

    private static IEnumerable<ArenaKill> ReadArenaKills(JsonElement events, IReadOnlyDictionary<string, int> aliases)
    {
        foreach (var gameEvent in events.EnumerateArray())
        {
            if (Identity(gameEvent, "EventName") != "ChampionKill" ||
                Identity(gameEvent, "KillerName") is not { } killer || !aliases.TryGetValue(killer, out int killerIndex) ||
                Identity(gameEvent, "VictimName") is not { } victim || !aliases.TryGetValue(victim, out int victimIndex) ||
                killerIndex == victimIndex || !gameEvent.TryGetProperty("EventTime", out var eventTime) ||
                eventTime.ValueKind != JsonValueKind.Number || !eventTime.TryGetDouble(out double time) ||
                !double.IsFinite(time) || time < 0)
                continue;

            var assisters = gameEvent.TryGetProperty("Assisters", out var names) && names.ValueKind == JsonValueKind.Array
                ? names.EnumerateArray().Where(name => name.ValueKind == JsonValueKind.String)
                    .Select(name => aliases.GetValueOrDefault(name.GetString()!, -1))
                    .Where(index => index >= 0 && index != killerIndex && index != victimIndex).Distinct().ToArray()
                : [];
            yield return new(killerIndex, victimIndex, assisters, time);
        }
    }

    private static Dictionary<string, int> ArenaAliases(JsonElement[] roster) => roster
        .SelectMany((player, index) => PlayerNames(player).Select(name => (name, index)))
        .GroupBy(alias => alias.name, StringComparer.OrdinalIgnoreCase)
        .Where(group => group.Select(alias => alias.index).Distinct().Count() == 1)
        .ToDictionary(group => group.Key, group => group.First().index, StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<string> PlayerNames(JsonElement player) =>
        new[] { ArenaIdentity(player), Identity(player, "summonerName"), Identity(player, "riotIdGameName") }
            .OfType<string>().Where(name => !string.IsNullOrWhiteSpace(name));

    private static string? ArenaIdentity(JsonElement player)
    {
        var riotId = Identity(player, "riotId");
        if (!string.IsNullOrWhiteSpace(riotId))
            return riotId;
        var name = Identity(player, "riotIdGameName");
        var tag = Identity(player, "riotIdTagLine");
        if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(tag))
            return $"{name}#{tag}";
        var summoner = Identity(player, "summonerName");
        return string.IsNullOrWhiteSpace(summoner) ? name : summoner;
    }

    private static double? ReadGameTime(JsonElement game) =>
        game.ValueKind == JsonValueKind.Object && game.TryGetProperty("gameTime", out var elapsed) &&
        elapsed.ValueKind == JsonValueKind.Number && elapsed.TryGetDouble(out var seconds) &&
        double.IsFinite(seconds) && seconds >= 0 ? seconds : null;

    private static string? ChampionKey(JsonElement player)
    {
        const string prefix = "game_character_displayname_";
        var name = Identity(player, "rawChampionName");
        return name?.Length > prefix.Length && name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? name[prefix.Length..] : null;
    }

    private static int[]? ReadItems(JsonElement player)
    {
        if (!player.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            return null;
        var ids = new List<int>();
        foreach (var item in items.EnumerateArray())
            if (item.ValueKind == JsonValueKind.Object &&
                item.TryGetProperty("itemID", out var id) && id.ValueKind == JsonValueKind.Number &&
                id.TryGetInt32(out var number) && number > 0)
                ids.Add(number);
        return ids.ToArray();
    }

    private static string? Identity(JsonElement player, string field) =>
        player.ValueKind == JsonValueKind.Object && player.TryGetProperty(field, out var value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    public void Dispose()
    {
        client.Dispose();
        root.Dispose();
    }
}

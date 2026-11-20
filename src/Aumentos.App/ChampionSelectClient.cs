using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Aumentos;

internal sealed record ChampionSelection(string Champion, string? FirstSpell, string? SecondSpell);
internal sealed record ArenaDraftTeam(string[] Teammates, string[] Unavailable, int Size = 0);
internal sealed record ChampionSelectSession(Mode Mode, ChampionSelection? Pick = null,
    string? ArenaChampion = null, ArenaDraftTeam? ArenaTeam = null);

internal sealed class ChampionSelectClient : IDisposable
{
    private const string SelectionEndpoint = "/lol-champ-select/v1/session";
    private readonly X509Certificate2 root;
    private HttpClient? client;
    private string? install;
    private int processId;
    private int port;
    private AuthenticationHeaderValue? credential;
    private Dictionary<int, string>? champions;
    private Dictionary<int, string>? spells;

    internal ChampionSelectClient(string rootPath) =>
        root = X509Certificate2.CreateFromPem(File.ReadAllText(rootPath));

    internal async Task<ChampionSelectSession?> Read(CancellationToken cancellation)
    {
        var connection = Open();
        if (connection is null) return null;
        using var flow = await Get(connection, "/lol-gameflow/v1/session", cancellation).ConfigureAwait(false);
        if (flow is null || SelectMode(flow.RootElement) is not { } mode)
            return null;
        if (mode == Mode.Arena)
        {
            var (champion, team) = await ReadArenaDraft(connection, cancellation).ConfigureAwait(false);
            return new ChampionSelectSession(mode, ArenaChampion: champion, ArenaTeam: team);
        }

        var pick = await ReadMayhemPick(connection, cancellation).ConfigureAwait(false);
        return new ChampionSelectSession(mode, pick);
    }

    private async Task<(string? Champion, ArenaDraftTeam? Team)> ReadArenaDraft(HttpClient connection, CancellationToken cancellation)
    {
        try
        {
            using var selection = await Get(connection, SelectionEndpoint, cancellation).ConfigureAwait(false);
            if (selection is null)
                return default;
            var (champion, teammates, unavailable, teamSize) = ArenaDraftIds(selection.RootElement);
            if (champion is null && teammates.Length == 0 && unavailable.Length == 0)
                return default;
            if (await ChampionNames(connection, cancellation).ConfigureAwait(false) is not { } names)
                return default;

            return (champion is { } id ? names.GetValueOrDefault(id) : null,
                new ArenaDraftTeam(Keys(teammates), Keys(unavailable), teamSize));

            string[] Keys(int[] ids) => ids.Select(id => names.GetValueOrDefault(id)).OfType<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }
        catch (Exception error) when (!cancellation.IsCancellationRequested &&
            error is IOException or HttpRequestException or JsonException or OperationCanceledException)
        {
            return default;
        }
    }

    private async Task<ChampionSelection?> ReadMayhemPick(HttpClient connection, CancellationToken cancellation)
    {
        using var selection = await Get(connection, SelectionEndpoint, cancellation).ConfigureAwait(false);
        if (selection is null || OwnPick(selection.RootElement) is not { } pick)
            return null;

        var names = await ChampionNames(connection, cancellation).ConfigureAwait(false);
        var key = names?.GetValueOrDefault(pick.Champion);
        if (spells is null)
        {
            using var spellData = await Get(connection, "/lol-game-data/assets/v1/summoner-spells.json", cancellation).ConfigureAwait(false);
            spells = ReadNames(spellData, "name");
        }
        return key is not null
            ? new ChampionSelection(key, spells?.GetValueOrDefault(pick.Spell1), spells?.GetValueOrDefault(pick.Spell2))
            : null;
    }

    private async Task<Dictionary<int, string>?> ChampionNames(HttpClient connection, CancellationToken cancellation)
    {
        if (champions is null)
        {
            using var championData = await Get(connection, "/lol-game-data/assets/v1/champion-summary.json", cancellation).ConfigureAwait(false);
            champions = ReadNames(championData, "alias");
        }
        return champions;
    }

    internal static Mode? SelectMode(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !TextIs(root, "phase", "ChampSelect") ||
            !root.TryGetProperty("gameData", out var data) || data.ValueKind != JsonValueKind.Object)
            return null;
        if (data.TryGetProperty("queue", out var queue) && queue.ValueKind == JsonValueKind.Object)
        {
            if (Number(queue, "id") == 2400)
                return Mode.Caos;
            if (Number(queue, "id") is 1700 or 1710 || TextIs(queue, "gameMode", "CHERRY"))
                return Mode.Arena;
        }
        return TextIs(data, "gameMode", "CHERRY") ? Mode.Arena : null;
    }

    private static bool TextIs(JsonElement root, string key, string expected) =>
        root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String && value.ValueEquals(expected);

    internal static (int Champion, int Spell1, int Spell2)? OwnPick(JsonElement root)
    {
        if (OwnPlayer(root) is not { } player)
            return null;
        return Number(player, "championId") is > 0 and var champion
            ? (champion, Number(player, "spell1Id"), Number(player, "spell2Id")) : null;
    }

    internal static (int? Champion, int[] Teammates, int[] Unavailable, int TeamSize) ArenaDraftIds(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return (null, [], [], 0);

        var own = OwnPlayer(root);
        int localCell = Number(root, "localPlayerCellId");
        int localTeam = own is { } player ? ArenaTeamId(player) : -1;
        int teamSize = own is null ? 0 : 1;
        var actions = Elements(root, "actions").Where(group => group.ValueKind == JsonValueKind.Array)
            .SelectMany(group => group.EnumerateArray())
            .Where(action => action.ValueKind == JsonValueKind.Object && Number(action, "championId") > 0)
            .ToArray();
        var inProgressPicks = new Dictionary<int, int>();
        foreach (var action in actions)
        {
            if (TextIs(action, "type", "pick") && FlagIs(action, "isInProgress", JsonValueKind.True) &&
                FlagIs(action, "completed", JsonValueKind.False) && Number(action, "actorCellId") is >= 0 and var cell)
                inProgressPicks[cell] = Number(action, "championId");
        }

        var teammates = new HashSet<int>();
        var unavailable = new HashSet<int>();
        var enemyCells = new HashSet<int>();
        foreach (var ally in Elements(root, "myTeam"))
        {
            if (ally.ValueKind != JsonValueKind.Object || Number(ally, "cellId") < 0 || Number(ally, "cellId") == localCell)
                continue;
            if (localTeam >= 0 && ArenaTeamId(ally) is >= 0 and var team && team != localTeam)
                AddEnemy(ally);
            else if (own is not null)
            {
                teamSize++;
                if (ArenaPick(ally, inProgressPicks) is { } teammate)
                    teammates.Add(teammate);
            }
        }
        foreach (var enemy in Elements(root, "theirTeam"))
            if (enemy.ValueKind == JsonValueKind.Object)
                AddEnemy(enemy);

        if (root.TryGetProperty("bans", out var bans))
        {
            foreach (var ban in Elements(bans, "myTeamBans").Concat(Elements(bans, "theirTeamBans")))
                if (ban.ValueKind == JsonValueKind.Number && ban.TryGetInt32(out var id) && id > 0)
                    unavailable.Add(id);
        }
        foreach (var action in actions)
        {
            if (FlagIs(action, "completed", JsonValueKind.True) && (TextIs(action, "type", "ban") ||
                (TextIs(action, "type", "pick") && enemyCells.Contains(Number(action, "actorCellId")))))
                unavailable.Add(Number(action, "championId"));
        }
        return (own is { } local ? ArenaPick(local, inProgressPicks) : null, [.. teammates], [.. unavailable], teamSize);

        void AddEnemy(JsonElement enemy)
        {
            if (Number(enemy, "cellId") is >= 0 and var cell)
                enemyCells.Add(cell);
            if (Number(enemy, "championId") is > 0 and var champion)
                unavailable.Add(champion);
        }
    }

    private static IEnumerable<JsonElement> Elements(JsonElement root, string key) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(key, out var array) &&
        array.ValueKind == JsonValueKind.Array ? array.EnumerateArray() : [];

    private static int ArenaTeamId(JsonElement player) => Number(player, "teamId") is >= 0 and var team
        ? team : Number(player, "team");

    private static int? ArenaPick(JsonElement player, Dictionary<int, int> inProgressPicks)
    {
        if (Number(player, "championId") is > 0 and var picked)
            return picked;
        if (inProgressPicks.TryGetValue(Number(player, "cellId"), out var hovered))
            return hovered;
        return Number(player, "championPickIntent") is > 0 and var intended ? intended : null;
    }

    private static bool FlagIs(JsonElement root, string key, JsonValueKind value) =>
        root.TryGetProperty(key, out var flag) && flag.ValueKind == value;

    private static JsonElement? OwnPlayer(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("myTeam", out var team) || team.ValueKind != JsonValueKind.Array)
            return null;
        var myCell = Number(root, "localPlayerCellId");
        if (myCell < 0)
            return null;
        foreach (var player in team.EnumerateArray())
        {
            if (player.ValueKind == JsonValueKind.Object && Number(player, "cellId") == myCell)
                return player;
        }
        return null;
    }

    private static int Number(JsonElement root, string key) =>
        root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var id) ? id : -1;

    private static Dictionary<int, string>? ReadNames(JsonDocument? document, string property)
    {
        if (document?.RootElement.ValueKind != JsonValueKind.Array)
            return null;
        var names = new Dictionary<int, string>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object && Number(item, "id") is > 0 and var id &&
                item.TryGetProperty(property, out var name) && name.ValueKind == JsonValueKind.String &&
                name.GetString() is { Length: > 0 } value)
                names.TryAdd(id, value);
        }
        return names.Count > 0 ? names : null;
    }

    private HttpClient? Open()
    {
        install ??= LeaguePatch.InstallDirectory();
        if (install is null) return null;
        var path = Path.Combine(install, "lockfile");
        if (!File.Exists(path))
        {
            install = null;
            Reset();
            return null;
        }
        string[] fields;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var reader = new StreamReader(stream))
            fields = (reader.ReadLine() ?? "").Trim().Split(':');
        if (fields.Length != 5 || fields[0] != "LeagueClient" || fields[4] != "https" ||
            !int.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out int owner) || owner <= 0 ||
            !int.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out int localPort) || localPort is < 1 or > 65535 ||
            fields[3].Length is < 1 or > 256)
            return null;

        var authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes("riot:" + fields[3])));
        if (client is not null && processId == owner && port == localPort && authorization.Equals(credential))
            return client;

        Reset();
        using var process = Process.GetProcessById(owner);
        if (!string.Equals(process.ProcessName, "LeagueClient", StringComparison.OrdinalIgnoreCase))
            return null;
        client = new HttpClient(LiveGame.LocalHandler(root, localPort))
        {
            BaseAddress = new Uri($"https://127.0.0.1:{localPort}"),
            Timeout = TimeSpan.FromMilliseconds(700),
            MaxResponseContentBufferSize = 1024 * 1024
        };
        client.DefaultRequestHeaders.Authorization = authorization;
        processId = owner;
        port = localPort;
        credential = authorization;
        return client;
    }

    private static async Task<JsonDocument?> Get(HttpClient client, string endpoint, CancellationToken cancellation)
    {
        using var response = await client.GetAsync(endpoint, cancellation).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return null;
        var json = await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false);
        return JsonDocument.Parse(json);
    }

    private void Reset()
    {
        client?.Dispose();
        client = null;
        credential = null;
        champions = null;
        spells = null;
    }

    public void Dispose()
    {
        Reset();
        root.Dispose();
    }
}

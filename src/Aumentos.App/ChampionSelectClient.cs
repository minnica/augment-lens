using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Aumentos;

internal sealed record ChampionSelection(string Champion, string? FirstSpell, string? SecondSpell);

internal sealed class ChampionSelectClient : IDisposable
{
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

    internal async Task<ChampionSelection?> Read(CancellationToken cancellation)
    {
        var connection = Open();
        if (connection is null) return null;
        using var flow = await Get(connection, "/lol-gameflow/v1/session", cancellation).ConfigureAwait(false);
        if (flow is null || !IsMayhemSelect(flow.RootElement))
            return null;
        using var selection = await Get(connection, "/lol-champ-select/v1/session", cancellation).ConfigureAwait(false);
        if (selection is null || OwnPick(selection.RootElement) is not { } pick)
            return null;

        if (champions is null)
        {
            using var championData = await Get(connection, "/lol-game-data/assets/v1/champion-summary.json", cancellation).ConfigureAwait(false);
            champions = ReadNames(championData, "alias");
        }
        if (spells is null)
        {
            using var spellData = await Get(connection, "/lol-game-data/assets/v1/summoner-spells.json", cancellation).ConfigureAwait(false);
            spells = ReadNames(spellData, "name");
        }
        return champions?.TryGetValue(pick.Champion, out var key) == true
            ? new ChampionSelection(key, spells?.GetValueOrDefault(pick.Spell1), spells?.GetValueOrDefault(pick.Spell2))
            : null;
    }

    internal static bool IsMayhemSelect(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty("phase", out var phase) &&
        phase.ValueKind == JsonValueKind.String && phase.ValueEquals("ChampSelect") &&
        root.TryGetProperty("gameData", out var data) && data.ValueKind == JsonValueKind.Object &&
        data.TryGetProperty("queue", out var queue) && queue.ValueKind == JsonValueKind.Object &&
        Number(queue, "id") == 2400;

    internal static (int Champion, int Spell1, int Spell2)? OwnPick(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || Number(root, "localPlayerCellId") is not >= 0 ||
            !root.TryGetProperty("myTeam", out var team) || team.ValueKind != JsonValueKind.Array)
            return null;
        var myCell = Number(root, "localPlayerCellId");
        foreach (var player in team.EnumerateArray())
        {
            if (player.ValueKind != JsonValueKind.Object || Number(player, "cellId") != myCell)
                continue;
            var champion = Number(player, "championId");
            return champion > 0 ? (champion, Number(player, "spell1Id"), Number(player, "spell2Id")) : null;
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

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
internal sealed record LiveMatch(GameContext Game, MayhemMatch? Mayhem = null);

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
        (await ReadMatch(cancellation).ConfigureAwait(false))?.Game;

    internal async Task<LiveMatch?> ReadMatch(CancellationToken cancellation)
    {
        using var game = await ReadJson("gamestats", cancellation).ConfigureAwait(false);
        var mode = ParseMode(game.RootElement.GetProperty("gameMode").GetString());
        if (mode is null)
            return null;
        using var name = await ReadJson("activeplayername", cancellation).ConfigureAwait(false);
        using var players = await ReadJson("playerlist", cancellation).ConfigureAwait(false);
        return ParseMatch(mode.Value, players.RootElement, name.RootElement.GetString());
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
        foreach (var player in players.EnumerateArray())
        {
            if (Identity(player, "riotId") != active && Identity(player, "summonerName") != active)
                continue;
            if (ChampionKey(player) is not { } champion)
                continue;
            var game = new GameContext(mode, champion);
            var team = Identity(player, "team");
            // Arena still uses only the player's champion. Its PvP teams change by round.
            if (mode != Mode.Caos || team is not ("ORDER" or "CHAOS") || ReadItems(player) is not { } owned)
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

    private async Task<JsonDocument> ReadJson(string path, CancellationToken cancellation)
    {
        using var response = await client.GetAsync(path, cancellation).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false);
        return JsonDocument.Parse(json);
    }

    public void Dispose()
    {
        client.Dispose();
        root.Dispose();
    }
}

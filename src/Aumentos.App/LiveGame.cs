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

internal sealed class LiveGame : IDisposable
{
    private readonly X509Certificate2 root;
    private readonly HttpClient client;

    public LiveGame(string rootPath)
    {
        root = X509Certificate2.CreateFromPem(File.ReadAllText(rootPath));
        client = new HttpClient(new HttpClientHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            ServerCertificateCustomValidationCallback = Validate
        })
        {
            BaseAddress = new Uri("https://127.0.0.1:2999/liveclientdata/"),
            Timeout = TimeSpan.FromMilliseconds(750)
        };
    }

    private bool Validate(HttpRequestMessage request, X509Certificate2? certificate, X509Chain? _, SslPolicyErrors errors)
    {
        if (request.RequestUri?.Host != "127.0.0.1" || request.RequestUri.Port != 2999 || certificate is null)
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

    internal async Task<GameContext?> Read(CancellationToken cancellation)
    {
        using var game = await ReadJson("gamestats", cancellation).ConfigureAwait(false);
        var mode = ParseMode(game.RootElement.GetProperty("gameMode").GetString());
        if (mode is null)
            return null;
        using var name = await ReadJson("activeplayername", cancellation).ConfigureAwait(false);
        using var players = await ReadJson("playerlist", cancellation).ConfigureAwait(false);
        var key = ChampionKey(players.RootElement, name.RootElement.GetString());
        return key is null ? null : new GameContext(mode.Value, key);
    }

    internal static Mode? ParseMode(string? mode) => mode switch
    {
        "KIWI" => Mode.Caos,
        "CHERRY" => Mode.Arena,
        _ => null
    };

    internal static string? ChampionKey(JsonElement players, string? active)
    {
        if (string.IsNullOrWhiteSpace(active))
            return null;
        const string prefix = "game_character_displayname_";
        foreach (var player in players.EnumerateArray())
        {
            if (Identity(player, "riotId") != active && Identity(player, "summonerName") != active)
                continue;
            var raw = Identity(player, "rawChampionName");
            if (raw is not null && raw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return raw[prefix.Length..];
        }
        return null;
    }

    private static string? Identity(JsonElement player, string field) =>
        player.TryGetProperty(field, out var value) ? value.GetString() : null;

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

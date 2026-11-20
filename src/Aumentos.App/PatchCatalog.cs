using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Aumentos;

internal sealed class PatchCatalog(string bundled, string settings, string tools)
{
    private static readonly TimeSpan Retry = TimeSpan.FromMinutes(30);
    private readonly string root = Path.GetFullPath(Path.Combine(settings, "patch-catalogs"));

    internal string Select(string? installed)
    {
        if (!ValidPatch(installed))
            return bundled;
        try
        {
            if (Ratings.Read<Catalog>(Path.Combine(bundled, "catalog.json")).Patch == installed)
                return bundled;
        }
        catch (Exception error) when (IsDataError(error)) { }
        var saved = Path.Combine(root, installed!);
        return IsReady(saved, installed!) ? saved : bundled;
    }

    internal async Task Watch(string active, Action<string> status, Action activate, CancellationToken cancellation)
    {
        string? pending = null;
        var retryAt = DateTimeOffset.MinValue;
        while (!cancellation.IsCancellationRequested)
        {
            var installed = LeaguePatch.Installed();
            if (ValidPatch(installed) && installed != active && !MatchRunning() &&
                (installed != pending || DateTimeOffset.UtcNow >= retryAt))
            {
                pending = installed;
                try
                {
                    status($"Preparando catálogo {LeaguePatch.Public(installed!)}…");
                    await Prepare(installed!, status, cancellation).ConfigureAwait(false);
                    while (LeaguePatch.Installed() == installed && MatchRunning())
                        await Task.Delay(TimeSpan.FromSeconds(15), cancellation).ConfigureAwait(false);
                    if (LeaguePatch.Installed() == installed)
                    {
                        activate();
                        return;
                    }
                }
                catch (Exception error) when (IsDataError(error) || error is Win32Exception)
                {
                    retryAt = DateTimeOffset.UtcNow + Retry;
                    status($"Catálogo {LeaguePatch.Public(installed!)} pendiente · {error.Message}");
                }
            }
            await Task.Delay(TimeSpan.FromSeconds(45), cancellation).ConfigureAwait(false);
        }
    }

    internal async Task Prepare(string patch, Action<string> status, CancellationToken cancellation)
    {
        if (!ValidPatch(patch))
            throw new ArgumentException("Se necesita el parche del cliente, por ejemplo 16.23.", nameof(patch));
        string destination = Path.Combine(root, patch);
        if (IsReady(destination, patch))
            return;

        string staging = Path.Combine(root, $"{patch}.{Guid.NewGuid():N}.pending");
        Directory.CreateDirectory(staging);
        try
        {
            foreach (var file in new[] { "layouts.json", "ratings.json", "riotgames.pem" })
                File.Copy(Path.Combine(bundled, file), Path.Combine(staging, file));
            var ocr = Directory.CreateDirectory(Path.Combine(staging, "tessdata"));
            foreach (var file in Directory.EnumerateFiles(Path.Combine(bundled, "tessdata")))
                File.Copy(file, Path.Combine(ocr.FullName, Path.GetFileName(file)));

            await RunTool("Get-Assets.ps1", ["-Patch", patch, "-Assets", staging], cancellation).ConfigureAwait(false);
            await TrySupplement("Get-ArenaWikiMechanics.ps1", "LoL Wiki", staging, status, cancellation).ConfigureAwait(false);
            if (LeaguePatch.Previous(patch) is not null)
                await TrySupplement("Get-AramHexCompatibility.ps1", "AramHex del parche anterior", staging, status, cancellation)
                    .ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            if (!IsReady(staging, patch))
                throw new InvalidDataException("El catálogo descargado no pasó la validación.");
            if (Directory.Exists(destination))
                Directory.Move(destination, Path.Combine(root, $"{patch}.{Guid.NewGuid():N}.invalid"));
            Directory.Move(staging, destination);
        }
        finally
        {
            try { Directory.Delete(staging, recursive: true); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    internal static bool MatchRunning()
    {
        var games = Process.GetProcessesByName("League of Legends");
        foreach (var game in games)
            game.Dispose();
        return games.Length != 0;
    }

    private static bool IsReady(string folder, string patch)
    {
        try
        {
            var ratings = new Ratings(folder, allowMissingRules: true);
            var catalog = ratings.Catalog;
            return catalog.Patch == patch && catalog.Champions.Length > 0 && catalog.ArenaPrismatics is { Length: > 0 } &&
                catalog.ArenaLegendaries is { Length: > 0 } &&
                catalog.Augments.Any(card => card.Mode == nameof(Mode.Arena)) &&
                catalog.Augments.Any(card => card.Mode == nameof(Mode.Caos)) &&
                Ratings.Read<MayhemItemCatalog>(Path.Combine(folder, "mayhem-items.json")) is { Items.Count: > 0 } items &&
                items.Patch == patch && File.Exists(Path.Combine(folder, "tessdata", "spa.traineddata")) &&
                File.Exists(Path.Combine(folder, "layouts.json")) && File.Exists(Path.Combine(folder, "riotgames.pem"));
        }
        catch (Exception error) when (IsDataError(error)) { return false; }
    }

    private async Task TrySupplement(string script, string source, string staging, Action<string> status,
        CancellationToken cancellation)
    {
        try { await RunTool(script, ["-Assets", staging], cancellation).ConfigureAwait(false); }
        catch (Exception error) when (IsDataError(error) || error is Win32Exception)
        {
            status($"{source} pendiente · {error.Message}");
        }
    }

    private async Task RunTool(string name, string[] arguments, CancellationToken cancellation)
    {
        var script = Path.Combine(tools, name);
        if (!File.Exists(script))
            throw new FileNotFoundException($"Falta {name} en esta instalación.");
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-File", script }.Concat(arguments))
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException($"No se pudo iniciar {name}.");
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromMinutes(6));
        try { await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync().ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            throw new IOException($"Se agotó el tiempo de {name}.");
        }
        var detail = (await errors.ConfigureAwait(false)).Trim();
        await output.ConfigureAwait(false);
        if (process.ExitCode != 0)
            throw new IOException($"{name} · {detail.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "Descarga fallida"}");
    }

    private static bool IsDataError(Exception error) => error is IOException or InvalidDataException or JsonException or
        UnauthorizedAccessException or ArgumentException or InvalidOperationException or IndexOutOfRangeException or NullReferenceException;

    private static bool ValidPatch(string? patch) => patch is not null && Regex.IsMatch(patch, @"\A[1-9][0-9]{0,2}\.[0-9]{1,3}\z");
}

using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Forms = System.Windows.Forms;

namespace Aumentos;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        Native.SetProcessDpiAwarenessContext(-4);
        var assets = Path.Combine(AppContext.BaseDirectory, "Assets");
        var settings = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AumentosLoL");
        using var http = OpGgRatings.CreateClient();
        if (args.FirstOrDefault() == "--verificar-build")
            return VerifyBuild(args, assets, settings, http);
        if (Array.IndexOf(args, "--verificar") >= 0)
        {
            try
            {
                var ratings = new Ratings(assets);
                using var detection = new Detection(ratings, Path.Combine(assets, "tessdata"));
                using var live = new LiveGame(Path.Combine(assets, "riotgames.pem"));
                if (args.Length is 3 or 4 && Enum.TryParse<Mode>(args[1], true, out var mode))
                {
                    var image = Diagnostics.OpenBitmap(args[2]);
                    var layouts = new LayoutStore(Path.Combine(assets, "layouts.json"), "");
                    var options = layouts.Find(mode, image.PixelWidth, image.PixelHeight);
                    if (options.Length == 0)
                        return 2;
                    var read = detection.ReadPanel(image, new Int32Rect(0, 0, image.PixelWidth, image.PixelHeight),
                        options, mode, previous: null);
                    if (read is not { Result: var result })
                        return 3;
                    CardComparison?[]? comparisons = null;
                    CardComparison?[]? itemChoices = null;
                    ArenaPrismaticOffer? prismatics = null;
                    var shards = StatShardRatings.Identify(result);
                    if (args.Length == 4)
                    {
                        var champion = ratings.Catalog.Champions.Single(item =>
                            string.Equals(item.Key, args[3], StringComparison.OrdinalIgnoreCase));
                        var game = new GameContext(mode, champion.Key);
                        var shardChoices = ratings.Shards.ForOffer(game, result);
                        prismatics = shardChoices is null ? ratings.Prismatics.ForOffer(game, result) : null;
                        itemChoices = shardChoices ?? prismatics?.Badges ?? ratings.Legendaries.ForOffer(game, result);
                        if (itemChoices is null)
                        {
                            var (source, mayhem, arena) = CaptureSources(game, new OpGgRatings(ratings.Catalog, settings, http),
                                new AramMayhemRatings(ratings.Catalog, settings, http), new GgezArenaRatings(ratings.Catalog, settings, http));
                            comparisons = CardComparison.ForOffer(ratings, game, result.Cards, result.Items, source.Data,
                                mayhem: mayhem?.Data, ggez: arena?.Data, upgradeLevels: result.UpgradeLevels);
                            Console.WriteLine(source.Status);
                            if (mayhem is not null)
                                Console.WriteLine(mayhem.Status);
                            if (arena is not null)
                                Console.WriteLine(arena.Status);
                        }
                    }
                    for (int index = 0; index < result.Cards.Length; index++)
                    {
                        var card = ratings.Catalog.Augments.FirstOrDefault(augment =>
                            augment.Mode == mode.ToString() && augment.Id == result.Cards[index]);
                        var label = result.Items[index] ? $"Objeto: {result.Texts[index]}" : card?.Name ?? $"? {result.Texts[index]}";
                        if (shards?[index] is { } shard)
                            label = StatShardRatings.Name(shard);
                        if (itemChoices?[index] is { ArenaChoice: { } choice } choiceBadge)
                            label = $"{(shards is null ? "Objeto: " : "")}{choice.Name} · Tier local {choiceBadge.LocalText}{choice.TopLabel} · {choice.Reason}" +
                                (choice.Recommended ? " · Recomendado" : "");
                        if (card is not null && comparisons?[index] is { } comparison)
                        {
                            if (comparison.IsArenaAugment && comparison.Local != CardComparison.Missing)
                            {
                                label += $" · {comparison.ArenaTitle.Replace("   ", " ")}";
                                if (comparison.External is not null)
                                    label += $" · OP.GG {comparison.ExternalText}";
                            }
                            else
                                label += $" · OP.GG {comparison.ExternalText} · Est. local {comparison.Local}";
                            if (comparison.DetailText.Length > 0)
                                label += $" · {comparison.DetailText.Replace("\n", " · ")}";
                            if (comparison.UsesWiki)
                                label += $" · Mecánica de {ArenaWikiMechanics.Attribution}";
                            if (comparison.Mayhem is { } popularity)
                                label += $" · {popularity.Text} · {(popularity.IsPrevious ? "Parche anterior" : "Parche")} {popularity.Patch}";
                            if (comparison.Arena is { } arenaPopularity)
                                label += $" · {arenaPopularity.Text} · {arenaPopularity.Details}";
                        }
                        Console.WriteLine(label);
                    }
                    if (prismatics is { Top: var top })
                    {
                        Console.WriteLine("Top 5 prismáticos");
                        foreach (var (entry, index) in top.Entries.Select((entry, index) => (entry, index)))
                            Console.WriteLine($"{index + 1}. {entry.Rating.Name} · {entry.Rating.TierText} · {entry.Rating.Reason}" +
                                (top.IsOffered(index) ? " · En oferta" : ""));
                    }
                    Console.WriteLine(result.CanShow(false) || shards is not null || itemChoices is not null ? "Overlay visible" : "Overlay oculto");
                    return result.Matched == result.Cards.Length ? 0 : 4;
                }
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error.Message);
                return 1;
            }
        }
        using var instance = new Mutex(true, @"Local\AumentosLoL", out var firstInstance);
        if (!firstInstance)
            return 0;
        try
        {
            var ratings = new Ratings(assets);
            var layouts = new LayoutStore(Path.Combine(assets, "layouts.json"), Path.Combine(settings, "layouts.json"));
            var history = new CardHistory(settings, ratings);
            var opgg = new OpGgRatings(ratings.Catalog, settings, http);
            var aramMayhem = new AramMayhemRatings(ratings.Catalog, settings, http);
            var ggez = new GgezArenaRatings(ratings.Catalog, settings, http);
            var builds = new OpGgBuilds(ratings.Catalog, settings, http);
            if (args.FirstOrDefault() == "--registrar-capturas")
                return ImportCaptures(args, assets, ratings, layouts, history, opgg, aramMayhem, ggez);
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var diagnostics = new Diagnostics(ratings, layouts, assets);
            var closing = false;
            diagnostics.Closing += (_, args) =>
            {
                if (!closing)
                {
                    args.Cancel = true;
                    diagnostics.Hide();
                }
            };
            var overlay = new Overlay();
            var itemIcons = new BuildItemIcons(http);
            var buildOverlay = new BuildOverlay(itemIcons);
            var sizePreference = new BuildSizePreference(Path.Combine(settings, "build-size.json"));
            int buildSize = sizePreference.Load();
            buildOverlay.SetSize(buildSize);
            BuildSizeWindow? buildSizeWindow = null;
            var spellOverlay = new SpellOverlay();
            var arenaChampionOverlay = new ArenaChampionOverlay();
            var championTiers = new ArenaChampionTierSource(ratings.Catalog, settings, http);
            var championSelect = new ChampionSelectAdvice(ratings, assets, builds, championTiers,
                app.Dispatcher, spellOverlay.Render, arenaChampionOverlay.Render);
            using var stop = new CancellationTokenSource();
            string previous = "";
            var worker = new LiveOverlay(ratings, layouts, history, opgg, aramMayhem, ggez, builds, assets, app.Dispatcher, state =>
            {
                overlay.Render(state);
                var info = $"{state.Status} · {(overlay.IsVisible ? "Overlay visible" : "Overlay oculto")}";
                if (info != previous)
                {
                    diagnostics.SetStatus(state.Status);
                    previous = info;
                    WriteStatus(info);
                }
            }, buildOverlay.Render);
            using var menu = new Forms.ContextMenuStrip();
            using var appIcon = Icon.ExtractAssociatedIcon(Path.Combine(AppContext.BaseDirectory, "AumentosLoL.exe"))
                ?? (Icon)SystemIcons.Application.Clone();
            using var tray = new Forms.NotifyIcon { Icon = appIcon, Text = "Aumentos LoL", ContextMenuStrip = menu, Visible = true };
            menu.Items.Add("Diagnóstico y calibración", null, (_, _) => OpenDiagnostics());
            menu.Items.Add("Abrir historial de aumentos", null, (_, _) =>
            {
                if (!File.Exists(history.FilePath))
                {
                    MessageBox.Show("El historial se crea al detectar aumentos en una partida.", "Historial de aumentos");
                    return;
                }
                try
                {
                    using var viewer = Process.Start(new ProcessStartInfo(history.FilePath) { UseShellExecute = true });
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    MessageBox.Show($"Abre el archivo desde esta ruta:\n{history.FilePath}", "Historial de aumentos");
                }
            });
            menu.Items.Add($"Datos de {AramMayhemRatings.Attribution}", null, (_, _) =>
                OpenProviderPage(AramMayhemRatings.AttributionUrl, AramMayhemRatings.Attribution));
            menu.Items.Add($"Datos de {GgezArenaPage.Attribution}", null, (_, _) =>
                OpenProviderPage(ggez.SourceUrl, GgezArenaPage.Attribution));
            if (ratings.Wiki is { } wiki)
                menu.Items.Add($"Mecánicas de {ArenaWikiMechanics.Attribution}", null, (_, _) =>
                    OpenProviderPage($"https://wiki.leagueoflegends.com/en-us/Special:PermanentLink/{wiki.SourceRevision}",
                        ArenaWikiMechanics.Attribution));
            menu.Items.Add("Tier de campeones de Arena", null, (_, _) =>
                OpenProviderPage(ArenaChampionTierSource.Address.AbsoluteUri, ArenaChampionTierSource.Attribution));
            var cardDetails = new Forms.ToolStripMenuItem("Detalles de las cartas") { CheckOnClick = true };
            cardDetails.CheckedChanged += (_, _) => overlay.ShowDetails = cardDetails.Checked;
            menu.Items.Add(cardDetails);
            var showBuild = new Forms.ToolStripMenuItem("Mostrar u ocultar build de OP.GG", null, (_, _) => ToggleBuild());
            menu.Items.Add(showBuild);
            var resizeBuild = new Forms.ToolStripMenuItem("Tamaño de las builds…", null, (_, _) => OpenBuildSize());
            menu.Items.Add(resizeBuild);
            menu.Items.Add("Abrir build de OP.GG", null, (_, _) => OpenProviderPage(builds.SourceUrl, "OP.GG"));
            var pause = new Forms.ToolStripMenuItem("Pausar overlay") { CheckOnClick = true };
            pause.CheckedChanged += (_, _) =>
            {
                worker.Paused = pause.Checked;
                championSelect.Paused = pause.Checked;
                if (pause.Checked)
                {
                    overlay.Hide();
                    buildOverlay.Render(null);
                    spellOverlay.Render(null);
                    arenaChampionOverlay.Render(null);
                }
            };
            menu.Items.Add(pause);
            menu.Items.Add("Salir", null, (_, _) =>
            {
                stop.Cancel();
                closing = true;
                tray.Visible = false;
                app.Shutdown();
            });
            tray.DoubleClick += (_, _) => OpenDiagnostics();
            app.Startup += (_, _) =>
            {
                var shortcuts = buildOverlay.RegisterShortcuts(ToggleBuild, OpenBuildSize);
                if (shortcuts.Toggle)
                    showBuild.ShortcutKeyDisplayString = "Ctrl+Alt+B";
                if (shortcuts.Size)
                    resizeBuild.ShortcutKeyDisplayString = "Ctrl+Alt+T";
                if (Array.IndexOf(args, "--bandeja") < 0)
                    OpenDiagnostics();
                _ = Task.Run(async () =>
                {
                    try { await worker.Run(stop.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
                    catch (Exception error)
                    {
                        if (!app.Dispatcher.HasShutdownStarted)
                            await app.Dispatcher.InvokeAsync(() =>
                            {
                                overlay.Hide();
                                buildOverlay.Render(null);
                                var message = $"No se pudo iniciar el overlay · {error.Message}";
                                diagnostics.SetStatus(message);
                                WriteStatus(message);
                            });
                    }
                });
                _ = Task.Run(async () =>
                {
                    try { await championSelect.Run(stop.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
                    catch (Exception error)
                    {
                        if (!app.Dispatcher.HasShutdownStarted)
                            await app.Dispatcher.InvokeAsync(() =>
                            {
                                spellOverlay.Render(null);
                                arenaChampionOverlay.Render(null);
                                WriteStatus($"No se pudo mostrar el panel de selección de campeón · {error.GetType().Name}");
                            });
                    }
                });
            };
            return app.Run();

            void ToggleBuild()
            {
                if (!worker.ToggleBuild())
                    buildOverlay.Render(null);
            }

            void OpenBuildSize()
            {
                if (buildSizeWindow is not null)
                {
                    buildSizeWindow.Activate();
                    return;
                }

                var window = new BuildSizeWindow(buildSize, ratings.Catalog.Patch, itemIcons, percent =>
                {
                    buildSize = percent;
                    buildOverlay.SetSize(percent);
                    return sizePreference.Save(percent);
                });
                buildSizeWindow = window;
                window.Closed += (_, _) => buildSizeWindow = null;
                window.Show();
            }

            void OpenDiagnostics()
            {
                diagnostics.Show();
                diagnostics.Activate();
            }

            void WriteStatus(string value)
            {
                try
                {
                    Directory.CreateDirectory(settings);
                    File.AppendAllText(Path.Combine(settings, "estado.log"), $"{DateTime.Now:HH:mm:ss}  {value}{Environment.NewLine}");
                    var path = Path.Combine(settings, "estado.log");
                    if (new FileInfo(path).Length > 64_000)
                        File.WriteAllText(path, string.Join(Environment.NewLine, File.ReadLines(path).TakeLast(80)) + Environment.NewLine);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        catch (Exception error)
        {
            MessageBox.Show(error.Message, "Aumentos LoL", MessageBoxButton.OK, MessageBoxImage.Error);
            return 1;
        }
    }

    private static int VerifyBuild(string[] args, string assets, string settings, System.Net.Http.HttpClient http)
    {
        try
        {
            if (args.Length != 3 || !Enum.TryParse<Mode>(args[1], true, out var mode) || !Enum.IsDefined(mode))
                throw new ArgumentException("Uso: --verificar-build Caos|Arena Campeón");
            var ratings = new Ratings(assets);
            var champion = ratings.Catalog.Champions.FirstOrDefault(item =>
                string.Equals(item.Key, args[2], StringComparison.OrdinalIgnoreCase)) ??
                throw new ArgumentException($"Campeón desconocido: {args[2]}.");
            var builds = new OpGgBuilds(ratings.Catalog, settings, http);
            var view = builds.ForCaptureAsync(new GameContext(mode, champion.Key), CancellationToken.None).GetAwaiter().GetResult();
            Console.WriteLine($"{champion.Name} · {view.Status}");
            if (view.Data is not { } data)
                return 4;
            if (data.Spells is { Length: > 0 } spells)
                Console.WriteLine("Hechizos de invocador · " +
                    string.Join(" | ", spells.Select(pair => $"{pair.First} + {pair.Second}")));
            foreach (var group in data.Groups)
                Console.WriteLine($"{BuildPanel.SectionLabel(group.Section)} · " +
                    string.Join(" | ", group.Choices.Select(items => BuildPanel.ChoiceText(group.Section, items))));
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }

    private static void OpenProviderPage(string url, string attribution)
    {
        try
        {
            using var browser = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception) { MessageBox.Show(url, attribution); }
    }

    private static (OpGgView OpGg, AramMayhemView? Mayhem, GgezArenaView? Arena) CaptureSources(
        GameContext game, OpGgRatings opgg, AramMayhemRatings aramMayhem, GgezArenaRatings ggez)
    {
        var opggWork = opgg.ForCaptureAsync(game, CancellationToken.None);
        var mayhemWork = aramMayhem.ForCaptureAsync(game.Mode, CancellationToken.None);
        var arenaWork = ggez.ForCaptureAsync(game, CancellationToken.None);
        return (opggWork.GetAwaiter().GetResult(), mayhemWork.GetAwaiter().GetResult(), arenaWork.GetAwaiter().GetResult());
    }

    private static int ImportCaptures(string[] args, string assets, Ratings ratings, LayoutStore layouts, CardHistory history,
        OpGgRatings opgg, AramMayhemRatings aramMayhem, GgezArenaRatings ggez)
    {
        try
        {
            if (args.Length <= 1 || (args.Length - 1) % 3 != 0)
                throw new ArgumentException("Indica el modo, la clave del campeón y la ruta de cada captura.");
            using var detector = new Detection(ratings, Path.Combine(assets, "tessdata"));
            for (int index = 1; index < args.Length; index += 3)
            {
                var mode = Enum.Parse<Mode>(args[index], true);
                var champion = ratings.Catalog.Champions.Single(item =>
                    string.Equals(item.Key, args[index + 1], StringComparison.OrdinalIgnoreCase));
                var file = args[index + 2];
                var image = Diagnostics.OpenBitmap(file);
                var bounds = new Int32Rect(0, 0, image.PixelWidth, image.PixelHeight);
                var options = layouts.Find(mode, bounds.Width, bounds.Height);
                detector.Clear();
                var read = detector.ReadPanel(image, bounds, options, mode, null);
                if (read is not { Result: var result } || !result.CanShow(false))
                    throw new InvalidDataException($"No se pudieron verificar los aumentos de {Path.GetFileName(file)}.");
                var (source, mayhem, arena) = CaptureSources(new GameContext(mode, champion.Key), opgg, aramMayhem, ggez);
                history.Import(File.GetLastWriteTime(file), mode, champion.Key, Path.GetFileName(file), result.Cards,
                    source.Data, mayhem?.Data, arena?.Data, result.UpgradeLevels);
                Console.WriteLine($"{champion.Name} · {Path.GetFileName(file)} · {result.Cards.Count(card => card.HasValue)} cartas");
            }
            history.Flush();
            Console.WriteLine(history.FilePath);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }
}

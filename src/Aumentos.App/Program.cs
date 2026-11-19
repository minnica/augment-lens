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
        if (Array.IndexOf(args, "--verificar") >= 0)
        {
            try
            {
                var ratings = new Ratings(assets);
                using var detection = new Detection(ratings, Path.Combine(assets, "tessdata"));
                using var live = new LiveGame(Path.Combine(assets, "riotgames.pem"));
                if (args.Length == 3 && Enum.TryParse<Mode>(args[1], true, out var mode))
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
                    for (int index = 0; index < result.Cards.Length; index++)
                    {
                        var card = ratings.Catalog.Augments.FirstOrDefault(augment =>
                            augment.Mode == mode.ToString() && augment.Id == result.Cards[index]);
                        Console.WriteLine(result.Items[index] ? $"Objeto: {result.Texts[index]}" : card?.Name ?? $"? {result.Texts[index]}");
                    }
                    Console.WriteLine(result.CanShow(false) ? "Overlay visible" : "Overlay oculto");
                    return result.Cards.Select((card, slot) => card.HasValue || result.Items[slot]).All(valid => valid) ? 0 : 4;
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
            var settings = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AumentosLoL");
            var ratings = new Ratings(assets);
            var layouts = new LayoutStore(Path.Combine(assets, "layouts.json"), Path.Combine(settings, "layouts.json"));
            var history = new CardHistory(settings, ratings);
            if (args.FirstOrDefault() == "--registrar-capturas")
                return ImportCaptures(args, assets, ratings, layouts, history);
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
            using var stop = new CancellationTokenSource();
            string previous = "";
            var worker = new LiveOverlay(ratings, layouts, history, assets, app.Dispatcher, state =>
            {
                overlay.Render(state);
                var info = $"{state.Status} · {(overlay.IsVisible ? "Overlay visible" : "Overlay oculto")}";
                if (info != previous)
                {
                    diagnostics.SetStatus(state.Status);
                    previous = info;
                    WriteStatus(info);
                }
            });
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
            var pause = new Forms.ToolStripMenuItem("Pausar overlay") { CheckOnClick = true };
            pause.CheckedChanged += (_, _) =>
            {
                worker.Paused = pause.Checked;
                if (pause.Checked) overlay.Hide();
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
                                var message = $"No se pudo iniciar el overlay · {error.Message}";
                                diagnostics.SetStatus(message);
                                WriteStatus(message);
                            });
                    }
                });
            };
            return app.Run();

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

    private static int ImportCaptures(string[] args, string assets, Ratings ratings, LayoutStore layouts, CardHistory history)
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
                history.Import(File.GetLastWriteTime(file), mode, champion.Key, Path.GetFileName(file), result.Cards);
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

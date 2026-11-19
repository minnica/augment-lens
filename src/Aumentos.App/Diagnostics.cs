using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using Path = System.IO.Path;

namespace Aumentos;

internal sealed class Diagnostics : Window
{
    private readonly Ratings ratings;
    private readonly LayoutStore layouts;
    private readonly string assets;
    private readonly Grid stage = new() { Width = 1000, Height = 520, Background = Overlay.Paint("#18273A") };
    private readonly Image preview = new() { Stretch = Stretch.Fill, IsHitTestVisible = false };
    private readonly Canvas selection = new() { Background = Brushes.Transparent };
    private readonly ComboBox modes = new() { Width = 112, ItemsSource = new[] { "ARAM Caos", "Arena" }, SelectedIndex = 0 };
    private readonly ComboBox slots = new() { Width = 50, ItemsSource = new[] { 3, 4 }, SelectedIndex = 0 };
    private readonly TextBlock detail = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly List<Region> regions = [];
    private BitmapSource? frame;
    private Point? start;
    private Rectangle? outline;
    private bool busy;

    public Diagnostics(Ratings ratings, LayoutStore layouts, string assets)
    {
        this.ratings = ratings;
        this.layouts = layouts;
        this.assets = assets;
        Title = "Aumentos LoL";
        Width = 1060;
        Height = 790;
        MinWidth = 720;
        MinHeight = 550;
        Background = Overlay.Paint("#0E1726");
        Foreground = Overlay.Paint("#E7EDF7");
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 14;

        var root = new Grid { Margin = new Thickness(22) };
        foreach (var height in new[] { GridLength.Auto, GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto })
            root.RowDefinitions.Add(new RowDefinition { Height = height });

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        header.Children.Add(new TextBlock { Text = "Aumentos LoL", FontSize = 26, FontWeight = FontWeights.SemiBold });
        status.Foreground = Overlay.Paint("#9DB4D2");
        header.Children.Add(status);
        root.Children.Add(header);
        var toolbar = new WrapPanel();
        toolbar.Children.Add(modes);
        toolbar.Children.Add(slots);
        AddButton("Abrir captura", LoadImage);
        AddButton("Capturar juego", async () => await CaptureGame());
        AddButton("Volver a marcar", () => Reset(false));
        AddButton("Probar lectura", async () => await Test(false));
        AddButton("Guardar calibración", async () => await Test(true));
        Grid.SetRow(toolbar, 1);
        root.Children.Add(toolbar);

        detail.Margin = new Thickness(0, 12, 0, 12);
        Grid.SetRow(detail, 2);
        root.Children.Add(detail);
        stage.Children.Add(preview);
        stage.Children.Add(selection);
        var viewport = new Border
        {
            Background = Overlay.Paint("#111D2D"), CornerRadius = new CornerRadius(8),
            Child = new Viewbox { Child = stage, Stretch = Stretch.Uniform }
        };
        Grid.SetRow(viewport, 3);
        root.Children.Add(viewport);
        root.Children.Add(new TextBlock
        {
            Text = "Captura la ventana completa del juego. Conserva una calibración por modo, tamaño y número de cartas.",
            Foreground = Overlay.Paint("#9DB4D2"), Margin = new Thickness(0, 12, 0, 0)
        });
        Grid.SetRow(root.Children[^1], 4);
        Content = root;
        SetStatus($"Datos es_MX · Parche {ratings.Catalog.Patch}");
        Guide();

        modes.SelectionChanged += (_, _) => Reset(true);
        slots.SelectionChanged += (_, _) => Reset(true);
        selection.MouseLeftButtonDown += (_, e) =>
        {
            if (frame is null || busy || regions.Count > Count)
                return;
            start = Clamp(e.GetPosition(selection));
            outline = RectangleFor(regions.Count == 0);
            selection.Children.Add(outline);
            selection.CaptureMouse();
        };
        selection.MouseMove += (_, e) =>
        {
            if (start is { } from && outline is not null)
                Position(outline, from, Clamp(e.GetPosition(selection)));
        };
        selection.MouseLeftButtonUp += (_, e) =>
        {
            if (start is not { } from || outline is null)
                return;
            var to = Clamp(e.GetPosition(selection));
            if (Math.Abs(from.X - to.X) > 5 && Math.Abs(from.Y - to.Y) > 4)
            {
                Position(outline, from, to);
                regions.Add(new Region(Math.Min(from.X, to.X) / selection.ActualWidth,
                    Math.Min(from.Y, to.Y) / selection.ActualHeight,
                    Math.Abs(from.X - to.X) / selection.ActualWidth,
                    Math.Abs(from.Y - to.Y) / selection.ActualHeight));
            }
            else
                selection.Children.Remove(outline);
            start = null;
            outline = null;
            selection.ReleaseMouseCapture();
            Guide();
        };
        preview.SizeChanged += (_, _) => DrawRegions();
        selection.SizeChanged += (_, _) => DrawRegions();
        return;

        void AddButton(string name, Action action)
        {
            var button = new Button { Content = name, Padding = new Thickness(9, 4, 9, 4), Margin = new Thickness(6, 0, 0, 0) };
            button.Click += (_, _) => action();
            toolbar.Children.Add(button);
        }

        void LoadImage()
        {
            if (busy)
                return;
            var dialog = new OpenFileDialog { Filter = "Capturas|*.png;*.jpg;*.jpeg;*.bmp" };
            if (dialog.ShowDialog(this) != true)
                return;
            try
            {
                LoadFrame(OpenBitmap(dialog.FileName));
            }
            catch (Exception error) when (error is IOException or NotSupportedException or System.Runtime.InteropServices.COMException)
            {
                detail.Text = $"No se pudo abrir la imagen. {error.Message}";
            }
        }
    }

    private void LoadFrame(BitmapSource bitmap)
    {
        frame = bitmap;
        preview.Source = bitmap;
        stage.Height = stage.Width * bitmap.PixelHeight / bitmap.PixelWidth;
        Reset(true);
    }

    private async Task CaptureGame()
    {
        if (busy)
            return;
        busy = true;
        modes.IsEnabled = slots.IsEnabled = false;
        detail.Text = "Cambia a League y abre el panel de cartas. La captura se tomará en cinco segundos.";
        try
        {
            await Task.Delay(5000);
            var (image, game) = await Task.Run(async () =>
            {
                var window = GameWindow.Foreground() ??
                    throw new InvalidOperationException("League debe estar en primer plano.");
                using var source = new WindowCapture(window.Handle);
                var region = new Int32Rect(window.Bounds.X - window.Frame.X, window.Bounds.Y - window.Frame.Y,
                    window.Bounds.Width, window.Bounds.Height);
                var timer = Stopwatch.StartNew();
                while (timer.ElapsedMilliseconds < 1200 && Native.GetForegroundWindow() == window.Handle)
                {
                    if (source.Read(region) is { } captured)
                    {
                        GameContext? current = null;
                        try
                        {
                            using var live = new LiveGame(Path.Combine(assets, "riotgames.pem"));
                            current = await live.Read(default).ConfigureAwait(false);
                        }
                        catch (System.Net.Http.HttpRequestException) { }
                        catch (OperationCanceledException) { }
                        return (captured, current);
                    }
                    await Task.Delay(40).ConfigureAwait(false);
                }
                throw new InvalidOperationException("No se recibió una imagen de la ventana del juego.");
            });
            busy = false;
            if (game is not null)
                modes.SelectedIndex = game.Mode == Mode.Arena ? 1 : 0;
            LoadFrame(image);
            if (regions.Count == Count + 1)
                await Test(false);
        }
        catch (Exception error)
        {
            detail.Text = $"Captura no disponible · {error.Message}";
        }
        finally
        {
            busy = false;
            modes.IsEnabled = slots.IsEnabled = true;
        }
    }

    internal static BitmapSource OpenBitmap(string file)
    {
        using var stream = File.OpenRead(file);
        var bitmap = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        bitmap.Freeze();
        return bitmap;
    }

    internal void SetStatus(string value) => status.Text = value;
    private Mode CurrentMode => modes.SelectedIndex == 1 ? Mode.Arena : Mode.Caos;
    private int Count => (int)slots.SelectedItem;

    private void Reset(bool useSaved)
    {
        if (busy)
            return;
        regions.Clear();
        if (useSaved && frame is not null && layouts.Find(CurrentMode, frame.PixelWidth, frame.PixelHeight)
                .FirstOrDefault(layout => layout.Titles.Length == Count) is { } layout)
            regions.AddRange(new[] { layout.Marker }.Concat(layout.Titles));
        DrawRegions();
        Guide();
    }

    private async Task Test(bool save)
    {
        if (busy || frame is not { } image || regions.Count != Count + 1)
            return;
        var mode = CurrentMode;
        var marker = regions[0];
        var titles = regions.Skip(1).OrderBy(region => region.X).ToArray();
        busy = true;
        modes.IsEnabled = slots.IsEnabled = false;
        try
        {
            detail.Text = "Leyendo títulos…";
            var (layout, result) = await Task.Run(() =>
            {
                var calibration = new Calibration(image.PixelWidth, image.PixelHeight, marker, titles,
                    Detection.Signature(image, marker.Pixels(image.PixelWidth, image.PixelHeight)));
                using var detector = new Detection(ratings, Path.Combine(assets, "tessdata"));
                var read = detector.Scan(image, new Int32Rect(0, 0, image.PixelWidth, image.PixelHeight), calibration, mode);
                return (calibration, read);
            });
            if (!result.PanelVisible)
            {
                detail.Text = "No encontré el panel. Revisa el recorte del marcador.";
                return;
            }
            var prismatics = mode == Mode.Arena ? ratings.Prismatics.Identify(result) : null;
            var shards = mode == Mode.Arena ? ArenaShardRatings.Identify(result) : null;
            var legendaries = mode == Mode.Arena && prismatics is null ? ratings.Legendaries.Identify(result) : null;
            var names = result.Cards.Select((id, index) => shards?[index] is { } shard ? ArenaShardRatings.Name(shard)
                : result.Items[index]
                ? (prismatics ?? legendaries)?[index]?.Name ?? $"Objeto «{result.Texts[index]}»"
                : id.HasValue ? ratings.Catalog.Augments.Single(card => card.Mode == mode.ToString() && card.Id == id.Value).Name
                : $"? «{result.Texts[index]}»").ToArray();
            bool recognized = result.CanShow(false) || prismatics is not null || legendaries is not null || shards is not null;
            if (save && recognized)
            {
                layouts.Save(new SavedLayout(mode, layout));
                detail.Text = $"Calibración guardada · {string.Join("  |  ", names)}";
            }
            else if (recognized)
                detail.Text = $"{(shards is not null ? "Fragmentos" : prismatics is not null ? "Prismáticos" : legendaries is not null ? "Legendarios" : "Aumentos")} detectados · " +
                    $"{string.Join("  |  ", names)} · Los tiers aparecen en la partida.";
            else if (result.Items.Count(item => item) >= 2)
                detail.Text = "La captura muestra objetos de Arena. Los tiers aparecen con las cartas legendarias o prismáticas reconocidas.";
            else
                detail.Text = "No encontré suficientes cartas. Revisa el modo y los recortes de los títulos.";
        }
        catch (Exception error)
        {
            detail.Text = $"No se pudo completar la prueba. {error.Message}";
        }
        finally
        {
            busy = false;
            modes.IsEnabled = slots.IsEnabled = true;
        }
    }

    private void Guide()
    {
        detail.Text = frame is null ? "Elige el modo y el número de cartas. Abre una captura para probar la lectura o ajustar el overlay."
            : regions.Count == 0 ? "Marca un detalle fijo del panel, como el botón inferior. Evita el temporizador y los destellos."
            : regions.Count <= Count ? $"Marca la zona del título de la carta {regions.Count} de {Count}, de izquierda a derecha."
            : "Prueba la lectura. Guarda la calibración cuando los títulos coincidan.";
    }

    private void DrawRegions()
    {
        selection.Children.Clear();
        foreach (var region in regions)
        {
            var mark = RectangleFor(region == regions[0]);
            Position(mark, new Point(region.X * selection.ActualWidth, region.Y * selection.ActualHeight),
                new Point((region.X + region.Width) * selection.ActualWidth, (region.Y + region.Height) * selection.ActualHeight));
            selection.Children.Add(mark);
        }
    }

    private static Rectangle RectangleFor(bool marker) => new()
    {
        Stroke = Overlay.Paint(marker ? "#FFD76D" : "#75EAC5"), StrokeThickness = 2, IsHitTestVisible = false
    };

    private Point Clamp(Point point) => new(Math.Clamp(point.X, 0, selection.ActualWidth),
        Math.Clamp(point.Y, 0, selection.ActualHeight));

    private static void Position(Rectangle rect, Point from, Point to)
    {
        Canvas.SetLeft(rect, Math.Min(from.X, to.X));
        Canvas.SetTop(rect, Math.Min(from.Y, to.Y));
        rect.Width = Math.Abs(from.X - to.X);
        rect.Height = Math.Abs(from.Y - to.Y);
    }
}

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Aumentos;

internal static class ArenaVoteOverlayTests
{
    internal static void Run()
    {
        var overlay = new ArenaVoteOverlay { Left = -30000, Top = -30000 };
        nint unawareTarget = 0;
        try
        {
            var handle = new WindowInteropHelper(overlay).EnsureHandle();
            double dpi = Native.GetDpiForWindow(handle) / 96.0;
            var layer = (Canvas)overlay.Content;
            ArenaVoteLabel[] labels =
            [
                new(new(7, 435, 402, 82), "A", "Control y daño para cazar equipos"),
                new(new(7, 545, 402, 82), "S", "Más aumentos para tus sinergias", true),
                new(new(7, 655, 402, 83), null, "Aún falta confirmar tu equipo")
            ];
            var full = new ArenaVoteDisplay(new(-30000, -30000, 2560, 1440), handle, labels);
            CheckPlacement(full);
            Check(ReadText(layer).SequenceEqual(
                ["A", labels[0].Reason, "S", "Recomendado", labels[1].Reason, "—", labels[2].Reason]),
                "muestra cada tier, su motivo y la recomendación");
            var tierColumn = (Grid)((DockPanel)((Border)layer.Children[0]).Child).Children[0];
            Check(tierColumn.Children[0] is TierHexagon { Background: ImageBrush, Tier: "A" },
                "la votación muestra la misma insignia hexagonal que las cartas y la selección");
            Check((Native.GetWindowLongW(handle, -20) & 0x08000020) == 0x08000020 && !overlay.IsHitTestVisible,
                "la ventana no se activa ni intercepta clics");
            Check(((Border)layer.Children[1]).BorderBrush != ((Border)layer.Children[0]).BorderBrush,
                "la recomendación tiene un marco distintivo");

            unawareTarget = CreateDpiUnawareWindow();
            Check(Native.GetDpiForWindow(unawareTarget) == 96, "la ventana de prueba simula la escala de League");
            CheckPlacement(full with { Target = unawareTarget });

            var compact = full with
            {
                Bounds = new(full.Bounds.X, full.Bounds.Y, 1280, 720),
                Labels = labels.Select(label => label with
                {
                    Row = new(label.Row.X / 2, label.Row.Y / 2, label.Row.Width / 2, label.Row.Height / 2)
                }).ToArray()
            };
            CheckPlacement(compact);
            Check(ReadText(layer).Contains("Recomendado"), "la vista compacta conserva la recomendación");

            overlay.Render(null);
            Check(!Native.IsWindowVisible(handle), "se oculta al terminar la votación");
            CheckPlacement(compact);
            overlay.Render(compact with { Labels = [new(new(1250, 540, 40, 40), "S", "Fuera de ventana")] });
            Check(!Native.IsWindowVisible(handle), "omite las posiciones inválidas sin cubrir la interfaz");

            void CheckPlacement(ArenaVoteDisplay view)
            {
                overlay.Render(view);
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                var placement = Native.Bounds(handle);
                int x = placement.X - view.Bounds.X;
                int y = placement.Y - view.Bounds.Y;
                Check(Native.IsWindowVisible(handle) && layer.Children.Count == view.Labels.Length &&
                    x > view.Labels.Max(label => label.Row.X + label.Row.Width) &&
                    x + placement.Width <= view.Bounds.Width && y >= 0 && y + placement.Height <= view.Bounds.Height,
                    "cada insignia se ubica dentro de la ventana y al costado de las opciones");
                for (int i = 0; i < layer.Children.Count; i++)
                {
                    var badge = (Border)layer.Children[i];
                    var row = view.Labels[i].Row;
                    int badgeTop = y + (int)Math.Round(Canvas.GetTop(badge) * dpi);
                    int badgeHeight = (int)Math.Round(badge.ActualHeight * dpi);
                    int expectedTop = Math.Clamp(row.Y + row.Height * 2 / 3 - badgeHeight / 2, row.Y,
                        Math.Max(row.Y, row.Y + row.Height - badgeHeight));
                    Check(badge.ActualWidth > 0 && badgeHeight > 0 &&
                        badge.ActualWidth * dpi <= placement.Width + 1 &&
                        badgeTop + badgeHeight <= y + placement.Height + 1 &&
                        Math.Abs(badgeTop - expectedTop) <= 1 && badgeTop + badgeHeight <= row.Y + row.Height + 1,
                        "la escala de la ventana del juego no desplaza ni recorta las insignias");
                }
            }
        }
        finally
        {
            overlay.Close();
            if (unawareTarget != 0)
                DestroyWindow(unawareTarget);
        }
        Console.WriteLine("Panel de votación de Arena en WPF verificado.");
    }

    private static nint CreateDpiUnawareWindow()
    {
        nint prior = SetThreadDpiAwarenessContext(-1);
        if (prior == 0)
            throw new InvalidOperationException("No se pudo crear la ventana de prueba sin escalado DPI.");
        try
        {
            return CreateWindowExW(0, "Static", "", 0, 0, 0, 20, 20, 0, 0, 0, 0);
        }
        finally { SetThreadDpiAwarenessContext(prior); }
    }

    [DllImport("user32.dll")] private static extern nint SetThreadDpiAwarenessContext(nint context);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern nint CreateWindowExW(uint extendedStyle, string className, string title, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint handle);

    private static IEnumerable<string> ReadText(UIElement element) => element switch
    {
        TierHexagon badge => [badge.Tier ?? "—"],
        TextBlock text => [text.Text],
        Panel panel => panel.Children.Cast<UIElement>().SelectMany(ReadText),
        Decorator { Child: { } child } => ReadText(child),
        _ => []
    };

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

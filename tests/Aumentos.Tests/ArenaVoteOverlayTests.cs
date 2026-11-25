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
                new(new(7, 435, 402, 82), "A"),
                new(new(7, 545, 405, 82), "S"),
                new(new(7, 655, 402, 83), null)
            ];
            var full = new ArenaVoteDisplay(new(-30000, -30000, 2560, 1440), handle, labels);
            CheckPlacement(full);
            Check(layer.Children[0] is TierHexagon { Background: ImageBrush, Tier: "A" } &&
                layer.Children[1] is TierHexagon { Background: ImageBrush, Tier: "S" },
                "la votación muestra la misma insignia hexagonal que las cartas y la selección");
            Check((Native.GetWindowLongW(handle, -20) & 0x08000020) == 0x08000020 && !overlay.IsHitTestVisible,
                "la ventana no se activa ni intercepta clics");
            double defaultHeight = ((TierHexagon)layer.Children[0]).ActualHeight;
            Check(defaultHeight > 30, "la insignia de votación es más grande con el tamaño predeterminado");
            CheckPlacement(full, 250);
            double enlargedHeight = ((TierHexagon)layer.Children[0]).ActualHeight;
            Check(enlargedHeight > defaultHeight && enlargedHeight * dpi <= labels[0].Row.Height + 1,
                "el tamaño guardado agranda una votación abierta dentro del espacio disponible");

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
            CheckPlacement(compact, Overlay.TierSizeMaximum);

            overlay.Render(null);
            Check(!Native.IsWindowVisible(handle), "se oculta al terminar la votación");
            CheckPlacement(compact);
            overlay.Render(compact with { Labels = [new(new(1250, 540, 40, 40), "S")] });
            Check(!Native.IsWindowVisible(handle), "omite las posiciones inválidas sin cubrir la interfaz");

            void CheckPlacement(ArenaVoteDisplay view, int percent = BuildSizePreference.Default)
            {
                overlay.Render(view, percent);
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                var placement = Native.Bounds(handle);
                int x = placement.X - view.Bounds.X;
                int y = placement.Y - view.Bounds.Y;
                var visible = view.Labels.Where(label => label.Tier is not null).ToArray();
                Check(Native.IsWindowVisible(handle) && layer.Children.Count == visible.Length &&
                    x >= 0 &&
                    x + placement.Width <= view.Bounds.Width && y >= 0 && y + placement.Height <= view.Bounds.Height,
                    "cada insignia se ubica dentro de la ventana y al costado de las opciones");
                for (int i = 0; i < layer.Children.Count; i++)
                {
                    var badge = (TierHexagon)layer.Children[i];
                    var row = visible[i].Row;
                    int badgeTop = y + (int)Math.Round(Canvas.GetTop(badge) * dpi);
                    int badgeLeft = x + (int)Math.Round(Canvas.GetLeft(badge) * dpi);
                    double centerX = badgeLeft + badge.ActualWidth * dpi / 2;
                    double centerY = badgeTop + badge.ActualHeight * dpi / 2;
                    Check(Math.Abs(centerX - (row.X + row.Width)) <= 1 &&
                        Math.Abs(centerY - (row.Y + (row.Height + row.Height / 3) / 2.0)) <= 1,
                        "el icono se centra sobre el borde derecho del recuadro del título");
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

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

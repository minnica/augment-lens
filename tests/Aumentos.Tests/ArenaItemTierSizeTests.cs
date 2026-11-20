using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Aumentos;

internal static class ArenaItemTierSizeTests
{
    internal static void Smoke()
    {
        var overlay = new Overlay();
        foreach (var window in overlay.Windows)
        {
            window.Left = -30000;
            window.Top = -30000;
        }

        var folder = Path.Combine(Path.GetTempPath(), $"aumentos-item-size-{Guid.NewGuid():N}");
        var path = Path.Combine(folder, "item-tier-size.json");
        var preference = new BuildSizePreference(path);
        BuildSizeWindow? settings = null;
        overlay.ItemSizeRequested += () =>
        {
            settings = new BuildSizeWindow(preference.Load(), overlay.CreateItemTierPreview(), percent =>
            {
                overlay.SetItemTierSize(percent);
                return preference.Save(percent);
            })
            {
                Left = -30000, Top = -30000, ShowActivated = false,
                WindowStartupLocation = WindowStartupLocation.Manual
            };
            settings.Show();
        };

        try
        {
            var bounds = new Int32Rect(-30000, -30000, 2560, 1440);
            var layout = new Calibration(bounds.Width, bounds.Height, new Region(.45, .76, .1, .04),
                [new Region(.21, .386, .19, .04), new Region(.405, .386, .19, .04), new Region(.60, .386, .19, .04)], new byte[720]);
            CardComparison?[] labels =
            [
                new("C", ArenaChoice: new("Compañero de Hexproyectil", Tier.C, "Velocidad de ataque")),
                new("B", ArenaChoice: new("Jitte Kinkou", Tier.B, "Control y daño")),
                new("A", ArenaChoice: new("Reverberación", Tier.A, "Inmovilizar activa impactos", Recommended: true, TopPosition: 4))
            ];
            const int edge = 241;
            var offer = new OverlayUpdate("Prismáticos", bounds, layout, labels, Mode: Mode.Arena, ArenaCardTop: edge);
            overlay.Render(offer);
            var unchanged = Position(overlay.Windows[1]);
            overlay.Render(offer with { ArenaItemOffer = true });
            var initial = Position(overlay.Windows[1]);
            Check(initial.Width > unchanged.Width && initial.Height > unchanged.Height, "los tiers de objetos tienen un tamaño inicial mayor");

            var sizeButton = overlay.Windows[0].OwnedWindows.OfType<BuildSizeButton>().Single();
            var button = (Button)sizeButton.Content;
            var location = Position(sizeButton);
            Check(sizeButton.IsVisible && location.Y >= bounds.Y && location.Y + location.Height < bounds.Y + edge &&
                location.X > bounds.X + layout.Titles[^1].Pixels(bounds.Width, bounds.Height).X,
                "el botón de tamaño acepta clics fuera de la carta");
            var peer = new ButtonAutomationPeer(button);
            ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Check(settings is { IsVisible: true } && settings.Title.Contains("tiers de objetos", StringComparison.Ordinal),
                "el botón abre el ajuste de tiers de objetos");

            var slider = (Slider)settings!.FindName("SizeSlider");
            var preview = (FrameworkElement)((ContentPresenter)settings.FindName("PreviewHost")).Content;
            slider.Value = BuildSizePreference.Minimum;
            var smallest = Position(overlay.Windows[1]);
            slider.Value = BuildSizePreference.Maximum;
            var largest = Position(overlay.Windows[1]);
            var center = bounds.Y + edge;
            Check(smallest.Width < initial.Width && largest.Width > initial.Width &&
                Math.Abs(largest.Y + largest.Height / 2.0 - center) <= 1 &&
                largest.Y >= bounds.Y + (int)(bounds.Height * .09),
                "el deslizamiento cambia los distintivos en pantalla y mantiene su centro y el reloj");
            Check(preference.Load() == BuildSizePreference.Maximum &&
                preview.LayoutTransform is ScaleTransform { ScaleX: 1.5, ScaleY: 1.5 },
                "la vista previa y la preferencia reflejan el porcentaje");
            ((Button)settings.FindName("ResetButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(preference.Load() == BuildSizePreference.Default && Position(overlay.Windows[1]) == initial,
                "restablecer recupera el tamaño inicial");
            overlay.SetItemTierSize(BuildSizePreference.Maximum);
            overlay.Render(offer);
            Check(Position(overlay.Windows[1]) == unchanged && !sizeButton.IsVisible,
                "la escala y el botón se aplican solo durante ofertas de objetos");
        }
        finally
        {
            settings?.Close();
            overlay.Close();
            File.Delete(path);
            if (Directory.Exists(folder))
                Directory.Delete(folder);
        }
    }

    private static Int32Rect Position(Window window) => Native.Bounds(new WindowInteropHelper(window).Handle);

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}

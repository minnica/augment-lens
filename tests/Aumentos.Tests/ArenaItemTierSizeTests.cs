using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Aumentos;

internal static class ArenaItemTierSizeTests
{
    internal static void Smoke()
    {
        var folder = Directory.CreateTempSubdirectory("aumentos-item-size");
        var path = Path.Combine(folder.FullName, "item-tier-size.json");
        var preference = new BuildSizePreference(path, Overlay.TierSizeMaximum);
        var overlay = new Overlay(preference.Save);
        foreach (var window in overlay.Windows)
        {
            window.Left = -30000;
            window.Top = -30000;
        }

        try
        {
            var bounds = new Int32Rect(-30000, -30000, 2560, 1440);
            var layout = new Calibration(bounds.Width, bounds.Height, new Region(.45, .76, .1, .04),
                [new Region(.21, .386, .19, .04), new Region(.405, .386, .19, .04), new Region(.60, .386, .19, .04)], new byte[720]);
            const int edge = 241;
            var arenaAugments = new OverlayUpdate("Aumentos de Arena", bounds, layout,
                [new("A", IsArenaAugment: true), new("C", IsArenaAugment: true), new("B", IsArenaAugment: true)],
                Mode: Mode.Arena, ArenaCardTop: edge);
            overlay.Render(arenaAugments);
            var augmentDefault = Position(overlay.Windows[1]);
            var arenaFragments = arenaAugments with
            {
                Status = "Fragmentos de Arena",
                Labels =
                [
                    new("A", ArenaChoice: new("Penetración de magia", Tier.A, "Mejora el daño mágico", Recommended: true)),
                    new("B", ArenaChoice: new("Paquete de cuidados", Tier.B, "Recursos adicionales")),
                    new("B", ArenaChoice: new("Curación", Tier.B, "Refuerza la supervivencia"))
                ]
            };
            overlay.Render(arenaFragments);
            var fragmentDefault = Position(overlay.Windows[1]);
            var prismatics = new PrismaticDisplay(new([], 0), layout, "26.23");
            var itemOffer = arenaAugments with
            {
                Status = "Objetos de Arena", ArenaItemOffer = true, Prismatics = prismatics,
                Labels =
                [
                    new("C", ArenaChoice: new("Compañero de Hexproyectil", Tier.C, "Velocidad de ataque")),
                    new("B", ArenaChoice: new("Jitte Kinkou", Tier.B, "Control y daño")),
                    new("A", ArenaChoice: new("Reverberación", Tier.A, "Inmovilizar activa impactos", Recommended: true, TopPosition: 4))
                ]
            };
            overlay.Render(itemOffer);
            var initial = Position(overlay.Windows[1]);
            int lowerEdge = bounds.Y + edge + (int)Math.Round(bounds.Height * .465);
            int rerollTop = lowerEdge + (int)(bounds.Height * .037);
            Check(initial == fragmentDefault && initial == augmentDefault &&
                Math.Abs(initial.Y + initial.Height / 2.0 - lowerEdge) <= 1,
                "objetos, fragmentos y aumentos comparten tamaño y posición en el marco inferior de Arena");

            var sizeButton = overlay.Windows.SelectMany(window => window.OwnedWindows.OfType<BuildSizeButton>()).Single();
            var titles = layout.Titles.Select(title => title.Pixels(bounds.Width, bounds.Height)).ToArray();
            var buttonPosition = Position(sizeButton);
            Check(sizeButton.IsVisible && buttonPosition.Y >= bounds.Y && buttonPosition.Y + buttonPosition.Height < bounds.Y + edge &&
                buttonPosition.X > bounds.X + titles[^1].X + titles[^1].Width && RightAligned(bounds, buttonPosition) &&
                sizeButton.Content is Button { Content: "Tamaño de los tiers" },
                "el botón identifica el ajuste de tiers y queda fuera de la oferta, en el borde derecho");

            var slider = OpenEditor(sizeButton, 100);
            var editorPosition = Position(sizeButton);
            Check(RightAligned(bounds, editorPosition) && editorPosition.Y == buttonPosition.Y,
                "el ajuste abierto conserva el anclaje al borde derecho");
            slider.ApplyTemplate();
            var track = (Track)slider.Template.FindName("PART_Track", slider);
            track.Thumb.RaiseEvent(new DragDeltaEventArgs(50, 0) { RoutedEvent = Thumb.DragDeltaEvent });
            Check(slider.Value > 100 && preference.Load() == (int)slider.Value && Position(sizeButton) == editorPosition,
                "el arrastre redimensiona en partida y guarda el valor sin mover el control");
            slider.Value = BuildSizePreference.Minimum;
            var smallest = Position(overlay.Windows[1]);
            slider.Value = Overlay.TierSizeMaximum;
            var largest = Position(overlay.Windows[1]);
            Check(smallest.Width < initial.Width && largest.Width > initial.Width * 2 &&
                largest.Y < initial.Y && largest.Y >= bounds.Y + titles[1].Y + titles[1].Height &&
                largest.Y + largest.Height <= rerollTop &&
                Math.Abs(largest.X + largest.Width / 2.0 - (bounds.X + titles[1].X + titles[1].Width / 2)) <= 1,
                "el tamaño máximo de los tiers de objetos crece hacia arriba y deja libre el botón de cambio");
            Check(preference.Load() == Overlay.TierSizeMaximum && overlay.TierSizePercent == Overlay.TierSizeMaximum &&
                ((TextBlock)((BuildScaleEditor)sizeButton.Content).FindName("PercentText")).Text == "300 %" &&
                Position(sizeButton) == editorPosition, "el ajuste visible y la preferencia reflejan el porcentaje");
            slider.Value = BuildSizePreference.Default;
            Check(preference.Load() == BuildSizePreference.Default && Position(overlay.Windows[1]) == initial,
                "100 % recupera el tamaño inicial");
            var editor = (BuildScaleEditor)sizeButton.Content;
            editor.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = Mouse.MouseLeaveEvent });
            WaitForAutoClose();
            Check(sizeButton.Content is Button && sizeButton.IsVisible && overlay.IsVisible && Position(sizeButton) == buttonPosition,
                "apartarse del ajuste vuelve al botón y mantiene visibles las cartas");

            slider = OpenEditor(sizeButton, 100);
            slider.Value = BuildSizePreference.Maximum;
            overlay.Render(arenaAugments);
            Check(Position(overlay.Windows[1]).Width > augmentDefault.Width && sizeButton.IsVisible && sizeButton.Content is Button,
                "el mismo ajuste escala aumentos de Arena y cierra el editor de una oferta anterior");
            OpenEditor(sizeButton, BuildSizePreference.Maximum).Value = BuildSizePreference.Default;
            overlay.Render(arenaAugments with { ArenaUpgradeHeading = true });
            Check(sizeButton.IsVisible && sizeButton.Content is Button, "el control aparece al presentar mejoras de Arena");

            var mayhem = new OverlayUpdate("Aumentos de ARAM Caos", bounds, layout,
                [new("A", new OpGgCard("A")), new(CardComparison.Missing, new OpGgCard("B")), new("A", new OpGgCard("S"))],
                Mode: Mode.Caos);
            overlay.Render(mayhem);
            var mayhemDefault = Position(overlay.Windows[1]);
            slider = OpenEditor(sizeButton, 100);
            slider.Value = BuildSizePreference.Minimum;
            var mayhemSmallest = Position(overlay.Windows[1]);
            slider.Value = BuildSizePreference.Maximum;
            var mayhemLargest = Position(overlay.Windows[1]);
            Check(mayhemSmallest.Width < mayhemDefault.Width && mayhemSmallest.Height < mayhemDefault.Height &&
                mayhemLargest.Width > mayhemDefault.Width && mayhemLargest.Height > mayhemDefault.Height,
                "el deslizador cambia el ancho y el alto de un tier de ARAM Caos ya visible");
            Check(overlay.Windows.Where(window => window.IsVisible).Select(Position).All(position =>
                position.Width == mayhemLargest.Width && position.Height == mayhemLargest.Height && position.Y == mayhemLargest.Y),
                "la escala conserva el mismo tamaño y alineación para toda la oferta de ARAM Caos");
            slider.Value = BuildSizePreference.Default;
            Check(Position(overlay.Windows[1]) == mayhemDefault,
                "100 % recupera también el tamaño original en ARAM Caos");

            var compact = new Int32Rect(bounds.X, bounds.Y, 1280, 720);
            var wideCards = layout with
            {
                Titles = [new(.06, .39, .16, .04), new(.3, .39, .16, .04), new(.54, .39, .16, .04), new(.78, .39, .16, .04)]
            };
            overlay.Render(mayhem with { Bounds = compact, Layout = wideCards,
                Labels = [.. mayhem.Labels!, new("S", new OpGgCard("S"))] });
            var compactDefault = Position(overlay.Windows[1]);
            slider = OpenEditor(sizeButton, 100);
            slider.Value = BuildSizePreference.Minimum;
            var compactSmallest = Position(overlay.Windows[1]);
            Check(compactSmallest.Width < compactDefault.Width && compactSmallest.Height < compactDefault.Height,
                "en 1280×720 el 75 % reduce el tamaño que cabe al 100 %");
            slider.Value = BuildSizePreference.Maximum;
            var compactControl = Position(sizeButton);
            Check(compactControl.X >= compact.X + compact.Width * 2 / 3 && RightAligned(compact, compactControl) &&
                overlay.Windows.Where(window => window.IsVisible).Select(Position)
                    .All(rect => rect.Y >= compactControl.Y + compactControl.Height),
                "en 1280×720 el ajuste cabe por encima de todas las cartas sin cubrir el centro");

            var shiftedLayout = layout with { Titles = layout.Titles.Select(title => title with { X = title.X - .09 }).ToArray() };
            var rightPrismatics = prismatics with { Layout = shiftedLayout };
            overlay.Render(itemOffer with { Bounds = compact, Layout = shiftedLayout, ArenaCardTop = 120, Prismatics = rightPrismatics });
            var topPanel = new BuildOverlay(savePrismaticSize: _ => true) { Left = -30000, Top = -30000 };
            try
            {
                topPanel.Render(new BuildDisplay(compact, 0, "Elise", Mode.Arena, null, Prismatics: rightPrismatics));
                var topButton = topPanel.OwnedWindows.OfType<BuildSizeButton>().Single();
                OpenEditor(sizeButton, BuildSizePreference.Maximum);
                var tierEditor = Position(sizeButton);
                var topControl = Position(topButton);
                Check(RightAligned(compact, tierEditor) && topControl.X > compact.X + compact.Width / 2 &&
                    tierEditor.Y + tierEditor.Height < topControl.Y,
                    "cuando el Top 5 está a la derecha, su botón queda debajo del ajuste de tiers");
            }
            finally { topPanel.Close(); }

            var normal = new Int32Rect(bounds.X, bounds.Y, 1600, 900);
            var mayhemFragments = mayhem with { Status = "Fragmentos de ARAM Caos", Labels = arenaFragments.Labels };
            foreach (var viewport in new[] { compact, normal, bounds })
            foreach (var offer in new[] { mayhem, mayhemFragments, arenaAugments, arenaFragments, itemOffer })
            {
                var fixture = layout with { Width = viewport.Width, Height = viewport.Height };
                int cardTop = (int)Math.Round(viewport.Height * .167);
                var scaledOffer = offer with { Bounds = viewport, Layout = fixture, ArenaCardTop = cardTop };
                overlay.Hide();
                overlay.SetTierSize(BuildSizePreference.Maximum);
                overlay.Render(scaledOffer);
                var formerMaximum = Position(overlay.Windows[1]);
                slider = OpenEditor(sizeButton, BuildSizePreference.Maximum);
                slider.Value = Overlay.TierSizeMaximum;
                var newMaximum = Position(overlay.Windows[1]);
                var title = fixture.Titles[1].Pixels(viewport.Width, viewport.Height);
                int theoreticalTop = offer.Mode == Mode.Arena ? cardTop : title.Y - (int)Math.Round(viewport.Height * .213);
                int viewportRerollTop = viewport.Y + theoreticalTop + (int)Math.Round(viewport.Height * .465) + (int)(viewport.Height * .037);
                Check(newMaximum.Width > formerMaximum.Width * 1.6 && newMaximum.Height > formerMaximum.Height * 1.6 &&
                    Math.Abs(newMaximum.X + newMaximum.Width / 2.0 - (viewport.X + title.X + title.Width / 2)) <= 1 &&
                    newMaximum.Y >= viewport.Y + title.Y + title.Height && newMaximum.Y + newMaximum.Height <= viewportRerollTop,
                    $"el nuevo máximo agranda los hexágonos de {offer.Status} a {viewport.Width}×{viewport.Height} y libera los botones");
                Check(preference.Load() == Overlay.TierSizeMaximum &&
                    ((TextBlock)((BuildScaleEditor)sizeButton.Content).FindName("PercentText")).Text == "300 %",
                    "el nuevo máximo se muestra y se guarda");
            }

            overlay.Hide();
            Check(!overlay.IsVisible && !sizeButton.IsVisible && sizeButton.Content is Button &&
                preference.Load() == Overlay.TierSizeMaximum, "al cerrar la oferta desaparece el control y se conserva la escala");

            bool RightAligned(Int32Rect area, Int32Rect control) => control.X + control.Width == area.X + area.Width -
                (int)Math.Round(14 * VisualTreeHelper.GetDpi(overlay.Windows[0]).DpiScaleX);
        }
        finally
        {
            overlay.Close();
            File.Delete(path);
            folder.Delete();
        }
    }

    private static Int32Rect Position(Window window) => Native.Bounds(new WindowInteropHelper(window).Handle);

    private static Slider OpenEditor(BuildSizeButton sizeButton, int percent)
    {
        Check(sizeButton.IsVisible && sizeButton.Content is Button, "el botón de tiers está visible");
        var peer = new ButtonAutomationPeer((Button)sizeButton.Content);
        ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        var slider = ((BuildScaleEditor)sizeButton.Content).FindName("SizeSlider") as Slider;
        Check(slider is { Minimum: BuildSizePreference.Minimum, Maximum: Overlay.TierSizeMaximum } && slider.Value == percent,
            "Tiers abre el ajuste en partida y recupera la escala guardada");
        return slider!;
    }

    private static void WaitForAutoClose()
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = BuildScaleEditor.PointerExitDelay + TimeSpan.FromMilliseconds(100) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}

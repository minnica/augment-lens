using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Aumentos;

internal static class ArenaBuildOverlayTests
{
    internal static void Run(Ratings ratings)
    {
        var jaxRoute = new MayhemBuildPlan([
            new(new(223071, "Cuchilla Oscura"), "Comprado", Owned: true),
            new(new(223047, "Punteras de Acero Revestidas"), "Comprado", Owned: true),
            new(new(443054, "Garras de Acero Oscuro"), "Comprado", Owned: true),
            new(new(447116, "Jitte Kinkou"), "Comprado", Owned: true),
            new(new(226609, "Sierraespada Quimpunk"), "Para reducir la curación de Karma y Zed"),
            new(new(223143, "Presagio de Randuin"), "Contra los golpes críticos de Zed")
        ]);
        var clientPatch = ratings.Catalog.Patch;
        var build = new OpGgBuildSnapshot(Mode.Arena, "Jax", clientPatch, clientPatch, DateTimeOffset.UtcNow,
            [new(OpGgBuildSection.Prismatic, [[new(443090, "Cuota del Segador")]])]);
        var rates = new ShopItemRates(clientPatch, DateTimeOffset.UtcNow, new() { [226609] = new(31.4m, 51.2m) });
        var view = new BuildDisplay(new Int32Rect(0, 0, 1600, 900), 0, "Jax", Mode.Arena, new(build, "Prueba"),
            Plan: jaxRoute, ClientPatch: clientPatch, ArenaRivalsDetected: true, EnemyDamage: new(67), LobbyDamage: new(45), ItemRates: rates);
        var renderer = new BuildPanel(null);
        var panel = (StackPanel)renderer.Child;
        renderer.Render(view);
        var blocks = panel.Children.OfType<TextBlock>().Select(text => text.Text).ToArray();
        Check(blocks.SequenceEqual(["Cuarto objeto", "Objetos prismáticos", $"OP.GG · Arena · {LeaguePatch.Public(clientPatch)}",
            "Daño rival estimado", "Según rivales de rondas jugadas", "Daño del lobby", "Solo rivales identificados"]) &&
            ReadText(panel.Children[^4]).SequenceEqual(["67% físico", "33% mágico"]) &&
            ReadText(panel.Children[^1]).SequenceEqual(["45% físico", "55% mágico"]),
            "Arena distingue los rivales recientes de los que identifica en todo el lobby");
        var next = (Border)panel.Children[2];
        var pending = (Border)panel.Children[3];
        Check(panel.Children[0] == renderer.DragHandle && next.Background is not null &&
            panel.Children.OfType<Border>().All(row => row.BorderThickness == new Thickness(0) &&
                (row == next || row.Background is null)) &&
            ((DockPanel)next.Child).Children.OfType<TextBlock>().Single().Text == "El mejor" &&
            ReadText(next).SequenceEqual(["El mejor", "Sierraespada Quimpunk", "31.4 % elección", "51.2 % victorias", "Para reducir la curación de Karma y Zed"]) &&
            ReadText(pending).SequenceEqual(["Presagio de Randuin", "Contra los golpes críticos de Zed"]) &&
            !ReadText(panel).Contains("Cuchilla Oscura") && !ReadText(panel).Contains("Botas"),
            "el mejor candidato muestra las tasas de Arena y las compras confirmadas se quitan de las opciones");

        var empty = new MayhemBuildPlan(jaxRoute.Steps.Select(step => step with
            { Detail = "Encaja con tu campeón", Owned = false }).ToArray());
        renderer.Render(view with { Plan = empty, Source = null, ArenaRivalsDetected = false, EnemyDamage = null, LobbyDamage = null });
        var emptyLabels = ReadText(panel).ToArray();
        Check(emptyLabels.Contains("Esperando confirmar rivales.") &&
            emptyLabels.Contains("Esperando identificar rivales.") && !emptyLabels.Any(label => label.EndsWith("% físico")) &&
            emptyLabels.Contains("Primer objeto") && emptyLabels.Contains("Botas") &&
            emptyLabels.Count(label => label == "El mejor") == 1 && panel.Children.OfType<Border>().Count() == 4,
            "la etapa inicial ofrece tres objetos y las botas por separado");
        renderer.Render(view with { Plan = empty, Source = null, ArenaRivalsDetected = false, EnemyDamage = null }, 215);
        renderer.Measure(new Size(215, double.PositiveInfinity));
        var narrowLabels = ReadText(panel).ToArray();
        Check(!narrowLabels.Contains("Encaja con tu campeón") && narrowLabels.Contains("Esperando confirmar rivales.") &&
            narrowLabels.Contains("45% físico") && narrowLabels.Contains("55% mágico") &&
            renderer.DesiredSize.Width <= 215 && panel.Children.OfType<Border>().Count() == 4,
            "la vista estrecha permite ver el daño del lobby mientras se confirman los rivales");

        renderer.Render(view with { Mode = Mode.Caos, Source = null });
        var mayhemLabels = ReadText(panel).ToArray();
        Check(mayhemLabels.Contains("Daño rival estimado") && !mayhemLabels.Contains("Daño del lobby") &&
            !mayhemLabels.Contains("45% físico"), "la barra del lobby pertenece a la tienda de Arena");

        var full = new MayhemBuildPlan(jaxRoute.Steps.Select(step => step with { Detail = null, Owned = true }).ToArray());
        renderer.Render(view with { Plan = full, Source = null }, 215);
        renderer.Measure(new Size(215, double.PositiveInfinity));
        var fullLabels = ReadText(panel).ToArray();
        Check(fullLabels.Skip(1).First() == "Build completa" && fullLabels.Contains("Daño rival estimado") &&
            !fullLabels.Contains("El mejor") && renderer.DesiredSize.Width == 215,
            "Arena aconseja conservar el inventario lleno cuando no hay un cambio, también en el panel estrecho");
        var swap = new BuildSwap(full.Steps[^1].Item, new(223156, "Fauces de Malmortius"), "Contra el daño mágico de Brand");
        renderer.Render(view with { Plan = full with { Swap = swap }, Source = null }, 215);
        Check(ReadText(panel.Children[1]).SequenceEqual(
            ["Cambio sugerido", "Sale", "Presagio de Randuin", "Entra", "Fauces de Malmortius", "Contra el daño mágico de Brand"]) &&
            !ReadText(panel).Contains("Build completa") && ReadText(panel).Contains("Daño rival estimado"),
            "el cambio muestra los dos objetos y el motivo sin alterar el inventario equipado");
        renderer.Render(view with { Champion = null });
        Check(ReadText(panel).SequenceEqual(["Recomendaciones de objetos", "No se reconoció tu campeón."]),
            "si falta el campeón, el aviso reemplaza los datos anteriores");
        var previous = LeaguePatch.Previous(clientPatch);
        if (previous is not null)
        {
            renderer.Render(view with { Source = new(build with { SourcePatch = previous }, "Prueba") });
            Check(ReadText(panel).Contains($"Build de OP.GG · Parche anterior {LeaguePatch.Public(previous)}"),
                "se conserva el aviso solo para recomendaciones de un parche anterior");
        }
        CheckResizeAndRefresh(view);
        Console.WriteLine("Tienda de Arena verificada con próximas compras, inventario separado y ruta completa.");
    }

    private static void CheckResizeAndRefresh(BuildDisplay view)
    {
        var overlay = new BuildOverlay(saveSize: _ => true) { Left = -30000, Top = -30000 };
        try
        {
            view = view with { Bounds = new Int32Rect(-30000, -30000, 2560, 1440), Shop = new Int32Rect(420, 220, 1100, 810) };
            overlay.Render(view);
            var panel = (BuildPanel)((Viewbox)overlay.Content).Child;
            double initialHeight = overlay.Height;
            overlay.SetSize(BuildSizePreference.Maximum);
            Check(overlay.Height > initialHeight && ReadText(panel).Contains("45% físico"),
                "el tamaño de la tienda amplía ambas barras de daño");
            overlay.Render(view with { LobbyDamage = new(58) });
            var labels = ReadText(panel).ToArray();
            Check(labels.Contains("58% físico") && labels.Contains("42% mágico") && labels.Contains("67% físico") &&
                !labels.Contains("45% físico"), "los cambios del lobby refrescan la barra sin modificar la de rivales");

            var foreground = Native.GetForegroundWindow();
            var dpi = VisualTreeHelper.GetDpi(overlay).DpiScaleX;
            int Pixels(int dips) => (int)Math.Round(dips * dpi);
            var compactView = view with
            {
                Bounds = new Int32Rect(-30000, -30000, Pixels(1280), Pixels(1080)),
                Shop = new Int32Rect(Pixels(100), Pixels(150), Pixels(1080), Pixels(760))
            };
            overlay.SetSize(100);
            overlay.Render(compactView);
            var grip = overlay.OwnedWindows.OfType<BuildDragSurface>().Single();
            var window = new WindowInteropHelper(overlay).Handle;
            var gripWindow = new WindowInteropHelper(grip).Handle;
            var initial = Native.Bounds(window);
            var input = Native.Bounds(gripWindow);
            var sizeControl = overlay.OwnedWindows.OfType<BuildSizeButton>().Single();
            var sizeWindow = new WindowInteropHelper(sizeControl).Handle;
            var sizeStart = Native.Bounds(sizeWindow);
            Check(grip.IsVisible && (Native.GetWindowLongW(gripWindow, -20) & 0x08000020) == 0x08000000 &&
                (Native.GetWindowLongW(window, -20) & 0x08000020) == 0x08000020 &&
                input.X >= initial.X && input.X + input.Width <= initial.X + initial.Width &&
                input.Y > initial.Y && input.Height < initial.Height / 2 && Native.GetForegroundWindow() == foreground,
                "solo la cabecera acepta ratón y el panel no toma el foco del juego");

            var start = new Point(input.X + input.Width / 2, input.Y + input.Height / 2);
            var destination = new Point(compactView.Bounds.X + Pixels(175), compactView.Bounds.Y + Pixels(68));
            overlay.StartDrag(start);
            var offset = destination - new Point(initial.X, initial.Y);
            for (int step = 1; step <= 120; step++)
                overlay.MoveDrag(start + offset * (step / 120.0));
            var moved = Native.Bounds(window);
            var movedInput = Native.Bounds(gripWindow);
            var movedSize = Native.Bounds(sizeWindow);
            Check(moved.X == destination.X && moved.Y == destination.Y && moved.Width == initial.Width && moved.Height == initial.Height,
                $"se arrastra el panel de Arena a {destination} conservando su tamaño · antes {initial} · después {moved}");
            Check(movedInput == input with { X = input.X + (int)offset.X, Y = input.Y + (int)offset.Y } &&
                movedSize == sizeStart with { X = sizeStart.X + (int)offset.X, Y = sizeStart.Y + (int)offset.Y },
                "la cabecera y el control de tamaño se trasladan junto al panel");
            overlay.Render(compactView with { LobbyDamage = new(54) });
            var shiftedShop = compactView.Shop!.Value with { X = compactView.Shop.Value.X + 1 };
            overlay.Render(compactView with { Shop = shiftedShop, LobbyDamage = new(58) });
            Check(Native.Bounds(window) == moved && ReadText(panel).Contains("45% físico"),
                "el movimiento usa la superficie que ya está dibujada hasta soltar");
            overlay.StopDrag(refresh: true);
            Check(Native.Bounds(window).X == moved.X && Native.Bounds(window).Y == moved.Y && ReadText(panel).Contains("58% físico"),
                "se aplica la actualización más reciente en el mismo punto al soltar");
            overlay.Render(null);
            Check(!grip.IsVisible, "cerrar la tienda oculta también la zona de arrastre");
            overlay.Render(compactView);
            Check(grip.IsVisible && Native.Bounds(window) == moved, "al reabrir la tienda el panel conserva su posición");

            overlay.Render(compactView with { Mode = Mode.Caos });
            var mayhem = Native.Bounds(window);
            var disclosureWindow = new WindowInteropHelper(overlay.MayhemDisclosure!).Handle;
            var disclosure = Native.Bounds(disclosureWindow);
            overlay.StartDrag(new Point(mayhem.X + 20, mayhem.Y + 20));
            overlay.MoveDrag(new Point(compactView.Bounds.X - 200, compactView.Bounds.Y - 200));
            var clamped = Native.Bounds(window);
            Check(clamped.X >= compactView.Bounds.X && clamped.X < mayhem.X &&
                clamped.Y >= compactView.Bounds.Y && clamped.Y <= mayhem.Y && clamped.Width == mayhem.Width,
                "en ARAM Caos también se puede mover el panel y se queda dentro del cliente");
            Check(Native.Bounds(disclosureWindow) == disclosure with
                { X = disclosure.X + clamped.X - mayhem.X, Y = disclosure.Y + clamped.Y - mayhem.Y },
                "el control de minimizar sigue el panel de ARAM Caos durante el arrastre");
            overlay.Render(compactView with { Mode = Mode.Caos, Shop = null });
            Check(!grip.IsVisible && overlay.IsVisible, "el atajo sin tienda muestra la build sin la zona de arrastre");
        }
        finally { overlay.Close(); }
    }

    private static IEnumerable<string> ReadText(UIElement element) => element switch
    {
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

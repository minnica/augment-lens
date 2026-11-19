using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
        var view = new BuildDisplay(new Int32Rect(0, 0, 1600, 900), 0, "Jax", Mode.Arena, new(build, "Prueba"),
            Plan: jaxRoute, ClientPatch: clientPatch, ArenaRivalsDetected: true, EnemyDamage: new(67));
        var renderer = new BuildPanel(null);
        var panel = (StackPanel)renderer.Child;
        renderer.Render(view);
        var blocks = panel.Children.OfType<TextBlock>().Select(text => text.Text).ToArray();
        Check(blocks.SequenceEqual(["Después", "Equipados · 4 de 6", "Objetos prismáticos",
            "Daño rival estimado", "Según rivales de rondas jugadas"]) &&
            ReadText(panel.Children[^1]).SequenceEqual(["67% físico", "33% mágico"]),
            "Arena abre directo en los objetos y muestra la estimación de daño al pie");
        var next = panel.Children.OfType<Border>().Single();
        Check(panel.Children[0] == next && next.BorderThickness.Left > 0 && next.Background is not null &&
            ReadText(next).SequenceEqual(["Siguiente compra", "Sierraespada Quimpunk"]) &&
            ReadText(panel.Children.OfType<DockPanel>().Single()).SequenceEqual(["2.", "Presagio de Randuin"]),
            "la siguiente compra se destaca y las filas conservan posición y objeto");
        var owned = panel.Children.OfType<UniformGrid>().Take(2).SelectMany(ReadText);
        Check(owned.SequenceEqual(jaxRoute.Steps.Take(4).Select(step => step.Item.Name)),
            "los objetos comprados se agrupan aparte y conservan sus iconos y nombres");

        var empty = new MayhemBuildPlan(jaxRoute.Steps.Select(step => step with
            { Detail = "Encaja con tu campeón", Owned = false }).ToArray());
        renderer.Render(view with { Plan = empty, Source = null, ArenaRivalsDetected = false, EnemyDamage = null });
        var emptyLabels = ReadText(panel).ToArray();
        Check(emptyLabels.Contains("Esperando confirmar rivales.") &&
            emptyLabels.Contains("Equipados · 0 de 6") &&
            emptyLabels.Count(label => label == "Siguiente compra") == 1 && panel.Children.OfType<DockPanel>().Count() == 5 &&
            !panel.Children.OfType<UniformGrid>().Any(), "ruta inicial con seis compras y sin cuadrícula de inventario vacía");
        renderer.Render(view with { Plan = empty, Source = null, ArenaRivalsDetected = false, EnemyDamage = null }, 215);
        Check(!ReadText(panel).Contains("Encaja con tu campeón") &&
            panel.Children.OfType<DockPanel>().Count() == 5, "la vista estrecha muestra las compras sin explicaciones genéricas");

        var full = new MayhemBuildPlan(jaxRoute.Steps.Select(step => step with { Detail = null, Owned = true }).ToArray());
        renderer.Render(view with { Plan = full, Source = null });
        var fullLabels = ReadText(panel).ToArray();
        Check(fullLabels.Contains("Build completa") && fullLabels.Contains("Equipados · 6 de 6") &&
            !fullLabels.Contains("Siguiente compra") && !panel.Children.OfType<DockPanel>().Any(),
            "inventario lleno sin una compra pendiente");
        var swap = new BuildSwap(full.Steps[^1].Item, new(223156, "Fauces de Malmortius"), "Contra el daño mágico de Brand");
        renderer.Render(view with { Plan = full with { Swap = swap }, Source = null }, 215);
        Check(ReadText(panel.Children.OfType<Border>().Single()).SequenceEqual(
            ["Cambio sugerido", "Sale", "Presagio de Randuin", "Entra", "Fauces de Malmortius", "Contra el daño mágico de Brand"]) &&
            ReadText(panel).Contains("Equipados · 6 de 6"),
            "el cambio muestra los dos objetos y el motivo sin alterar el inventario equipado");
        renderer.Render(view with { Champion = null });
        Check(ReadText(panel).SequenceEqual(["No se reconoció tu campeón."]),
            "si falta el campeón, el aviso reemplaza los datos anteriores");
        var previous = LeaguePatch.Previous(clientPatch);
        if (previous is not null)
        {
            renderer.Render(view with { Source = new(build with { SourcePatch = previous }, "Prueba") });
            Check(ReadText(panel).Contains($"OP.GG · Parche anterior {LeaguePatch.Public(previous)}"),
                "se conserva el aviso solo para recomendaciones de un parche anterior");
        }
        Console.WriteLine("Tienda de Arena verificada con próximas compras, inventario separado y ruta completa.");
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

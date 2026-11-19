using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
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
            new(new(226609, "Sierraespada Quimpunk"), "Curación rival · Karma, Zed"),
            new(new(223143, "Presagio de Randuin"), "Críticos rivales · Zed")
        ]);
        var clientPatch = ratings.Catalog.Patch;
        var build = new OpGgBuildSnapshot(Mode.Arena, "Jax", clientPatch, clientPatch, DateTimeOffset.UtcNow,
            [new(OpGgBuildSection.Prismatic, [[new(443090, "Cuota del Segador")]])]);
        var view = new BuildDisplay(new Int32Rect(0, 0, 1600, 900), 0, "Jax", Mode.Arena, new(build, "Prueba"),
            Plan: jaxRoute, ClientPatch: clientPatch, ArenaRivalsDetected: true, EnemyDamage: new(67));
        var overlay = new BuildOverlay();
        try
        {
            var panel = (StackPanel)((Border)((Viewbox)overlay.Content).Child).Child;
            overlay.RenderContent(view);
            var blocks = panel.Children.OfType<TextBlock>().Select(text => text.Text).ToArray();
            Check(blocks.Skip(2).SequenceEqual(["Daño rival estimado · 67% AD · 33% AP", "Rivales de rondas jugadas",
                "Por comprar · 2", "Después", "Comprados · 4/6", "Referencia de prismáticos · OP.GG"]),
                "Arena muestra el daño rival junto a las compras y la referencia prismática");
            var next = panel.Children.OfType<Border>().Single();
            Check(next.BorderThickness.Left > 0 && next.Background is not null &&
                ReadText(next).SequenceEqual(["Siguiente compra", "Sierraespada Quimpunk", "Curación rival · Karma, Zed"]) &&
                ReadText(panel.Children.OfType<DockPanel>().Single()).SequenceEqual(["2.", "Presagio de Randuin", "Críticos rivales · Zed"]),
                "la siguiente compra se destaca y el segundo objeto conserva posición y motivo");
            var owned = panel.Children.OfType<Grid>().Take(2).SelectMany(row => row.Children.OfType<StackPanel>())
                .Select(tile => AutomationProperties.GetName(((Border)tile.Children[0]).Child)).ToArray();
            Check(owned.SequenceEqual(jaxRoute.Steps.Take(4).Select(step => step.Item.Name)),
                "los objetos comprados se agrupan aparte y conservan sus iconos y nombres");

            var empty = new MayhemBuildPlan(jaxRoute.Steps.Select(step => step with
                { Detail = "Afinidad con tu campeón", Owned = false }).ToArray());
            overlay.RenderContent(view with { Plan = empty, Source = null, ArenaRivalsDetected = false, EnemyDamage = null });
            var emptyLabels = ReadText(panel).ToArray();
            Check(emptyLabels.Contains("Daño rival estimado · esperando rivales") &&
                emptyLabels.Contains("Por comprar · 6") && emptyLabels.Contains("Comprados · 0/6") &&
                emptyLabels.Count(label => label == "Siguiente compra") == 1 && panel.Children.OfType<DockPanel>().Count() == 5 &&
                !panel.Children.OfType<Grid>().Any(), "ruta inicial con seis compras y sin cuadrícula de inventario vacía");

            var full = new MayhemBuildPlan(jaxRoute.Steps.Select(step => step with { Detail = null, Owned = true }).ToArray());
            overlay.RenderContent(view with { Plan = full, Source = null });
            var fullLabels = ReadText(panel).ToArray();
            Check(fullLabels.Contains("Build completa") && fullLabels.Contains("Comprados · 6/6") &&
                !fullLabels.Contains("Siguiente compra") && !panel.Children.OfType<DockPanel>().Any(),
                "inventario lleno sin una compra pendiente");
        }
        finally { overlay.Close(); }
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

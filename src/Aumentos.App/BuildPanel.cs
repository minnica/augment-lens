using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Aumentos;

internal sealed class BuildPanel : Border
{
    internal const double ShopWidth = 372;
    internal const double CompactWidth = 282;
    internal const double PrismaticWidth = 320;

    private static readonly Brush Muted = Overlay.Paint("#B1C1D8");
    private static readonly Brush Accent = Overlay.Paint("#AAD1FF");
    private static readonly Brush PreviousPatch = Overlay.Paint("#FFD76D");
    private static readonly Brush PurchaseAccent = Overlay.Paint("#F6D28C");
    private static readonly Brush PurchaseSurface = Overlay.Paint("#30303A");
    private static readonly Brush PurchaseBorder = Overlay.Paint("#8D7759");
    private static readonly Brush Divider = Overlay.Paint("#40516B");
    private static readonly Brush ItemSurface = Overlay.Paint("#24354C");
    private static readonly Brush PhysicalDamage = Overlay.Paint("#F2AD6B");
    private static readonly Brush MagicDamage = Overlay.Paint("#B9A7FA");

    private readonly StackPanel content = new();
    private readonly BuildItemIcons? itemIcons;
    internal bool Compact => Width < CompactWidth;

    internal BuildPanel(BuildItemIcons? itemIcons)
    {
        this.itemIcons = itemIcons;
        Width = ShopWidth;
        Padding = new Thickness(16, 12, 16, 14);
        CornerRadius = new CornerRadius(9);
        BorderBrush = Overlay.Paint("#667C9F");
        BorderThickness = new Thickness(1);
        Background = Overlay.Paint("#F2172436");
        Child = content;
    }

    internal void RenderPreview(string patch)
    {
        content.Children.Clear();
        AddHeading("Lillia", "ARAM Caos");
        AddPurchase(new BuildStep(new(6653, "El Tormento de Liandry")), patch, 1);
    }

    internal void Render(BuildDisplay view, double layoutWidth = ShopWidth)
    {
        content.Children.Clear();
        Width = view.Prismatics is null ? layoutWidth : PrismaticWidth;
        Padding = Compact ? new Thickness(11, 9, 11, 10) : new Thickness(16, 12, 16, 14);
        if (view.Prismatics is { } prismatics)
        {
            RenderPrismatics(view.Champion, prismatics);
            return;
        }

        var build = view.Source?.Data;
        var mode = view.Mode == Mode.Caos ? "ARAM Caos" : "Arena";
        AddHeading(view.Champion, view.Mode == Mode.Caos && view.Plan is not null && build is not null
            ? $"{mode} · Base de OP.GG" : mode);

        if (view.Plan is { } plan)
        {
            AddPurchaseRoute(plan, view.ClientPatch ?? build?.ClientPatch ?? "");
            AddEnemyDamage(view);
        }

        if (build is { Groups.Length: > 0 })
            AddSourceBuild(view, build);
        else if (view.Plan is null)
        {
            var status = build is null ? view.Source?.Status ?? "No hay una build disponible."
                : "OP.GG no tiene recomendaciones de objetos disponibles.";
            AddLine(status, 14, Brushes.WhiteSmoke, top: 12);
        }

        if (view.Plan is null && (view.EnemyDamage is not null || build is { Groups.Length: > 0 }))
            AddEnemyDamage(view);
    }

    private void AddHeading(string champion, string mode)
    {
        AddLine($"Build de {champion}", 21, Brushes.WhiteSmoke, bold: true);
        AddLine(mode, 13, Muted);
    }

    private void AddSourceBuild(BuildDisplay view, OpGgBuildSnapshot build)
    {
        bool localArena = view.Plan is not null && view.Mode == Mode.Arena;
        var groups = build.Groups.Where(group => localArena ? group.Section == OpGgBuildSection.Prismatic :
            group.Section != OpGgBuildSection.Start || !view.HideStartingItems).ToArray();
        bool hasReferences = groups.Any(group => group.Section != OpGgBuildSection.Core || group.Choices.Length > 1);
        var heading = view.Plan is null ? "Objetos de OP.GG" : hasReferences ? "Referencias de OP.GG" : "Datos de OP.GG";
        AddDivider();
        AddLine(heading, 15, Brushes.WhiteSmoke, bold: true);
        AddLine($"{(build.IsPrevious ? "Parche anterior" : "Parche")} {LeaguePatch.Public(build.SourcePatch)}",
            12, build.IsPrevious ? PreviousPatch : Muted);

        if (localArena)
        {
            if (groups.FirstOrDefault() is { } prismatics)
            {
                AddLine("Objetos prismáticos", 13, Accent, bold: true, top: 8);
                AddItems(prismatics.Choices.SelectMany(choice => choice).DistinctBy(item => item.Id).Take(3).ToArray(),
                    build.SourcePatch, ordered: false);
            }
            return;
        }

        foreach (var group in groups)
        {
            if (group.Section == OpGgBuildSection.Core && view.Plan is not null)
            {
                if (group.Choices.ElementAtOrDefault(1) is { } alternative)
                {
                    AddLine("Otra ruta de compra", 13, Accent, bold: true, top: 8);
                    AddItems(alternative, build.SourcePatch, ordered: true);
                }
                continue;
            }
            AddLine(group.Section == OpGgBuildSection.Core ? "Orden de compra" : SectionLabel(group.Section),
                13, Accent, bold: true, top: 8);
            if ((group.Section is OpGgBuildSection.Boots or OpGgBuildSection.Prismatic) &&
                group.Choices.All(choice => choice.Length == 1))
            {
                AddItems(group.Choices.Select(choice => choice[0]).ToArray(), build.SourcePatch, ordered: false);
                continue;
            }
            foreach (var (choice, index) in group.Choices.Take(2).Select((items, index) => (items, index)))
            {
                if (index == 1)
                    AddLine("Alternativa", 13, Muted, top: 6);
                AddItems(choice, build.SourcePatch, ordered: group.Section == OpGgBuildSection.Core);
            }
        }
    }

    private void AddEnemyDamage(BuildDisplay view)
    {
        AddDivider();
        AddLine("Daño rival estimado", 14, Brushes.WhiteSmoke, bold: true);
        if (view.EnemyDamage is not { } mix)
        {
            var status = view.Mode == Mode.Arena && !view.ArenaRivalsDetected
                ? "Esperando confirmar rivales."
                : "Faltan datos para estimar el daño rival.";
            AddLine(status, 12, Muted, top: 2);
            return;
        }

        if (view.Mode == Mode.Arena)
            AddLine("Según rivales de rondas jugadas", 12, Muted);
        var distribution = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        var labels = new Grid();
        labels.Children.Add(new TextBlock { Text = $"{mix.PhysicalPercent}% físico", Foreground = PhysicalDamage,
            FontSize = 13, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Left });
        labels.Children.Add(new TextBlock { Text = $"{mix.MagicPercent}% mágico", Foreground = MagicDamage,
            FontSize = 13, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Right });
        distribution.Children.Add(labels);

        var bar = new Grid { Height = 6, Margin = new Thickness(0, 4, 0, 0) };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(mix.PhysicalPercent, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(mix.MagicPercent, GridUnitType.Star) });
        var physical = new Border { Background = PhysicalDamage,
            CornerRadius = mix.MagicPercent == 0 ? new CornerRadius(4) : new CornerRadius(4, 0, 0, 4) };
        var magic = new Border { Background = MagicDamage,
            CornerRadius = mix.PhysicalPercent == 0 ? new CornerRadius(4) : new CornerRadius(0, 4, 4, 0) };
        bar.Children.Add(physical);
        Grid.SetColumn(magic, 1);
        bar.Children.Add(magic);
        distribution.Children.Add(bar);
        content.Children.Add(distribution);
    }

    private void AddPurchaseRoute(MayhemBuildPlan plan, string patch)
    {
        var byPurchase = plan.Steps.ToLookup(step => step.Owned);
        var purchased = byPurchase[true].ToArray();
        var pending = byPurchase[false].ToArray();
        if (pending.Length > 0)
        {
            AddPurchase(pending[0], patch, 1);
            if (pending.Length > 1)
                AddLine("Después", 14, Brushes.WhiteSmoke, bold: true, top: 10);
            for (int index = 1; index < pending.Length; index++)
            {
                var step = pending[index];
                bool repeated = Compact && (step.Detail is ArenaBuilds.ChampionFit or ArenaBuilds.PublishedPick) &&
                    step.Detail == pending[index - 1].Detail;
                AddPurchase(step, patch, index + 1, showDetail: !repeated);
            }
        }
        else if (purchased.Length == 6)
            AddLine("Build completa", 17, PurchaseAccent, bold: true, top: 10);

        AddLine($"Equipados · {purchased.Length} de 6", 14, Brushes.WhiteSmoke, bold: true, top: 10);
        AddItems(purchased.Select(step => step.Item).ToArray(), patch, ordered: false);
    }

    private void AddPurchase(BuildStep step, string patch, int position, bool showDetail = true)
    {
        bool next = position == 1;
        var row = new DockPanel { Margin = next ? new Thickness(0) : new Thickness(0, 4, 0, 0) };
        if (!next)
        {
            var order = new TextBlock { Text = $"{position}.", Width = 19, Foreground = Muted, FontSize = 13,
                VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 4, 0, 0) };
            DockPanel.SetDock(order, Dock.Left);
            row.Children.Add(order);
        }

        double iconSize = next ? (Compact ? 36 : 46) : (Compact ? 23 : 28);
        var icon = ItemIcon(step.Item, patch, iconSize);
        icon.Margin = new Thickness(0, 1, 9, 0);
        icon.VerticalAlignment = next ? VerticalAlignment.Center : VerticalAlignment.Top;
        DockPanel.SetDock(icon, Dock.Left);
        row.Children.Add(icon);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        if (next)
            text.Children.Add(new TextBlock { Text = "Siguiente compra", FontSize = 13,
                Foreground = PurchaseAccent, FontWeight = FontWeights.Bold });
        double nameSize = next ? (Compact ? 17 : 19) : 14;
        text.Children.Add(new TextBlock { Text = step.Item.Name, FontSize = nameSize, Foreground = Brushes.WhiteSmoke,
            FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        if (showDetail && step.Detail is { Length: > 0 } detail)
            text.Children.Add(new TextBlock { Text = detail, FontSize = 12.5, Foreground = Muted, TextWrapping = TextWrapping.Wrap });
        row.Children.Add(text);

        if (next)
            content.Children.Add(new Border { Child = row, Background = PurchaseSurface, BorderBrush = PurchaseBorder,
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(Compact ? 7 : 9),
                Margin = new Thickness(0, 10, 0, 0) });
        else
            content.Children.Add(row);
    }

    private void RenderPrismatics(string champion, PrismaticDisplay view)
    {
        AddLine("Top 5 prismáticos", 17, Brushes.WhiteSmoke, bold: true);
        AddLine($"Afinidad con {champion}", 12, Muted);
        foreach (var (entry, index) in view.Top.Entries.Select((entry, index) => (entry, index)))
        {
            bool offered = view.Top.IsOffered(index);
            var row = new DockPanel { Margin = new Thickness(0, 9, 0, 0) };
            row.Children.Add(new TextBlock
            {
                Text = (index + 1).ToString(), Width = 20, FontSize = 13,
                FontWeight = FontWeights.SemiBold, Foreground = Accent
            });
            var icon = new Image { Width = 26, Height = 26, Margin = new Thickness(0, 1, 8, 0),
                VerticalAlignment = VerticalAlignment.Top };
            row.Children.Add(icon);
            DockPanel.SetDock(icon, Dock.Left);
            if (itemIcons is not null)
                _ = SetIcon(icon, itemIcons, view.Patch, entry.Id);
            var text = new StackPanel();
            text.Children.Add(new TextBlock
            {
                Text = $"{entry.Rating.Name} · {entry.Rating.TierText}",
                FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Brushes.WhiteSmoke,
                TextWrapping = TextWrapping.Wrap
            });
            text.Children.Add(new TextBlock
            {
                Text = entry.Rating.Reason, FontSize = 11, Foreground = Muted, TextWrapping = TextWrapping.Wrap
            });
            if (offered)
                text.Children.Add(new TextBlock { Text = "En oferta", FontSize = 11, Foreground = Accent });
            row.Children.Add(text);
            content.Children.Add(row);
        }
    }

    private void AddItems(OpGgBuildItem[] items, string patch, bool ordered)
    {
        if (Compact)
        {
            for (int i = 0; i < items.Length; i++)
                AddCompactItem(items[i], patch, ordered ? i + 1 : null);
            return;
        }

        const int columns = 3;
        int position = 0;
        foreach (var itemsInRow in items.Chunk(columns))
        {
            var row = new UniformGrid { Columns = columns, Margin = new Thickness(0, 4, 0, 2) };
            foreach (var item in itemsInRow)
            {
                var tile = new StackPanel { Margin = new Thickness(2, 0, 2, 0) };
                var iconRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
                if (ordered)
                    iconRow.Children.Add(new TextBlock { Text = $"{++position}.", FontSize = 13, Foreground = Accent,
                        Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center });
                iconRow.Children.Add(ItemIcon(item, patch, 34));
                tile.Children.Add(iconRow);
                tile.Children.Add(new TextBlock
                {
                    Text = item.Name, FontSize = 12.5, Foreground = Brushes.WhiteSmoke,
                    TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 3, 0, 0)
                });
                row.Children.Add(tile);
            }
            content.Children.Add(row);
        }
    }

    private void AddCompactItem(OpGgBuildItem item, string patch, int? position)
    {
        var row = new DockPanel { Margin = new Thickness(0, 3, 0, 0) };
        if (position is { } number)
            row.Children.Add(new TextBlock { Text = $"{number}.", Width = 20, FontSize = 13,
                Foreground = Accent, VerticalAlignment = VerticalAlignment.Center });
        var icon = ItemIcon(item, patch, 23);
        icon.Margin = new Thickness(0, 0, 7, 0);
        row.Children.Add(icon);
        row.Children.Add(new TextBlock { Text = item.Name, FontSize = 12.5, Foreground = Brushes.WhiteSmoke,
            TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
        content.Children.Add(row);
    }

    private Border ItemIcon(OpGgBuildItem item, string patch, double size)
    {
        var image = new Image { Stretch = Stretch.UniformToFill };
        AutomationProperties.SetName(image, item.Name);
        if (itemIcons is not null)
            _ = SetIcon(image, itemIcons, patch, item.Id);
        return new Border
        {
            Child = image, Width = size, Height = size, Background = ItemSurface,
            BorderThickness = new Thickness(1), BorderBrush = Divider
        };
    }

    private void AddDivider() => content.Children.Add(new Rectangle
    {
        Height = 1, Fill = Divider, Margin = new Thickness(0, Compact ? 8 : 10, 0, Compact ? 5 : 7)
    });

    private static async Task SetIcon(Image image, BuildItemIcons icons, string patch, int id)
    {
        if (await icons.Read(patch, id) is { } icon)
            image.Source = icon;
    }

    internal static string SectionLabel(OpGgBuildSection section) => section switch
    {
        OpGgBuildSection.Start => "Objetos iniciales",
        OpGgBuildSection.Boots => "Botas",
        OpGgBuildSection.Core => "Build principal",
        OpGgBuildSection.Prismatic => "Objetos prismáticos",
        _ => "Objetos"
    };

    internal static string ChoiceText(OpGgBuildSection section, OpGgBuildItem[] items) =>
        string.Join(section == OpGgBuildSection.Core ? " → " : " + ", items.Select(item => item.Name));

    private void AddLine(string text, double size, Brush foreground, bool bold = false, double top = 0) =>
        content.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = size,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = foreground,
            Margin = new Thickness(0, top, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });
}

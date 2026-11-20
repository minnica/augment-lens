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

    internal static Brush WindowSurface { get; } = VerticalGradient("#FC102A35", "#FC081923");
    internal static Brush WindowFrame { get; } = VerticalGradient("#BDA477", "#8A6A43");
    private static readonly Brush Muted = Overlay.Paint("#B1C1D8");
    private static readonly Brush Accent = Overlay.Paint("#AAD1FF");
    private static readonly Brush PreviousPatch = Overlay.Paint("#FFD76D");
    private static readonly Brush PurchaseAccent = Overlay.Paint("#F6D28C");
    private static readonly Brush PurchaseSurface = Overlay.Paint("#192E30");
    private static readonly Brush PurchaseBorder = Overlay.Paint("#927951");
    private static readonly Brush Divider = Overlay.Paint("#3C514D");
    private static readonly Brush ItemSurface = Overlay.Paint("#172E39");
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
        CornerRadius = new CornerRadius(5);
        BorderBrush = WindowFrame;
        BorderThickness = new Thickness(3, 4, 3, 3);
        Background = WindowSurface;
        Child = content;
    }

    private static Brush VerticalGradient(string top, string bottom)
    {
        var brush = new LinearGradientBrush((Color)ColorConverter.ConvertFromString(top),
            (Color)ColorConverter.ConvertFromString(bottom), 90);
        brush.Freeze();
        return brush;
    }

    internal void RenderPreview(string patch)
    {
        content.Children.Clear();
        AddPurchase(new(6653, "El Tormento de Liandry"), patch, 1);
    }

    internal void Render(BuildDisplay view, double layoutWidth = ShopWidth)
    {
        content.Children.Clear();
        Width = view.Prismatics is null ? layoutWidth : PrismaticWidth;
        Padding = Compact ? new Thickness(11, 9, 11, 10) : new Thickness(16, 12, 16, 14);
        if (string.IsNullOrWhiteSpace(view.Champion))
        {
            AutomationProperties.SetName(this, "Recomendaciones de objetos");
            AddLine("No se reconoció tu campeón.", 14, PreviousPatch, bold: true);
            return;
        }
        AutomationProperties.SetName(this, $"Recomendaciones de objetos para {view.Champion}");
        if (view.Prismatics is { } prismatics)
        {
            RenderPrismatics(view.Champion, prismatics);
            return;
        }

        var build = view.Source?.Data;
        if (view.Plan is { } plan)
            AddPurchaseRoute(plan, view.ClientPatch ?? build?.ClientPatch ?? "");

        if (build is { Groups.Length: > 0 })
            AddSourceBuild(view, build);
        else if (view.Plan is null)
        {
            var status = build is null ? view.Source?.Status ?? "No hay una build disponible."
                : "OP.GG no tiene recomendaciones de objetos disponibles.";
            AddLine(status, 14, Brushes.WhiteSmoke);
        }

        if (view.Plan is not null || view.EnemyDamage is not null || build is { Groups.Length: > 0 })
            AddEnemyDamage(view);
        if (view.Mode == Mode.Arena)
            AddLobbyDamage(view.LobbyDamage);
    }

    private void AddSourceBuild(BuildDisplay view, OpGgBuildSnapshot build)
    {
        bool localArena = view.Plan is not null && view.Mode == Mode.Arena;
        var owned = view.Plan?.Steps.Where(step => step.Owned).Select(step => MayhemBuilds.BaseId(step.Item.Id)).ToArray();
        var groups = build.Groups.Where(group => localArena ? group.Section == OpGgBuildSection.Prismatic :
            group.Section switch
            {
                OpGgBuildSection.Start => !view.HideStartingItems,
                OpGgBuildSection.Core => owned is null || (owned.Length < 6 &&
                    group.Choices.ElementAtOrDefault(1)?.Any(item => !owned.Contains(MayhemBuilds.BaseId(item.Id))) == true),
                _ => true
            }).ToArray();
        if (groups.Length == 0 && !build.IsPrevious)
            return;

        AddDivider();
        if (build.IsPrevious)
            AddLine($"OP.GG · Parche anterior {LeaguePatch.Public(build.SourcePatch)}", 12, PreviousPatch);

        if (localArena)
        {
            if (groups.FirstOrDefault() is { } prismatics)
            {
                AddSourceLabel("Objetos prismáticos");
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
                    AddSourceLabel("Ruta alternativa");
                    AddItems(alternative, build.SourcePatch, ordered: true);
                }
                continue;
            }
            AddSourceLabel(group.Section == OpGgBuildSection.Core ? "Orden de compra" : SectionLabel(group.Section));
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

        void AddSourceLabel(string label) =>
            AddLine(label, 13, Accent, bold: true, top: content.Children.Count == 0 || content.Children[^1] is Rectangle ? 0 : 8);
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
        AddDamageDistribution(mix);
    }

    private void AddLobbyDamage(EnemyDamageMix? mix)
    {
        AddLine("Daño del lobby", 14, Brushes.WhiteSmoke, bold: true, top: 10);
        if (mix is not { } damage)
        {
            AddLine("Esperando datos de los equipos.", 12, Muted, top: 2);
            return;
        }

        AddLine("Incluye a tu equipo", 12, Muted);
        AddDamageDistribution(damage);
    }

    private void AddDamageDistribution(EnemyDamageMix mix)
    {
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
            AddPurchase(pending[0].Item, patch, 1);
            if (pending.Length > 1)
                AddLine("Después", 14, Brushes.WhiteSmoke, bold: true, top: 10);
            for (int index = 1; index < pending.Length; index++)
                AddPurchase(pending[index].Item, patch, index + 1);
        }
        else if (purchased.Length == 6)
        {
            if (plan.Swap is { } swap)
                AddSwap(swap, patch);
            else
                AddLine("Build completa", 17, PurchaseAccent, bold: true);
        }

        AddLine($"Equipados · {purchased.Length} de 6", 14, Brushes.WhiteSmoke, bold: true, top: 10);
        AddItems(purchased.Select(step => step.Item).ToArray(), patch, ordered: false);
    }

    private void AddSwap(BuildSwap swap, string patch)
    {
        var card = new StackPanel();
        card.Children.Add(new TextBlock { Text = "Cambio sugerido", FontSize = 13,
            Foreground = PurchaseAccent, FontWeight = FontWeights.Bold });
        card.Children.Add(SwapItem("Sale", swap.Outgoing));
        card.Children.Add(SwapItem("Entra", swap.Incoming));
        card.Children.Add(new TextBlock { Text = swap.Reason, FontSize = 12.5, Foreground = Muted,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
        AddPurchaseCard(card);

        DockPanel SwapItem(string label, OpGgBuildItem item)
        {
            var row = new DockPanel { Margin = new Thickness(0, 5, 0, 0) };
            var icon = ItemIcon(item, patch, Compact ? 27 : 31);
            icon.Margin = new Thickness(0, 0, 8, 0);
            row.Children.Add(icon);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = label, FontSize = 11, Foreground = Muted });
            text.Children.Add(new TextBlock { Text = item.Name, FontSize = Compact ? 14 : 15,
                Foreground = Brushes.WhiteSmoke, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            row.Children.Add(text);
            return row;
        }
    }

    private void AddPurchase(OpGgBuildItem item, string patch, int position)
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
        var icon = ItemIcon(item, patch, iconSize);
        icon.Margin = new Thickness(0, 1, 9, 0);
        icon.VerticalAlignment = next ? VerticalAlignment.Center : VerticalAlignment.Top;
        DockPanel.SetDock(icon, Dock.Left);
        row.Children.Add(icon);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        if (next)
            text.Children.Add(new TextBlock { Text = "Siguiente compra", FontSize = 13,
                Foreground = PurchaseAccent, FontWeight = FontWeights.Bold });
        double nameSize = next ? (Compact ? 17 : 19) : 14;
        text.Children.Add(new TextBlock { Text = item.Name, FontSize = nameSize, Foreground = Brushes.WhiteSmoke,
            FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        row.Children.Add(text);

        if (next)
            AddPurchaseCard(row);
        else
            content.Children.Add(row);
    }

    private void AddPurchaseCard(UIElement child) =>
        content.Children.Add(new Border { Child = child, Background = PurchaseSurface, BorderBrush = PurchaseBorder,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(Compact ? 7 : 9) });

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

    private void AddDivider()
    {
        if (content.Children.Count > 0)
            content.Children.Add(new Rectangle
            {
                Height = 1, Fill = Divider, Margin = new Thickness(0, Compact ? 8 : 10, 0, Compact ? 5 : 7)
            });
    }

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

using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Aumentos;

internal sealed class BuildPanel : Border
{
    internal const double ShopWidth = 310;
    internal const double CompactWidth = 270;
    internal const double PrismaticWidth = 320;

    private static readonly Brush Muted = OverlayPanelStyle.Muted;
    private static readonly Brush Accent = Overlay.Paint("#AAD1FF");
    private static readonly Brush PreviousPatch = Overlay.Paint("#FFD76D");
    private static readonly Brush PurchaseAccent = OverlayPanelStyle.Gold;
    private static readonly Brush PurchaseSurface = Overlay.Paint("#192E30");
    private static readonly Brush PurchaseBorder = Overlay.Paint("#927951");
    private static readonly Brush Divider = OverlayPanelStyle.Divider;
    private static readonly Brush ItemSurface = Overlay.Paint("#172E39");
    private static readonly Brush RowSurface = Overlay.Paint("#192F3A");
    private static readonly Brush PhysicalDamage = Overlay.Paint("#F2AD6B");
    private static readonly Brush MagicDamage = Overlay.Paint("#B9A7FA");

    private readonly StackPanel content = new();
    private readonly TextBlock shopTitle = new() { FontSize = 15, FontFamily = OverlayPanelStyle.Heading,
        FontWeight = FontWeights.Bold, Foreground = OverlayPanelStyle.Gold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Path shopGrip = new()
    {
        Data = Geometry.Parse("M 0,0 L 10,0 M 0,3 L 10,3 M 0,6 L 10,6"),
        Stroke = OverlayPanelStyle.Gold, StrokeThickness = 1, SnapsToDevicePixels = true,
        HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 0, 3, 0), Visibility = Visibility.Collapsed
    };
    private readonly BuildItemIcons? itemIcons;
    internal FrameworkElement DragHandle { get; }
    internal bool Compact => Width < CompactWidth;

    internal BuildPanel(BuildItemIcons? itemIcons)
    {
        this.itemIcons = itemIcons;
        Width = ShopWidth;
        Padding = new Thickness(16, 12, 16, 14);
        CornerRadius = new CornerRadius(5);
        BorderBrush = OverlayPanelStyle.Frame;
        BorderThickness = new Thickness(3, 4, 3, 3);
        Background = OverlayPanelStyle.Surface;
        Child = content;
        DragHandle = CreateShopHeader();
    }

    internal void RenderPreview(string patch)
    {
        content.Children.Clear();
        AddShopHeader(null);
        AddPurchase(new(6653, "El Tormento de Liandry"), patch, 1);
    }

    internal void Render(BuildDisplay view, double layoutWidth = ShopWidth)
    {
        content.Children.Clear();
        Width = view.Prismatics is null ? layoutWidth : PrismaticWidth;
        Padding = view.Prismatics is not null ? new Thickness(16, 12, 16, 14)
            : Compact ? new Thickness(9, 7, 9, 9) : new Thickness(12, 8, 12, 11);
        if (view.Prismatics is null)
            AddShopHeader(view.Champion);
        if (string.IsNullOrWhiteSpace(view.Champion))
        {
            AutomationProperties.SetName(this, "Recomendaciones de objetos");
            AddLine("No se reconoció tu campeón.", 14, PreviousPatch, bold: true);
            return;
        }
        AutomationProperties.SetName(this, $"Recomendaciones de objetos para {view.Champion}");
        if (view.Prismatics is { } prismatics)
        {
            RenderPrismatics(view.Champion, prismatics.Top, prismatics.Patch);
            return;
        }

        var build = view.Source?.Data;
        if (view.Plan is { } plan)
        {
            if (plan.IsLocalRoute)
                AddLine("Ruta local · Según tu partida", 12, Accent, bold: true);
            AddPurchaseRoute(plan, view.ClientPatch ?? build?.ClientPatch ?? "");
        }

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
                    AddSourceLabel("Ruta alternativa · OP.GG");
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

    private Grid CreateShopHeader()
    {
        var header = new Grid { Background = Brushes.Transparent, MinHeight = 28, Margin = new Thickness(0, 0, 0, 7) };
        header.Children.Add(shopGrip);
        shopTitle.Margin = new Thickness(2, 0, 19, 0);
        shopTitle.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(shopTitle);
        header.Children.Add(new Rectangle { Fill = Divider, Height = 1, VerticalAlignment = VerticalAlignment.Bottom });
        AutomationProperties.SetName(header, "Mover panel de recomendaciones de objetos");
        return header;
    }

    private void AddShopHeader(string? champion)
    {
        shopTitle.Text = string.IsNullOrWhiteSpace(champion) ? "Recomendaciones de objetos" : $"Build de {champion}";
        content.Children.Add(DragHandle);
    }

    internal void SetShopDragEnabled(bool enabled)
    {
        DragHandle.Cursor = enabled ? Cursors.SizeAll : null;
        shopGrip.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
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

        AddLine(view.Mode == Mode.Arena ? "Según rivales de rondas jugadas" : "Según campeones y objetos rivales", 12, Muted);
        AddDamageDistribution(mix);
    }

    private void AddLobbyDamage(EnemyDamageMix? mix)
    {
        AddLine("Daño del lobby", 14, Brushes.WhiteSmoke, bold: true, top: 10);
        if (mix is not { } damage)
        {
            AddLine("Esperando identificar rivales.", 12, Muted, top: 2);
            return;
        }

        AddLine("Solo rivales identificados", 12, Muted);
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
            var reason = pending[0].Detail;
            AddPurchase(pending[0].Item, patch, 1, reason is ArenaBuilds.ChampionFit or ArenaBuilds.PublishedPick ? null : reason);
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
            {
                AddLine("Build completa", 17, PurchaseAccent, bold: true);
                AddLine("Conserva tus objetos por ahora.", 13, Muted, top: 3);
            }
        }

        AddLine($"Equipados · {purchased.Length} de 6", 14, Brushes.WhiteSmoke, bold: true, top: 10);
        AddItems(purchased.Select(step => step.Item).ToArray(), patch, ordered: false, equipped: true);
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

    private void AddPurchase(OpGgBuildItem item, string patch, int position, string? reason = null)
    {
        bool next = position == 1;
        double iconSize = next ? (Compact ? 32 : 38) : 23;
        var row = ItemRow(item, patch, iconSize, next ? null : position);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        if (next)
            text.Children.Add(new TextBlock { Text = "Siguiente compra", FontSize = 13,
                Foreground = PurchaseAccent, FontWeight = FontWeights.Bold });
        double nameSize = next ? (Compact ? 16 : 18) : 13;
        text.Children.Add(new TextBlock { Text = item.Name, FontSize = nameSize, Foreground = Brushes.WhiteSmoke,
            FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        if (reason is not null)
            text.Children.Add(new TextBlock { Text = reason, FontSize = 12.5, Foreground = Muted,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
        row.Children.Add(text);

        if (next)
            AddPurchaseCard(row);
        else
            AddItemCard(row);
    }

    private void AddPurchaseCard(UIElement child) =>
        content.Children.Add(new Border { Child = child, Background = PurchaseSurface, BorderBrush = PurchaseBorder,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(Compact ? 6 : 8) });

    private void AddItemCard(UIElement child, bool featured = false) =>
        content.Children.Add(new Border { Child = child, Background = featured ? PurchaseSurface : RowSurface,
            BorderBrush = featured ? PurchaseBorder : Divider, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3), Padding = new Thickness(5, 2, 5, 2), Margin = new Thickness(0, 3, 0, 0) });

    internal void RenderPrismatics(string champion, ArenaPrismaticTop top, string patch)
    {
        AddLine("Top 5 prismáticos", 17, Brushes.WhiteSmoke, bold: true);
        AddLine($"Afinidad con {champion}", 12, Muted);
        foreach (var (entry, index) in top.Entries.Select((entry, index) => (entry, index)))
        {
            bool offered = top.IsOffered(index);
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
                _ = SetIcon(icon, itemIcons, patch, entry.Id);
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

    private void AddItems(OpGgBuildItem[] items, string patch, bool ordered, bool equipped = false)
    {
        if (!equipped || Compact)
        {
            for (int i = 0; i < items.Length; i++)
            {
                var item = items[i];
                var row = ItemRow(item, patch, 22, ordered ? i + 1 : null);
                row.Children.Add(new TextBlock { Text = item.Name, FontSize = 12.5, Foreground = Brushes.WhiteSmoke,
                    TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
                AddItemCard(row, featured: ordered && i == 0);
            }
            return;
        }

        const int columns = 3;
        foreach (var itemsInRow in items.Chunk(columns))
        {
            var row = new UniformGrid { Columns = columns, Margin = new Thickness(0, 4, 0, 2) };
            foreach (var item in itemsInRow)
            {
                var tile = new StackPanel { Margin = new Thickness(2, 0, 2, 0) };
                tile.Children.Add(ItemIcon(item, patch, 27));
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

    private DockPanel ItemRow(OpGgBuildItem item, string patch, double iconSize, int? position)
    {
        var row = new DockPanel();
        var icon = ItemIcon(item, patch, iconSize);
        icon.Margin = new Thickness(0, 0, 7, 0);
        row.Children.Add(icon);
        if (position is { } number)
        {
            var order = new TextBlock { Text = $"{number}.", FontSize = 12, Foreground = Muted,
                Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(order, Dock.Right);
            row.Children.Add(order);
        }
        return row;
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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
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
    private static readonly Brush Accent = OverlayPanelStyle.Accent;
    private static readonly Brush PreviousPatch = OverlayPanelStyle.BrightGold;
    private static readonly Brush PurchaseAccent = OverlayPanelStyle.Gold;
    private static readonly Brush Divider = OverlayPanelStyle.Divider;
    private static readonly Brush ItemSurface = OverlayPanelStyle.Inset;
    private static readonly Brush PhysicalDamage = Overlay.Paint("#F2AD6B");
    private static readonly Brush MagicDamage = Overlay.Paint("#B9A7FA");
    private static readonly Brush TrueDamage = Overlay.Paint("#EDE8DC");

    private readonly StackPanel content = new();
    private readonly TextBlock shopTitle = new() { FontSize = OverlayPanelStyle.TitleSize, FontFamily = OverlayPanelStyle.Heading,
        FontWeight = FontWeights.Bold, Foreground = OverlayPanelStyle.Ink, TextTrimming = TextTrimming.CharacterEllipsis };
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
        OverlayPanelStyle.ApplyFrame(this);
        Child = content;
        DragHandle = CreateShopHeader();
    }

    internal void RenderPreview(string patch)
    {
        content.Children.Clear();
        AddShopHeader(null);
        AddLine("Primer objeto", OverlayPanelStyle.SectionSize, PurchaseAccent, bold: true);
        AddPurchase(new(new(6653, "El Tormento de Liandry")), patch, best: true);
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
        AddShopRecommendations(view, view.ClientPatch ?? build?.ClientPatch ?? "");

        if (view.Plan is not null || view.EnemyDamage is not null || build is { Groups.Length: > 0 })
            AddEnemyDamage(view);
        if (view.Mode == Mode.Arena)
            AddLobbyDamage(view.LobbyDamage);
    }

    private void AddShopRecommendations(BuildDisplay view, string patch)
    {
        var build = view.Source?.Data;
        var steps = view.Plan?.Steps ?? [];
        var owned = steps.Where(step => step.Owned).ToArray();
        if (owned.Length >= 6)
        {
            if (view.Plan?.Swap is { } swap)
            {
                AddSwap(swap, patch);
            }
            else
                AddBuildComplete();
            return;
        }

        if (view.Plan?.IsLocalRoute == true)
            AddLine("Ruta local · Según tu partida", 12, Accent, bold: true);

        var pending = steps.Where(step => !step.Owned).ToArray();
        var core = Choices(OpGgBuildSection.Core);
        var alternatives = core.Select(choice => choice.FirstOrDefault(item => !Owned(item) && !OpGgBuildPage.IsBoot(item.Id)))
            .OfType<OpGgBuildItem>().Select(item => new BuildStep(item));
        var purchases = pending.Length > 0 ? pending.Where(step => !OpGgBuildPage.IsBoot(step.Item.Id))
            : alternatives.Concat(core.SelectMany(choice => choice).Select(item => new BuildStep(item)));
        var next = purchases.Where(step => !Owned(step.Item) && !OpGgBuildPage.IsBoot(step.Item.Id))
            .DistinctBy(step => ItemFamily(step.Item.Id)).Take(3).ToArray();
        var boots = owned.Any(step => OpGgBuildPage.IsBoot(step.Item.Id)) ? [] :
            pending.Where(step => OpGgBuildPage.IsBoot(step.Item.Id))
                .Concat(Choices(OpGgBuildSection.Boots).SelectMany(choice => choice).Select(item => new BuildStep(item)))
                .DistinctBy(step => ItemFamily(step.Item.Id)).Take(2).ToArray();
        var starters = view.HideStartingItems ? [] : Choices(OpGgBuildSection.Start).FirstOrDefault() ?? [];
        var prismatics = Choices(OpGgBuildSection.Prismatic).SelectMany(choice => choice)
            .Where(item => !Owned(item)).DistinctBy(item => item.Id).Take(2).ToArray();

        if (starters.Length > 0)
            AddSection("Objetos iniciales", starters.Select(item => new BuildStep(item)));
        if (next.Length > 0)
            AddSection(PurchaseStage(owned.Count(step => !OpGgBuildPage.IsBoot(step.Item.Id))), next, highlight: true);
        if (boots.Length > 0)
            AddSection("Botas", boots, highlight: next.Length == 0);
        if (prismatics.Length > 0)
            AddSection("Objetos prismáticos", prismatics.Select(item => new BuildStep(item)));
        if (next.Length == 0 && boots.Length == 0 && starters.Length == 0 && prismatics.Length == 0)
        {
            var status = build is null ? view.Source?.Status ?? "No hay una build disponible."
                : "OP.GG no tiene recomendaciones de objetos disponibles.";
            AddLine(status, 14, OverlayPanelStyle.Ink);
        }
        if (build?.IsPrevious == true)
            AddLine($"Build de OP.GG · Parche anterior {LeaguePatch.Public(build.SourcePatch)}", 11, PreviousPatch, top: 6);
        OpGgBuildItem[][] Choices(OpGgBuildSection section) => build?.Groups.FirstOrDefault(group => group.Section == section)?.Choices ?? [];
        bool Owned(OpGgBuildItem item) => owned.Any(step => ItemFamily(step.Item.Id) == ItemFamily(item.Id));
        void AddSection(string title, IEnumerable<BuildStep> choices, bool highlight = false)
        {
            AddLine(title, OverlayPanelStyle.SectionSize, PurchaseAccent, bold: true, top: content.Children.Count == 1 ? 0 : 8);
            foreach (var (step, index) in choices.Select((step, index) => (step, index)))
                AddPurchase(step, patch, best: highlight && index == 0);
        }
    }

    private static int ItemFamily(int id) => id < 220000 ? MayhemBuilds.BaseId(id) : id;

    private static string PurchaseStage(int count) => count switch
    {
        0 => "Primer objeto", 1 => "Segundo objeto", 2 => "Tercer objeto", 3 => "Cuarto objeto",
        4 => "Quinto objeto", _ => "Sexto objeto"
    };

    private Grid CreateShopHeader()
    {
        var header = new Grid { Background = OverlayPanelStyle.HeaderSurface, MinHeight = 28, Margin = new Thickness(0, 0, 0, 7) };
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
        AddLine("Daño rival estimado", 14, OverlayPanelStyle.Ink, bold: true);
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
        AddLine("Daño del lobby", 14, OverlayPanelStyle.Ink, bold: true, top: 10);
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
        labels.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        labels.ColumnDefinitions.Add(new ColumnDefinition());
        labels.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var bar = new Grid { Height = 6, Margin = new Thickness(0, 4, 0, 0) };
        (int Percent, string Name, Brush Color)[] types =
            [(mix.PhysicalPercent, "físico", PhysicalDamage), (mix.MagicPercent, "mágico", MagicDamage), (mix.TruePercent, "verdadero", TrueDamage)];
        for (int i = 0; i < types.Length; i++)
        {
            var (percent, name, color) = types[i];
            var label = new TextBlock
            {
                Text = $"{percent}% {name}", Foreground = color, FontSize = Compact ? OverlayPanelStyle.DetailSize : OverlayPanelStyle.BodySize,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = i == 1 ? HorizontalAlignment.Center : HorizontalAlignment.Stretch
            };
            Grid.SetColumn(label, i);
            labels.Children.Add(label);
        }
        distribution.Children.Add(labels);

        var segments = types.Where(type => type.Percent > 0).ToArray();
        for (int i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(segment.Percent, GridUnitType.Star) });
            double left = i == 0 ? 4 : 0, right = i == segments.Length - 1 ? 4 : 0;
            var fill = new Border { Background = segment.Color, CornerRadius = new CornerRadius(left, right, right, left) };
            Grid.SetColumn(fill, i);
            bar.Children.Add(fill);
        }
        distribution.Children.Add(bar);
        content.Children.Add(distribution);
    }

    private void AddBuildComplete()
    {
        var complete = new StackPanel { Margin = new Thickness(0, 8, 0, 8) };
        complete.Children.Add(new Path
        {
            Data = Geometry.Parse("M 1,6 L 5,10 13,2"), Stroke = PurchaseAccent, StrokeThickness = 1.6,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        complete.Children.Add(new TextBlock
        {
            Text = "Build completa", FontSize = 15, Foreground = PurchaseAccent, FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 7, 0, 0)
        });
        content.Children.Add(complete);
    }

    private void AddSwap(BuildSwap swap, string patch)
    {
        var card = new StackPanel();
        card.Children.Add(new TextBlock { Text = "Cambio sugerido", FontSize = OverlayPanelStyle.BodySize,
            Foreground = PurchaseAccent, FontWeight = FontWeights.Bold });
        card.Children.Add(SwapItem("Sale", swap.Outgoing));
        card.Children.Add(SwapItem("Entra", swap.Incoming));
        card.Children.Add(new TextBlock { Text = swap.Reason, FontSize = OverlayPanelStyle.BodySize, Foreground = Muted,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
        content.Children.Add(new Border
        {
            Child = card, Background = OverlayPanelStyle.SelectionSurface, BorderBrush = OverlayPanelStyle.SelectionBorder,
            BorderThickness = OverlayPanelStyle.FrameThickness, Padding = new Thickness(Compact ? 6 : 8), SnapsToDevicePixels = true
        });

        DockPanel SwapItem(string label, OpGgBuildItem item)
        {
            var row = new DockPanel { Margin = new Thickness(0, 5, 0, 0) };
            var icon = ItemIcon(item, patch, Compact ? 27 : 31);
            icon.Margin = new Thickness(0, 0, 8, 0);
            row.Children.Add(icon);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = label, FontSize = OverlayPanelStyle.DetailSize, Foreground = Muted });
            text.Children.Add(new TextBlock { Text = item.Name, FontSize = OverlayPanelStyle.BodySize,
                Foreground = OverlayPanelStyle.Ink, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            row.Children.Add(text);
            return row;
        }
    }

    private void AddPurchase(BuildStep step, string patch, bool best)
    {
        var item = step.Item;
        var row = new DockPanel();
        var icon = ItemIcon(item, patch, Compact ? 25 : 29);
        icon.Margin = new Thickness(0, 0, 8, 0);
        row.Children.Add(icon);
        if (best)
        {
            var mark = new TextBlock
            {
                Text = "El mejor", Foreground = PurchaseAccent, FontSize = 10, FontWeight = FontWeights.Bold,
                Margin = new Thickness(8, 1, 0, 0), VerticalAlignment = VerticalAlignment.Top
            };
            DockPanel.SetDock(mark, Dock.Right);
            row.Children.Add(mark);
        }
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = item.Name, FontSize = OverlayPanelStyle.BodySize, Foreground = OverlayPanelStyle.Ink,
            FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        if (step.Detail is { } reason && reason is not (ArenaBuilds.ChampionFit or ArenaBuilds.PublishedPick or MayhemBuilds.PublishedCore or "Botas de OP.GG"))
            text.Children.Add(new TextBlock { Text = reason, FontSize = OverlayPanelStyle.DetailSize, Foreground = Muted,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        row.Children.Add(text);
        content.Children.Add(new Border
        {
            Child = row,
            Background = best ? OverlayPanelStyle.SelectionSurface : null,
            BorderBrush = best ? OverlayPanelStyle.SelectionBorder : null,
            BorderThickness = best ? OverlayPanelStyle.FrameThickness : new Thickness(0),
            Padding = best ? new Thickness(6, 4, 6, 4) : new Thickness(7, 5, 7, 5),
            Margin = new Thickness(0, 4, 0, 0), SnapsToDevicePixels = true
        });
    }

    internal void RenderPrismatics(string champion, ArenaPrismaticTop top, string patch)
    {
        content.Children.Add(new TextBlock
        {
            Text = "Top 5 prismáticos", FontFamily = OverlayPanelStyle.Heading, FontSize = OverlayPanelStyle.TitleSize,
            FontWeight = FontWeights.Bold, Foreground = OverlayPanelStyle.Ink
        });
        AddLine($"Afinidad con {champion}", 12, Muted);
        foreach (var (entry, index) in top.Entries.Select((entry, index) => (entry, index)))
        {
            bool offered = top.IsOffered(index);
            var row = new DockPanel { Margin = new Thickness(0, 9, 0, 0) };
            row.Children.Add(new TextBlock
            {
                Text = (index + 1).ToString(), Width = 20, FontSize = OverlayPanelStyle.BodySize,
                FontWeight = FontWeights.SemiBold, Foreground = Accent
            });
            var icon = new Image { Width = 26, Height = 26, Margin = new Thickness(0, 1, 8, 0),
                VerticalAlignment = VerticalAlignment.Top };
            row.Children.Add(icon);
            DockPanel.SetDock(icon, Dock.Left);
            if (itemIcons is not null)
                _ = SetIcon(icon, itemIcons, patch, entry.Id);
            var tier = TierHexagon.Create(entry.Rating.TierText, 26, 29);
            tier.Margin = new Thickness(6, 0, 0, 0);
            tier.VerticalAlignment = VerticalAlignment.Top;
            DockPanel.SetDock(tier, Dock.Right);
            row.Children.Add(tier);
            var text = new StackPanel();
            text.Children.Add(new TextBlock
            {
                Text = entry.Rating.Name,
                FontSize = OverlayPanelStyle.BodySize, FontWeight = FontWeights.SemiBold, Foreground = OverlayPanelStyle.Ink,
                TextWrapping = TextWrapping.Wrap
            });
            text.Children.Add(new TextBlock
            {
                Text = entry.Rating.Reason, FontSize = OverlayPanelStyle.DetailSize, Foreground = Muted, TextWrapping = TextWrapping.Wrap
            });
            if (offered)
                text.Children.Add(new TextBlock { Text = "En oferta", FontSize = OverlayPanelStyle.DetailSize, Foreground = Accent });
            row.Children.Add(text);
            content.Children.Add(row);
        }
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

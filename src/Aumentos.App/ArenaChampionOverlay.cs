using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;

namespace Aumentos;

internal sealed record ArenaChampionRow(string Champion, ArenaChampionTier? Rating);

internal sealed record ArenaChampionDisplay(Int32Rect Bounds, nint Target, ArenaChampionTierView Source,
    ArenaChampionRow[] Rows, ArenaChampionRow? Selection, ArenaCompositionPick[] Composition, ArenaCompositionGrade? Grade)
{
    private const int RankingLimit = 30;

    internal static ArenaChampionDisplay Create(Int32Rect bounds, nint target, Catalog catalog,
        ArenaChampionTierView source, string? championKey, ArenaCompositionPick[]? composition = null,
        ArenaCompositionGrade? grade = null)
    {
        const string tiers = "SABCD";
        var rows = source.Data?.Champions.Values
            .Select(rating => new ArenaChampionRow(Name(rating.Champion), rating))
            .OrderBy(row => row.Rating is { Tier.Length: 1 } rating && tiers.Contains(rating.Tier[0])
                ? tiers.IndexOf(rating.Tier[0]) : tiers.Length)
            .ThenByDescending(row => row.Rating?.TopThreePercent)
            .ThenBy(row => row.Champion, StringComparer.OrdinalIgnoreCase)
            .Take(RankingLimit).ToArray() ?? [];
        var selection = championKey is null ? null :
            new ArenaChampionRow(Name(championKey), source.Data?.Champions.GetValueOrDefault(championKey));
        return new(bounds, target, source, rows, selection, composition ?? [], grade);

        string Name(string key) => catalog.Champions.FirstOrDefault(champion =>
            string.Equals(champion.Key, key, StringComparison.OrdinalIgnoreCase))?.Name ?? key;
    }
}

internal sealed class ArenaChampionOverlay() : ChampionRankingOverlay(Mode.Arena)
{
    internal void Render(ArenaChampionDisplay? view) => RenderRanking(view is null ? null : new(
        view.Bounds, view.Target, view.Source.Data is { } data ?
            $"{ArenaChampionTierSource.Attribution} · {(data.IsPrevious ? "Parche anterior" : "Parche")} {LeaguePatch.Public(data.SourcePatch)}"
            : view.Source.Status,
        view.Source.Data is not null, view.Source.Data?.IsPrevious == true,
        view.Rows.Select(Normalize).ToArray(), view.Selection is { } selection ? Normalize(selection) : null,
        view.Composition.Select(pick => new ChampionRankingPick(pick.Champion, pick.Tier, pick.Reason)).ToArray(),
        view.Grade is { } grade ? new(grade.Tier, grade.Reason, grade.Members, grade.TeamSize,
            grade.Observed is { } observed ? $"GGEZ.GG · {LeaguePatch.Public(observed.Patch)}" : null) : null));

    private static ChampionRankingRow Normalize(ArenaChampionRow row) =>
        new(row.Champion, row.Rating?.Tier, TopThreePercent: row.Rating?.TopThreePercent);
}

internal sealed record ChampionRankingRow(string Champion, string? Tier, int? Position = null, decimal? TopThreePercent = null);
internal sealed record ChampionRankingPick(string Champion, string? Tier, string Reason, string? Tradeoff = null);
internal sealed record ChampionRankingGrade(string Tier, string Reason, int Members, int TeamSize, string? Source = null);
internal sealed record ChampionRankingDisplay(Int32Rect Bounds, nint Target, string Caption, bool HasRatings, bool PreviousPatch,
    ChampionRankingRow[] Rows, ChampionRankingRow? Selection, ChampionRankingPick[] Composition, ChampionRankingGrade? Grade,
    string? EmptyMessage = null);

internal abstract class ChampionRankingOverlay : Window
{
    private const double ArenaRankingWidth = 298;
    private const double CompactArenaWidth = 244;
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-MX");
    private static readonly Brush ArenaGold = Overlay.Paint("#D8BE85");
    private static readonly Brush ArenaRowTint = Overlay.Paint("#111C22");
    private static readonly Brush ArenaSelectionTint = Overlay.Paint("#30291D");
    private static readonly Brush ArenaBadgeBackground = Overlay.Paint("#1A252B");
    private const double MayhemTierColumn = 32;
    private const double MayhemInset = 11;
    private const double MayhemChampionTierSize = 15;
    private static readonly Brush MayhemTierTint = Overlay.Paint("#14212A");
    private readonly Brush primaryText;
    private readonly Brush secondaryText;
    private readonly Brush divider;
    private readonly StackPanel rows = new();
    private readonly Border panel;
    private readonly bool mayhem;
    private ChampionRankingDisplay? displayed;
    private bool displayedCompact;
    private (double Width, double Height) displayedSpace;
    private nint handle;
    private Int32Rect placement;

    protected ChampionRankingOverlay(Mode mode)
    {
        mayhem = mode == Mode.Caos;
        primaryText = mayhem ? MayhemTierStyle.Ink : OverlayPanelStyle.Ink;
        secondaryText = mayhem ? MayhemTierStyle.Muted : Overlay.Paint("#BBAF94");
        divider = mayhem ? MayhemTierStyle.Divider : Overlay.Paint("#594A33");
        Title = mayhem ? "Aumentos LoL · Selección de campeón" : "Aumentos LoL · Tier de campeones";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        ShowActivated = false;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        Background = Brushes.Transparent;
        Focusable = false;
        IsHitTestVisible = false;
        panel = mayhem ? MayhemTierStyle.Panel(rows, new Thickness(0)) : new Border
        {
            Background = Overlay.Paint("#FA0B141A"), BorderBrush = Overlay.Paint("#806B46"),
            BorderThickness = new Thickness(1, 2, 1, 1),
            Child = rows
        };
        TextOptions.SetTextFormattingMode(panel, TextFormattingMode.Display);
        Content = new Viewbox
        {
            Child = panel, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly,
            SnapsToDevicePixels = true
        };
        SourceInitialized += (_, _) =>
        {
            handle = new WindowInteropHelper(this).Handle;
            const int extendedStyle = -20;
            Native.SetWindowLongW(handle, extendedStyle, Native.GetWindowLongW(handle, extendedStyle) | 0x080000A0);
        };
    }

    protected void RenderRanking(ChampionRankingDisplay? view)
    {
        if (view is null)
        {
            if (IsVisible) Hide();
            displayed = null;
            return;
        }
        var bounds = view.Bounds;
        var dpi = Math.Max(96u, Native.GetDpiForWindow(view.Target)) / 96.0;
        bool compact = mayhem || bounds.Width / dpi < 1600 || bounds.Height / dpi < 680;
        var margin = Math.Min((int)Math.Round(14 * dpi), Math.Min(bounds.Width, bounds.Height) / 4);
        // In Mayhem, reserve the central roster and the bottom-right spell advice.
        // In Arena, leave space for the client's window controls and footer.
        int top = mayhem ? Math.Max(margin, (int)Math.Round(bounds.Height * .075))
            : Math.Min((int)Math.Round((compact ? 30 : 36) * dpi), bounds.Height / 4);
        int bottom = mayhem ? (int)(bounds.Height * (bounds.Height / dpi >= 900 ? .73 : .64))
            : bounds.Height - margin - Math.Min((int)Math.Round((compact ? 12 : 18) * dpi), bounds.Height / 4);
        int availableWidth = mayhem ? MayhemSidePanel.AvailableWidth(bounds.Width, margin) : Math.Max(1, bounds.Width - 2 * margin);
        int availableHeight = Math.Max(1, bottom - top);
        var space = (Width: availableWidth / dpi, Height: availableHeight / dpi);
        if (displayed is null || displayedCompact != compact || displayedSpace != space ||
            displayed.Caption != view.Caption || displayed.HasRatings != view.HasRatings || displayed.PreviousPatch != view.PreviousPatch ||
            displayed.EmptyMessage != view.EmptyMessage ||
            displayed.Selection != view.Selection || displayed.Grade != view.Grade || !displayed.Rows.SequenceEqual(view.Rows) ||
            !displayed.Composition.SequenceEqual(view.Composition))
            RenderRows(view, compact, space);
        displayed = view;
        displayedCompact = compact;
        displayedSpace = space;

        panel.Measure(new Size(panel.Width, double.PositiveInfinity));
        var canvas = panel.DesiredSize;
        var scale = Math.Min(dpi, Math.Min(availableWidth / canvas.Width, availableHeight / canvas.Height));
        int width = Math.Max(1, (int)Math.Floor(canvas.Width * scale));
        int height = Math.Max(1, (int)Math.Floor(canvas.Height * scale));
        var position = new Int32Rect(bounds.X + bounds.Width - margin - width, bounds.Y + top, width, height);
        if (IsVisible && Native.IsWindowVisible(handle) && position == placement)
            return;
        Width = width / dpi;
        Height = height / dpi;
        if (!IsVisible) Show();
        if (Native.SetWindowPos(handle, -1, position.X, position.Y, position.Width, position.Height, 0x0040 | 0x0010))
            placement = position;
    }

    private void RenderRows(ChampionRankingDisplay view, bool compact, (double Width, double Height) space)
    {
        rows.Children.Clear();
        if (mayhem)
        {
            panel.Width = MayhemSidePanel.PreferredWidth(space.Width);
            RenderMayhemRows(view);
            return;
        }

        panel.Width = compact ? CompactArenaWidth : ArenaRankingWidth;
        panel.Padding = compact ? new Thickness(10, 7, 10, 8) : new Thickness(13, 10, 13, 11);
        if (view.Selection is not null)
        {
            rows.Children.Add(SelectionSummary(view.Selection, view.HasRatings, view.Grade, compact));
            AddDivider(compact);
        }

        if (view.Composition.Length > 0)
        {
            AddHeading("Para tu equipo", 16, serif: false);
            foreach (var pick in view.Composition)
                rows.Children.Add(TeamPick(pick, compact));
            AddDivider(compact);
        }

        AddHeading(view.Rows.Length > 0 ? $"Arena · Top {view.Rows.Length} general" : "Arena · Ranking general",
            compact ? 18 : 21);
        AddLine(view.Caption, !view.HasRatings || !compact ? 13 : 12, view.PreviousPatch ? TierColor("S") : secondaryText);
        if (view.Rows.Length > 0)
        {
            rows.Children.Add(RankingTable(view.Rows, view.Selection, compact, space.Height));
            panel.InvalidateMeasure();
        }
    }

    private void RenderMayhemRows(ChampionRankingDisplay view)
    {
        bool balanced = view.Grade is { Tier: "S" } grade && grade.Members == grade.TeamSize;
        if (view.Grade is not null || view.Selection is not null)
            rows.Children.Add(MayhemEvaluation(view.Grade, balanced));

        var sections = new StackPanel { Margin = new Thickness(MayhemInset, 8, MayhemInset, 9) };
        rows.Children.Add(sections);
        if (view.Selection is { } selection)
        {
            var identity = new StackPanel();
            identity.Children.Add(MayhemHeading("Tu campeón", MayhemTierStyle.Gold));
            var name = Label(selection.Champion, MayhemTierStyle.PanelBodySize, primaryText, bold: true);
            name.TextWrapping = TextWrapping.Wrap;
            identity.Children.Add(name);
            sections.Children.Add(MayhemTierRow(identity,
                MayhemTierStyle.ChampionBadge(selection.Tier, selection.Champion, 28, 28, MayhemChampionTierSize)));
        }

        if (!balanced && view.Composition.Length > 0)
        {
            BeginMayhemSection(sections);
            sections.Children.Add(MayhemHeading("Para tu equipo", MayhemTierStyle.Gold));
            var firstTradeoff = view.Composition[0].Tradeoff;
            var sharedTradeoff = view.Composition.Length > 1 && view.Composition.All(pick => pick.Tradeoff == firstTradeoff)
                ? firstTradeoff : null;
            if (sharedTradeoff is not null)
                sections.Children.Add(MayhemNote(sharedTradeoff, MayhemTierStyle.Gold));
            foreach (var pick in view.Composition)
                sections.Children.Add(MayhemPick(pick, showTradeoff: sharedTradeoff is null));
        }

        BeginMayhemSection(sections);
        var heading = MayhemHeading("Campeones disponibles", MayhemTierStyle.Gold);
        var hasOptions = view.Rows.Length > 0;
        var tierHeader = hasOptions ? Label("Tier", MayhemTierStyle.PanelDetailSize, secondaryText) : null;
        if (tierHeader is not null)
            tierHeader.TextAlignment = TextAlignment.Center;
        sections.Children.Add(MayhemTierRow(heading, tierHeader));
        if (!hasOptions && view.EmptyMessage is { } message)
            sections.Children.Add(MayhemNote(message, secondaryText));
        else if (view.Caption.Length > 0)
            sections.Children.Add(MayhemNote(view.Caption, view.PreviousPatch ? MayhemTierStyle.Gold : secondaryText));

        foreach (var option in view.Rows)
        {
            var name = Label(option.Champion, MayhemTierStyle.PanelBodySize, primaryText);
            var row = MayhemTierRow(name, MayhemTierLabel(option.Tier));
            row.MinHeight = 19;
            sections.Children.Add(row);
        }
    }

    private Border MayhemEvaluation(ChampionRankingGrade? grade, bool balanced)
    {
        var text = new StackPanel();
        text.Children.Add(MayhemHeading(balanced ? "Equipo equilibrado" : grade?.Reason ?? "Esperando un aliado",
            balanced ? MayhemTierStyle.Gold : grade is null ? secondaryText : primaryText, size: MayhemTierStyle.PanelTitleSize));
        if (grade is { } partial && partial.Members < partial.TeamSize)
            text.Children.Add(MayhemNote($"{partial.Members}/{partial.TeamSize} campeones", secondaryText));

        return new Border
        {
            Background = MayhemTierStyle.HeaderSurface, BorderBrush = divider, BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(MayhemInset, 8, MayhemInset, 8),
            Child = MayhemTierRow(text, grade is null ? null : MayhemTierStyle.Badge(grade.Tier, MayhemTierColumn, 32, 22))
        };
    }

    private Grid MayhemPick(ChampionRankingPick pick, bool showTradeoff)
    {
        var tier = new Border
        {
            Width = 24, Height = 19, HorizontalAlignment = HorizontalAlignment.Center,
            Background = MayhemTierTint, Child = MayhemTierLabel(pick.Tier)
        };
        var grid = MayhemTierRow(Label(pick.Champion, MayhemTierStyle.PanelBodySize, primaryText, bold: true), tier);
        grid.Margin = new Thickness(0, 4, 0, 0);
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var details = new StackPanel();
        var reason = MayhemNote(pick.Reason, secondaryText);
        reason.Margin = new Thickness(0);
        details.Children.Add(reason);
        if (showTradeoff && pick.Tradeoff is { } tradeoff)
            details.Children.Add(MayhemNote(tradeoff, MayhemTierStyle.Gold));
        Grid.SetRow(details, 1);
        Grid.SetColumnSpan(details, 2);
        grid.Children.Add(details);
        return grid;
    }

    private static Grid MayhemTierRow(FrameworkElement text, FrameworkElement? tier)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        text.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(text);
        if (tier is not null)
        {
            text.Margin = new Thickness(0, 0, 6, 0);
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(MayhemTierColumn) });
            tier.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(tier, 1);
            grid.Children.Add(tier);
        }
        return grid;
    }

    private static TextBlock MayhemTierLabel(string? tier) => new()
    {
        Text = tier ?? "—", FontSize = MayhemChampionTierSize, Foreground = MayhemTierStyle.Tier(tier), FontFamily = MayhemTierStyle.Heading,
        FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center
    };

    private static TextBlock MayhemHeading(string value, Brush color, double size = MayhemTierStyle.PanelSectionSize) => new()
    {
        Text = value, FontSize = size, Foreground = color, FontFamily = MayhemTierStyle.Heading,
        FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap
    };

    private static TextBlock MayhemNote(string value, Brush color) => new()
    {
        Text = value, FontSize = MayhemTierStyle.PanelDetailSize, Foreground = color,
        TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0)
    };

    private void BeginMayhemSection(StackPanel sections)
    {
        if (sections.Children.Count > 0)
            sections.Children.Add(new Border { Height = 1, Background = divider, Margin = new Thickness(0, 8, 0, 7) });
    }

    private StackPanel SelectionSummary(ChampionRankingRow selection, bool hasSource, ChampionRankingGrade? grade,
        bool compact)
    {
        var summary = new StackPanel();
        string championDetail = selection switch
        {
            { TopThreePercent: { } topThree } => $"{TopThree(topThree)} top 3",
            { Tier: not null } => "Tier de OP.GG",
            _ => hasSource ? "Sin tier publicado" : "Tier pendiente"
        };
        summary.Children.Add(SummaryBadge(selection.Champion, championDetail, selection.Tier, compact));

        var composition = new StackPanel { Margin = new Thickness(0, compact ? 4 : 6, 0, 0) };
        var detail = grade switch
        {
            null => "Esperando un aliado",
            { Source: { } source } => source,
            _ => $"Tier local · {grade.Members}/{grade.TeamSize}"
        };
        if (!compact && grade is { Source: null })
            detail += " campeones";
        string compositionName = compact ? "Composición" : "Composición Arena";
        composition.Children.Add(SummaryBadge(compositionName, detail, grade?.Tier, compact));
        if (grade is not null)
        {
            var reason = Label(grade.Reason, compact ? 12 : 13, secondaryText);
            reason.Margin = new Thickness(0, 3, 0, 0);
            reason.TextWrapping = TextWrapping.Wrap;
            composition.Children.Add(reason);
        }
        summary.Children.Add(composition);
        return summary;
    }

    private DockPanel SummaryBadge(string name, string detail, string? tier, bool compact)
    {
        var group = new DockPanel { VerticalAlignment = VerticalAlignment.Top };
        if (tier is not null)
        {
            var badge = ArenaSummaryBadge(tier, compact);
            badge.Margin = new Thickness(6, 0, 0, 0);
            DockPanel.SetDock(badge, Dock.Right);
            group.Children.Add(badge);
        }

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Label(name, compact ? 14 : 17, primaryText, bold: true));
        text.Children.Add(Label(detail, compact ? 12 : 13, secondaryText));
        group.Children.Add(text);
        return group;
    }

    private Border ArenaSummaryBadge(string tier, bool compact)
    {
        var grade = Label(tier, compact ? 22 : 27, TierColor(tier), bold: true);
        grade.TextAlignment = TextAlignment.Center;
        grade.VerticalAlignment = VerticalAlignment.Center;
        return new Border
        {
            Width = compact ? 30 : 39, Height = compact ? 34 : 40, Background = ArenaBadgeBackground,
            BorderBrush = divider, BorderThickness = new Thickness(1), Child = grade
        };
    }

    private StackPanel TeamPick(ChampionRankingPick pick, bool compact)
    {
        var text = new StackPanel { Margin = new Thickness(0, compact ? 1 : 3, 0, 0) };
        var header = new DockPanel();
        if (pick.Tier is { } tier)
        {
            var grade = Label(tier, 14, TierColor(tier), bold: true);
            grade.Margin = new Thickness(6, 0, 0, 0);
            DockPanel.SetDock(grade, Dock.Right);
            header.Children.Add(grade);
        }
        header.Children.Add(Label(pick.Champion, 14, primaryText, bold: true));
        text.Children.Add(header);
        var reason = Label(pick.Reason, 12, secondaryText);
        reason.TextWrapping = TextWrapping.Wrap;
        text.Children.Add(reason);
        return text;
    }

    private Grid RankingTable(ChampionRankingRow[] rows, ChampionRankingRow? selection, bool compact, double availableHeight)
    {
        var grid = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        int headerHeight = compact ? 19 : 24;
        panel.Measure(new Size(panel.Width, double.PositiveInfinity));
        var rowSpace = availableHeight - panel.DesiredSize.Height - headerHeight - grid.Margin.Top;
        int rowHeight = Math.Clamp((int)Math.Floor(rowSpace / rows.Length), compact ? 17 : 20, compact ? 20 : 24);

        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(compact ? 24 : 31) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(compact ? 21 : 26) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(compact ? 52 : 60) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(headerHeight) });

        int headerSize = compact ? 12 : 13;
        AddCell(0, 1, "Campeón", headerSize, ArenaGold, bold: true);
        AddCell(0, 2, "Tier", headerSize, ArenaGold, TextAlignment.Center, bold: true);
        AddCell(0, 3, "Top 3", headerSize, ArenaGold, TextAlignment.Right, bold: true);
        var rule = new Border { Background = divider, Height = 1, VerticalAlignment = VerticalAlignment.Bottom };
        Grid.SetColumnSpan(rule, grid.ColumnDefinitions.Count);
        grid.Children.Add(rule);

        for (int i = 0; i < rows.Length; i++)
        {
            var row = rows[i];
            int line = i + 1;
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(rowHeight) });
            bool selected = row == selection;
            if (selected || i % 2 != 0)
            {
                var background = new Border { Background = selected ? ArenaSelectionTint : ArenaRowTint };
                Grid.SetRow(background, line);
                Grid.SetColumnSpan(background, grid.ColumnDefinitions.Count);
                grid.Children.Add(background);
            }
            AddCell(line, 0, line.ToString(Spanish), compact ? 12 : 13, selected ? ArenaGold : secondaryText, TextAlignment.Right);
            AddCell(line, 1, row.Champion, compact ? 15 : 17, selected ? ArenaGold : primaryText, bold: true);
            int valueSize = compact ? 14 : 16;
            if (row.Tier is { } tier)
                AddCell(line, 2, tier, valueSize, TierColor(tier), TextAlignment.Center, bold: true);
            if (row.TopThreePercent is { } topThree)
                AddCell(line, 3, TopThree(topThree), valueSize, primaryText, TextAlignment.Right);
        }
        return grid;

        void AddCell(int row, int column, string value, double size, Brush color,
            TextAlignment alignment = TextAlignment.Left, bool bold = false)
        {
            var text = Label(value, size, color, bold);
            text.TextAlignment = alignment;
            text.VerticalAlignment = VerticalAlignment.Center;
            if (column == 0)
                text.Margin = new Thickness(0, 0, 9, 0);
            if (column == 3)
                text.Margin = new Thickness(0, 0, 3, 0);
            Typography.SetNumeralAlignment(text, FontNumeralAlignment.Tabular);
            Grid.SetRow(text, row);
            Grid.SetColumn(text, column);
            grid.Children.Add(text);
        }
    }

    private static string TopThree(decimal percent) => percent.ToString("0.#", Spanish) + " %";

    private static Brush TierColor(string tier) => Overlay.Paint(tier switch
    {
        "S" => "#EDC675",
        "A" => "#9ED5BF",
        "B" => "#B7CCE3",
        _ => "#DDD0B8"
    });

    private void AddDivider(bool compact) => rows.Children.Add(new Border
    {
        Height = 1, Background = divider,
        Margin = compact ? new Thickness(0, 4, 0, 3) : new Thickness(0, 8, 0, 5)
    });

    private void AddHeading(string text, double size, bool serif = true)
    {
        var heading = AddLine(text, size, ArenaGold, bold: true);
        if (serif)
            heading.FontFamily = OverlayPanelStyle.Heading;
    }

    private TextBlock AddLine(string text, double size, Brush color, bool bold = false)
    {
        var line = Label(text, size, color, bold);
        line.TextWrapping = TextWrapping.Wrap;
        line.Margin = new Thickness(0, 2, 0, 0);
        rows.Children.Add(line);
        return line;
    }

    private static TextBlock Label(string text, double size, Brush color, bool bold = false) => new()
    {
        Text = text, FontSize = size, Foreground = color, TextTrimming = TextTrimming.CharacterEllipsis,
        FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal
    };
}

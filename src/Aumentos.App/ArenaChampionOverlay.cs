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
internal sealed record ArenaRosterMember(string Champion, string? Tier, bool? Confirmed, bool Own = false);

internal sealed record ArenaChampionDisplay(Int32Rect Bounds, nint Target, ArenaChampionTierView Source,
    ArenaChampionRow[] Rows, ArenaChampionRow? Selection, ArenaCompositionGroup[] Composition, ArenaCompositionGrade? Grade,
    ArenaRosterMember[] Roster)
{
    private const int RankingLimit = 30;

    internal static ArenaChampionDisplay Create(Int32Rect bounds, nint target, Catalog catalog,
        ArenaChampionTierView source, string? championKey, ArenaCompositionGroup[]? composition = null,
        ArenaCompositionGrade? grade = null, ArenaDraftTeam? team = null)
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
        var roster = new List<ArenaRosterMember>();
        if (selection is not null)
            roster.Add(new(selection.Champion, selection.Rating?.Tier, team?.OwnConfirmed, Own: true));
        if (team is not null)
        {
            foreach (var ally in team.Teammates.Where(key => !string.Equals(key, championKey, StringComparison.OrdinalIgnoreCase)))
                roster.Add(new(Name(ally), source.Data?.Champions.GetValueOrDefault(ally)?.Tier,
                    team.ConfirmedTeammates?.Contains(ally, StringComparer.OrdinalIgnoreCase)));
        }
        return new(bounds, target, source, rows, selection, composition ?? [], grade, roster.ToArray());

        string Name(string key) => catalog.Champions.FirstOrDefault(champion =>
            string.Equals(champion.Key, key, StringComparison.OrdinalIgnoreCase))?.Name ?? key;
    }
}

internal sealed class ArenaChampionOverlay(ChampionSelectPortraits? portraits = null) : ChampionRankingOverlay(Mode.Arena, portraits)
{
    internal void Render(ArenaChampionDisplay? view) => RenderRanking(view is null ? null : new(
        view.Bounds, view.Target, view.Source.Data is { } data ? data.IsPrevious ? "Parche anterior" : "" : view.Source.Status,
        view.Source.Data?.IsPrevious == true,
        view.Rows.Select(Normalize).ToArray(), view.Selection is { } selection ? Normalize(selection) : null,
        [], view.Grade is { } grade ? Normalize(grade) : null, ArenaGroups: view.Composition, Roster: view.Roster));

    private static ChampionRankingRow Normalize(ArenaChampionRow row) =>
        new(row.Champion, row.Rating?.Tier, TopThreePercent: row.Rating?.TopThreePercent);

    private static ChampionRankingGrade Normalize(ArenaCompositionGrade grade)
    {
        if (grade.Observed is not { } observed)
            return new(grade.Tier, grade.Reason, grade.Members, grade.TeamSize);
        var average = observed.AveragePlace.ToString("0.00", CultureInfo.GetCultureInfo("es-MX"));
        return new(grade.Tier, grade.Reason, grade.Members, grade.TeamSize,
            $"Puesto medio {average} · {observed.Games} partidas");
    }
}

internal sealed record ChampionRankingRow(string Champion, string? Tier, int? Position = null, decimal? TopThreePercent = null);
internal sealed record ChampionRankingPick(string Champion, string? Tier, string Reason, string? Tradeoff = null);
internal sealed record ChampionRankingGrade(string Tier, string Reason, int Members, int TeamSize, string? Detail = null);
internal sealed record ChampionRankingDisplay(Int32Rect Bounds, nint Target, string Caption, bool PreviousPatch,
    ChampionRankingRow[] Rows, ChampionRankingRow? Selection, ChampionRankingPick[] Composition, ChampionRankingGrade? Grade,
    string? EmptyMessage = null, ArenaCompositionGroup[]? ArenaGroups = null, ArenaRosterMember[]? Roster = null);

internal abstract class ChampionRankingOverlay : Window
{
    private const double ArenaRankingWidth = 298;
    private const double CompactArenaWidth = 244;
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-MX");
    private static readonly Brush RowTint = Overlay.Paint("#09141D");
    private static readonly Brush SelectionTint = Overlay.Paint("#30291D");
    private static readonly Brush SelectionFrame = Overlay.Paint("#785A28");
    private const double TierColumnWidth = 32;
    private const double Inset = 11;
    private readonly StackPanel rows = new();
    private readonly Border panel;
    private readonly bool mayhem;
    private readonly ChampionSelectPortraits? portraits;
    private ChampionRankingDisplay? displayed;
    private bool displayedCompact;
    private double displayedWidth;
    private nint handle;
    private Int32Rect placement;

    protected ChampionRankingOverlay(Mode mode, ChampionSelectPortraits? portraits)
    {
        mayhem = mode == Mode.Caos;
        this.portraits = portraits;
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
        panel = MayhemTierStyle.Panel(rows, new Thickness(0));
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
        double widthLimit = availableWidth / dpi;
        if (displayed is null || displayedCompact != compact || displayedWidth != widthLimit ||
            displayed.Caption != view.Caption || displayed.PreviousPatch != view.PreviousPatch ||
            displayed.EmptyMessage != view.EmptyMessage || !(displayed.Roster ?? []).SequenceEqual(view.Roster ?? []) ||
            !SameGroups(displayed.ArenaGroups ?? [], view.ArenaGroups ?? []) ||
            displayed.Selection != view.Selection || displayed.Grade != view.Grade || !displayed.Rows.SequenceEqual(view.Rows) ||
            !displayed.Composition.SequenceEqual(view.Composition))
            RenderRows(view, compact, widthLimit);
        displayed = view;
        displayedCompact = compact;
        displayedWidth = widthLimit;

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

    private void RenderRows(ChampionRankingDisplay view, bool compact, double widthLimit)
    {
        rows.Children.Clear();
        panel.Width = mayhem ? MayhemSidePanel.PreferredWidth(widthLimit) : compact ? CompactArenaWidth : ArenaRankingWidth;
        bool balanced = mayhem && view.Grade is { Tier: "S" } grade && grade.Members == grade.TeamSize;
        if (view.Grade is not null || view.Selection is not null || view.Roster is { Length: > 0 })
            rows.Children.Add(TeamEvaluation(view, balanced, mayhem));

        var sections = new StackPanel { Margin = new Thickness(Inset, 8, Inset, 9) };
        rows.Children.Add(sections);

        if (view.ArenaGroups is { Length: > 0 } groups)
            AddArenaGroups(sections, groups);

        if (!balanced && view.Composition.Length > 0)
        {
            BeginSection(sections);
            sections.Children.Add(Heading("Para tu equipo", MayhemTierStyle.Gold));
            var firstTradeoff = view.Composition[0].Tradeoff;
            var sharedTradeoff = view.Composition.Length > 1 && view.Composition.All(pick => pick.Tradeoff == firstTradeoff)
                ? firstTradeoff : null;
            if (sharedTradeoff is not null)
                sections.Children.Add(Note(sharedTradeoff, MayhemTierStyle.Gold));
            foreach (var pick in view.Composition)
                sections.Children.Add(TeamPick(pick, showTradeoff: sharedTradeoff is null));
        }

        BeginSection(sections);
        if (mayhem)
            RenderAvailable(sections, view);
        else
            RenderArenaRanking(sections, view, compact);
    }

    private static bool SameGroups(ArenaCompositionGroup[] previous, ArenaCompositionGroup[] current) =>
        previous.Length == current.Length && previous.Zip(current).All(pair =>
            pair.First.Category == pair.Second.Category && pair.First.Options.SequenceEqual(pair.Second.Options));

    private void AddArenaGroups(StackPanel sections, ArenaCompositionGroup[] groups)
    {
        BeginSection(sections);
        sections.Children.Add(Heading("Para tu equipo", MayhemTierStyle.Gold));
        foreach (var group in groups)
        {
            var category = new StackPanel { Margin = new Thickness(0, 5, 0, 0) };
            category.Children.Add(Label(group.Category, MayhemTierStyle.PanelDetailSize, MayhemTierStyle.Gold, bold: true));
            foreach (var option in group.Options)
            {
                var row = TierRow(ChampionIdentity(option.Champion, 16), ChampionTier(option.Champion, option.Tier, compact: true));
                row.MinHeight = 18;
                category.Children.Add(row);
            }
            sections.Children.Add(category);
        }
    }

    private void RenderAvailable(StackPanel sections, ChampionRankingDisplay view)
    {
        var heading = Heading("Campeones disponibles", MayhemTierStyle.Gold);
        var hasRows = view.Rows.Length > 0;
        var tierHeader = hasRows ? Label("Tier", MayhemTierStyle.PanelDetailSize, MayhemTierStyle.Muted) : null;
        if (tierHeader is not null)
            tierHeader.TextAlignment = TextAlignment.Center;
        sections.Children.Add(TierRow(heading, tierHeader));
        if (view.Caption.Length > 0 && (hasRows || view.EmptyMessage is null))
            sections.Children.Add(SourceNote(view));

        for (int index = 0; index < view.Rows.Length; index++)
        {
            var option = view.Rows[index];
            bool selected = option == view.Selection;
            var row = TierRow(ChampionIdentity(option.Champion, 20, selected), ChampionTier(option.Champion, option.Tier));
            row.MinHeight = 26;
            if (selected || index % 2 != 0)
                AddRowBackground(row, 0, selected);
            sections.Children.Add(row);
        }
        if (view.EmptyMessage is { } message && !view.Rows.Any(row => row != view.Selection))
            sections.Children.Add(Note(message, MayhemTierStyle.Muted));
    }

    private void RenderArenaRanking(StackPanel sections, ChampionRankingDisplay view, bool compact)
    {
        bool rankingOnly = rows.Children.Count == 1 && sections.Children.Count == 0;
        string title = view.Rows.Length > 0 ? $"Top {view.Rows.Length} general" : "Ranking general";
        sections.Children.Add(Heading(title, rankingOnly ? MayhemTierStyle.Ink : MayhemTierStyle.Gold,
            rankingOnly ? MayhemTierStyle.PanelTitleSize : MayhemTierStyle.PanelSectionSize));
        if (view.Caption.Length > 0)
            sections.Children.Add(SourceNote(view));
        if (view.Rows.Length > 0)
            sections.Children.Add(RankingTable(view.Rows, view.Selection, compact));
    }

    private Border TeamEvaluation(ChampionRankingDisplay view, bool balanced, bool mayhem)
    {
        var grade = view.Grade;
        var waiting = view.Selection is null ? "Esperando tu elección" : "Esperando un aliado";
        var text = new StackPanel();
        text.Children.Add(Heading(balanced ? "Equipo equilibrado" : grade?.Reason ?? waiting,
            balanced ? MayhemTierStyle.Gold : grade is null ? MayhemTierStyle.Muted : MayhemTierStyle.Ink,
            size: MayhemTierStyle.PanelTitleSize));
        if (mayhem && grade is { } partial && partial.Members < partial.TeamSize)
            text.Children.Add(Note($"{partial.Members}/{partial.TeamSize} campeones", MayhemTierStyle.Muted));
        if (grade?.Detail is { } detail)
            text.Children.Add(Note(detail, MayhemTierStyle.Muted));
        var summary = TierRow(text, grade is null ? null : MayhemTierStyle.SelectionBadge(grade.Tier, 28, 31));
        FrameworkElement content = summary;
        if (view.Roster is { Length: > 0 } roster)
        {
            var stack = new StackPanel();
            stack.Children.Add(summary);
            stack.Children.Add(ArenaRoster(roster));
            content = stack;
        }

        return new Border
        {
            Background = MayhemTierStyle.HeaderSurface, BorderBrush = MayhemTierStyle.Divider,
            BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(Inset, 8, Inset, 8),
            Child = content
        };
    }

    private Grid ArenaRoster(ArenaRosterMember[] members)
    {
        var grid = new Grid { Margin = new Thickness(0, 5, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(TierColumnWidth) });
        grid.RowDefinitions.Add(new RowDefinition { MinHeight = 17 });
        AddCell(0, 0, Label("Campeón", MayhemTierStyle.PanelDetailSize, MayhemTierStyle.Muted));
        AddCell(0, 1, Label("Estado", MayhemTierStyle.PanelDetailSize, MayhemTierStyle.Muted));
        AddCell(0, 2, new TextBlock
        {
            Text = "Tier", FontSize = MayhemTierStyle.PanelDetailSize, Foreground = MayhemTierStyle.Muted,
            TextAlignment = TextAlignment.Center
        });
        for (int index = 0; index < members.Length; index++)
        {
            var member = members[index];
            int row = index + 1;
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto, MinHeight = 24 });
            if (member.Own)
                AddRowBackground(grid, row, selected: true);
            var champion = ChampionIdentity(member.Champion, 18, selected: member.Own, wrapName: true);
            champion.Margin = new Thickness(0, 0, 5, 0);
            AddCell(row, 0, champion);
            string status = member.Confirmed switch { true => "Confirmado", false => "Preselección", _ => "—" };
            AddCell(row, 1, Label(status, MayhemTierStyle.PanelDetailSize,
                member.Confirmed == true ? MayhemTierStyle.Gold : MayhemTierStyle.Muted));
            AddCell(row, 2, ChampionTier(member.Champion, member.Tier));
        }
        return grid;

        void AddCell(int row, int column, FrameworkElement label)
        {
            label.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRow(label, row);
            Grid.SetColumn(label, column);
            grid.Children.Add(label);
        }
    }

    private Grid TeamPick(ChampionRankingPick pick, bool showTradeoff)
    {
        var grid = TierRow(ChampionIdentity(pick.Champion, 20, bold: true), ChampionTier(pick.Champion, pick.Tier));
        grid.Margin = new Thickness(0, 4, 0, 0);
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var details = new StackPanel();
        var reason = Note(pick.Reason, MayhemTierStyle.Muted);
        reason.Margin = new Thickness(0);
        details.Children.Add(reason);
        if (showTradeoff && pick.Tradeoff is { } tradeoff)
            details.Children.Add(Note(tradeoff, MayhemTierStyle.Gold));
        Grid.SetRow(details, 1);
        Grid.SetColumnSpan(details, 2);
        grid.Children.Add(details);
        return grid;
    }

    private static Grid TierRow(FrameworkElement text, FrameworkElement? tier)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        text.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(text);
        if (tier is not null)
        {
            text.Margin = new Thickness(0, 0, 6, 0);
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(TierColumnWidth) });
            tier.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(tier, 1);
            grid.Children.Add(tier);
        }
        return grid;
    }

    private Grid ChampionIdentity(string champion, double portraitSize, bool selected = false, bool bold = false, bool wrapName = false)
    {
        var identity = new Grid();
        identity.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        identity.ColumnDefinitions.Add(new ColumnDefinition());
        var portrait = ChampionSelectStyle.Portrait(portraits, champion, portraitSize);
        portrait.Margin = new Thickness(5, 0, 6, 0);
        identity.Children.Add(portrait);
        var name = Label(champion, MayhemTierStyle.PanelBodySize,
            selected ? MayhemTierStyle.Gold : MayhemTierStyle.Ink, bold: selected || bold);
        name.VerticalAlignment = VerticalAlignment.Center;
        if (wrapName)
            name.TextWrapping = TextWrapping.Wrap;
        Grid.SetColumn(name, 1);
        identity.Children.Add(name);
        if (selected)
        {
            var marker = new Border
            {
                BorderBrush = SelectionFrame, BorderThickness = new Thickness(1), Margin = new Thickness(4, 0, 0, 0),
                Padding = new Thickness(3, 0, 3, 0), VerticalAlignment = VerticalAlignment.Center,
                Child = Label("TÚ", 9, MayhemTierStyle.Gold, bold: true)
            };
            identity.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(marker, 2);
            identity.Children.Add(marker);
        }
        return identity;
    }

    private Border ChampionTier(string champion, string? tier, bool compact = false) =>
        MayhemTierStyle.ChampionBadge(tier, champion, compact ? 17 : 21, compact ? 18 : 22,
            mayhem ? "OP.GG" : "GGEZ.GG");

    private static void AddRowBackground(Grid grid, int row, bool selected)
    {
        var background = new Border
        {
            Background = selected ? SelectionTint : RowTint,
            BorderBrush = selected ? SelectionFrame : null, BorderThickness = new Thickness(1)
        };
        Grid.SetRow(background, row);
        Grid.SetColumnSpan(background, grid.ColumnDefinitions.Count);
        grid.Children.Insert(0, background);
    }

    private static TextBlock Heading(string value, Brush color, double size = MayhemTierStyle.PanelSectionSize) => new()
    {
        Text = value, FontSize = size, Foreground = color, FontFamily = MayhemTierStyle.Heading,
        FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap
    };

    private static TextBlock Note(string value, Brush color) => new()
    {
        Text = value, FontSize = MayhemTierStyle.PanelDetailSize, Foreground = color,
        TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0)
    };

    private static TextBlock SourceNote(ChampionRankingDisplay view) =>
        Note(view.Caption, view.PreviousPatch ? MayhemTierStyle.Gold : MayhemTierStyle.Muted);

    private static void BeginSection(StackPanel sections)
    {
        if (sections.Children.Count > 0)
            sections.Children.Add(new Border
            {
                Height = 1, Background = MayhemTierStyle.Divider, Margin = new Thickness(0, 8, 0, 7)
            });
    }

    private Grid RankingTable(ChampionRankingRow[] rows, ChampionRankingRow? selection, bool compact)
    {
        var grid = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        const int headerHeight = 19;
        int rowHeight = compact ? 18 : 22;

        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(compact ? 25 : 30) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(TierColumnWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(compact ? 55 : 60) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(headerHeight) });

        AddCell(0, 1, "Campeón", MayhemTierStyle.PanelDetailSize, MayhemTierStyle.Muted);
        AddCell(0, 2, "Tier", MayhemTierStyle.PanelDetailSize, MayhemTierStyle.Muted, TextAlignment.Center);
        AddCell(0, 3, "Top 3", MayhemTierStyle.PanelDetailSize, MayhemTierStyle.Muted, TextAlignment.Right);
        var rule = new Border
        {
            Background = MayhemTierStyle.Divider, Height = 1, VerticalAlignment = VerticalAlignment.Bottom
        };
        Grid.SetColumnSpan(rule, grid.ColumnDefinitions.Count);
        grid.Children.Add(rule);

        for (int i = 0; i < rows.Length; i++)
        {
            var row = rows[i];
            int line = i + 1;
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(rowHeight) });
            bool selected = row == selection;
            if (selected || i % 2 != 0)
                AddRowBackground(grid, line, selected);
            AddCell(line, 0, line.ToString(Spanish), MayhemTierStyle.PanelDetailSize,
                selected ? MayhemTierStyle.Gold : MayhemTierStyle.Muted, TextAlignment.Right);
            AddCellElement(line, 1, ChampionIdentity(row.Champion, compact ? 15 : 18, selected));
            var tier = ChampionTier(row.Champion, row.Tier, compact);
            tier.VerticalAlignment = VerticalAlignment.Center;
            AddCellElement(line, 2, tier);
            if (row.TopThreePercent is { } topThree)
                AddCell(line, 3, TopThree(topThree), MayhemTierStyle.PanelBodySize, MayhemTierStyle.Ink, TextAlignment.Right);
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
            AddCellElement(row, column, text);
        }

        void AddCellElement(int row, int column, FrameworkElement element)
        {
            Grid.SetRow(element, row);
            Grid.SetColumn(element, column);
            grid.Children.Add(element);
        }
    }

    private static string TopThree(decimal percent) => percent.ToString("0.#", Spanish) + " %";

    private static TextBlock Label(string text, double size, Brush color, bool bold = false) => new()
    {
        Text = text, FontSize = size, Foreground = color, TextTrimming = TextTrimming.CharacterEllipsis,
        FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal
    };
}

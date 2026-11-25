using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Aumentos;

internal sealed record ArenaChampionRow(string Champion, ArenaChampionTier? Rating);
internal sealed record ArenaRosterMember(string? Champion, string? Tier, bool? Confirmed, bool Own = false);

internal sealed record ArenaChampionDisplay(Int32Rect Bounds, nint Target, ArenaChampionTierView Source,
    ArenaChampionRow[] Rows, ArenaChampionRow? Selection, ArenaComposition[] Composition, ArenaCompositionGrade? Grade,
    ArenaRosterMember[] Roster)
{
    private const int RankingLimit = 30;

    internal static ArenaChampionDisplay Create(Int32Rect bounds, nint target, Catalog catalog,
        ArenaChampionTierView source, string? championKey, ArenaComposition[]? composition = null,
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
        int teamSize = team?.DisplaySize ?? 3;
        var roster = new ArenaRosterMember[teamSize];
        roster[0] = new(selection?.Champion, selection?.Rating?.Tier, team?.OwnConfirmed, Own: true);
        var allies = team?.AllySlots ?? team?.Teammates ?? [];
        for (int i = 1; i < roster.Length; i++)
        {
            string? ally = allies.ElementAtOrDefault(i - 1);
            roster[i] = ally is null || string.Equals(ally, championKey, StringComparison.OrdinalIgnoreCase)
                ? new(null, null, null)
                : new(Name(ally), source.Data?.Champions.GetValueOrDefault(ally)?.Tier,
                    team?.ConfirmedTeammates?.Contains(ally, StringComparer.OrdinalIgnoreCase));
        }
        return new(bounds, target, source, rows, selection, composition ?? [], grade, roster);

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
        [], view.Grade is { } grade ? Normalize(grade) : null, ArenaCompositions: view.Composition, Roster: view.Roster));

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
    string? EmptyMessage = null, ArenaComposition[]? ArenaCompositions = null, ArenaRosterMember[]? Roster = null);

internal abstract class ChampionRankingOverlay : Window
{
    private const double ArenaRankingWidth = 298;
    private const double CompactArenaWidth = 244;
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-MX");
    private static readonly Brush RowTint = OverlayPanelStyle.HeaderSurface;
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
        panel = new Border { Child = rows };
        OverlayPanelStyle.ApplyFrame(panel);
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
            displayed.ArenaCompositions != view.ArenaCompositions ||
            displayed.Selection != view.Selection || displayed.Grade != view.Grade || !displayed.Rows.SequenceEqual(view.Rows) ||
            !displayed.Composition.SequenceEqual(view.Composition))
        {
            RenderRows(view, compact, widthLimit);
            panel.InvalidateMeasure();
        }
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

        if (view.Roster is { } roster && roster.Any(member => member.Confirmed != true))
            AddArenaCompositions(sections, view.ArenaCompositions ?? [], roster.Any(member => member.Confirmed == true));

        if (!balanced && view.Composition.Length > 0)
        {
            BeginSection(sections);
            sections.Children.Add(Heading("Para tu equipo", OverlayPanelStyle.Gold));
            var firstTradeoff = view.Composition[0].Tradeoff;
            var sharedTradeoff = view.Composition.Length > 1 && view.Composition.All(pick => pick.Tradeoff == firstTradeoff)
                ? firstTradeoff : null;
            if (sharedTradeoff is not null)
                sections.Children.Add(Note(sharedTradeoff, OverlayPanelStyle.Gold));
            foreach (var pick in view.Composition)
                sections.Children.Add(TeamPick(pick, showTradeoff: sharedTradeoff is null));
        }

        BeginSection(sections);
        if (mayhem)
            RenderAvailable(sections, view);
        else
            RenderArenaRanking(sections, view, compact);
    }

    private void AddArenaCompositions(StackPanel sections, ArenaComposition[] compositions, bool hasConfirmedPick)
    {
        sections.Children.Add(Heading("Composiciones sugeridas", OverlayPanelStyle.Gold));
        if (compositions.Length == 0)
            sections.Children.Add(Note(hasConfirmedPick ? "Sin opciones para completar el equipo." :
                "Confirma un campeón para ver sugerencias.", OverlayPanelStyle.Muted));
        foreach (var composition in compositions)
        {
            var lineup = new DockPanel();
            var portraitsRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center };
            lineup.Children.Add(portraitsRow);
            foreach (var member in composition.Members)
            {
                var portrait = ChampionSelectStyle.Portrait(portraits, member.Champion, 22);
                var outline = new Border { Child = portrait, Padding = new Thickness(1), Margin = new Thickness(0, 0, 2, 0),
                    BorderBrush = member.Confirmed ? OverlayPanelStyle.Divider : OverlayPanelStyle.Gold,
                    BorderThickness = new Thickness(1) };
                AutomationProperties.SetName(outline, member.Champion + (member.Confirmed ? ", confirmado" : ", sugerido"));
                portraitsRow.Children.Add(outline);
            }
            var names = new TextBlock
            {
                Text = string.Join(" + ", composition.Members.Where(member => !member.Confirmed).Select(member => member.Champion)),
                FontSize = OverlayPanelStyle.BodySize, FontWeight = FontWeights.SemiBold, Foreground = OverlayPanelStyle.Ink,
                TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center
            };
            lineup.Children.Add(names);
            var tier = TierHexagon.Create(composition.Tier, 24, 26);
            AutomationProperties.SetName(tier, $"Tier proyectado {composition.Tier}");
            var details = new StackPanel();
            details.Children.Add(TierRow(lineup, tier));
            details.Children.Add(Note(composition.Reason, OverlayPanelStyle.Muted));
            sections.Children.Add(new Border
            {
                Child = details, Margin = new Thickness(0, 4, 0, 0), Padding = new Thickness(6, 4, 6, 4),
                BorderBrush = OverlayPanelStyle.SelectionBorder, BorderThickness = OverlayPanelStyle.FrameThickness,
                Background = OverlayPanelStyle.SelectionSurface, SnapsToDevicePixels = true
            });
        }
    }

    private void RenderAvailable(StackPanel sections, ChampionRankingDisplay view)
    {
        var heading = Heading("Campeones disponibles", OverlayPanelStyle.Gold);
        var hasRows = view.Rows.Length > 0;
        var tierHeader = hasRows ? Label("Tier", OverlayPanelStyle.DetailSize, OverlayPanelStyle.Muted) : null;
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
            sections.Children.Add(Note(message, OverlayPanelStyle.Muted));
    }

    private void RenderArenaRanking(StackPanel sections, ChampionRankingDisplay view, bool compact)
    {
        bool rankingOnly = rows.Children.Count == 1 && sections.Children.Count == 0;
        string title = view.Rows.Length > 0 ? $"Top {view.Rows.Length} general" : "Ranking general";
        sections.Children.Add(Heading(title, rankingOnly ? OverlayPanelStyle.Ink : OverlayPanelStyle.Gold,
            rankingOnly ? OverlayPanelStyle.TitleSize : OverlayPanelStyle.SectionSize));
        if (view.Caption.Length > 0)
            sections.Children.Add(SourceNote(view));
        if (view.Rows.Length > 0)
            sections.Children.Add(RankingTable(view.Rows, view.Selection, compact));
    }

    private Border TeamEvaluation(ChampionRankingDisplay view, bool balanced, bool mayhem)
    {
        var grade = view.Grade;
        var waiting = mayhem ? view.Selection is null ? "Esperando tu elección" : "Esperando un aliado" : "Tu equipo";
        var text = new StackPanel();
        text.Children.Add(Heading(balanced ? "Equipo equilibrado" : grade?.Reason ?? waiting,
            balanced ? OverlayPanelStyle.Gold : grade is null ? OverlayPanelStyle.Muted : OverlayPanelStyle.Ink,
            size: OverlayPanelStyle.TitleSize));
        if (mayhem && grade is { } partial && partial.Members < partial.TeamSize)
            text.Children.Add(Note($"{partial.Members}/{partial.TeamSize} campeones", OverlayPanelStyle.Muted));
        if (grade?.Detail is { } detail)
            text.Children.Add(Note(detail, OverlayPanelStyle.Muted));
        var summary = TierRow(text, grade is null ? null : TierHexagon.Create(grade.Tier, 32, 35));
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
            Background = OverlayPanelStyle.HeaderSurface, BorderBrush = OverlayPanelStyle.Divider,
            BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(Inset, 8, Inset, 8),
            Child = content
        };
    }

    private UniformGrid ArenaRoster(ArenaRosterMember[] members)
    {
        var roster = new UniformGrid { Rows = 1, Columns = members.Length, Margin = new Thickness(0, 6, 0, 0) };
        foreach (var member in members)
        {
            var slot = new StackPanel { Margin = new Thickness(3, 0, 3, 0) };
            var art = new Grid { Width = 42, Height = 42 };
            string status = member.Champion is null ? "Sin elegir" : member.Confirmed == true ? "Confirmado" : "Preselección";
            if (member.Champion is { } champion)
            {
                var portrait = ChampionSelectStyle.Portrait(portraits, champion, 38);
                art.Children.Add(new Border { Child = portrait, BorderThickness = new Thickness(1),
                    BorderBrush = member.Own || member.Confirmed == true ? OverlayPanelStyle.Gold : OverlayPanelStyle.Divider });
                if (member.Tier is { } grade)
                {
                    var tier = ChampionTier(champion, grade, 17);
                    tier.HorizontalAlignment = HorizontalAlignment.Right;
                    tier.VerticalAlignment = VerticalAlignment.Bottom;
                    tier.Margin = new Thickness(0, 0, -3, -2);
                    art.Children.Add(tier);
                }
            }
            else
            {
                art.Children.Add(new Rectangle { Stroke = OverlayPanelStyle.Frame, StrokeThickness = 1,
                    StrokeDashArray = new DoubleCollection([3, 2]), Fill = OverlayPanelStyle.Inset });
                art.Children.Add(new TextBlock { Text = "?", FontSize = 17, Foreground = OverlayPanelStyle.Muted,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            }
            slot.Children.Add(art);
            var name = Label(member.Champion ?? (member.Own ? "Tú" : "—"), OverlayPanelStyle.BodySize,
                member.Own ? OverlayPanelStyle.Gold : OverlayPanelStyle.Ink, bold: member.Own);
            name.TextAlignment = TextAlignment.Center;
            name.Margin = new Thickness(0, 4, 0, 0);
            slot.Children.Add(name);
            var state = Label(member.Own && member.Champion is not null ? $"Tú · {status}" : status,
                OverlayPanelStyle.DetailSize, member.Confirmed == true ? OverlayPanelStyle.Gold : OverlayPanelStyle.Muted);
            state.TextAlignment = TextAlignment.Center;
            slot.Children.Add(state);
            roster.Children.Add(slot);
        }
        return roster;
    }

    private Grid TeamPick(ChampionRankingPick pick, bool showTradeoff)
    {
        var grid = TierRow(ChampionIdentity(pick.Champion, 20, bold: true), ChampionTier(pick.Champion, pick.Tier));
        grid.Margin = new Thickness(0, 4, 0, 0);
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var details = new StackPanel();
        var reason = Note(pick.Reason, OverlayPanelStyle.Muted);
        reason.Margin = new Thickness(0);
        details.Children.Add(reason);
        if (showTradeoff && pick.Tradeoff is { } tradeoff)
            details.Children.Add(Note(tradeoff, OverlayPanelStyle.Gold));
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

    private Grid ChampionIdentity(string champion, double portraitSize, bool selected = false, bool bold = false)
    {
        var identity = new Grid();
        identity.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        identity.ColumnDefinitions.Add(new ColumnDefinition());
        var portrait = ChampionSelectStyle.Portrait(portraits, champion, portraitSize);
        portrait.Margin = new Thickness(5, 0, 6, 0);
        identity.Children.Add(portrait);
        var name = Label(champion, OverlayPanelStyle.BodySize,
            selected ? OverlayPanelStyle.Gold : OverlayPanelStyle.Ink, bold: selected || bold);
        name.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(name, 1);
        identity.Children.Add(name);
        if (selected)
        {
            var marker = new Border
            {
                BorderBrush = OverlayPanelStyle.Divider, BorderThickness = new Thickness(1), Margin = new Thickness(4, 0, 0, 0),
                Padding = new Thickness(3, 0, 3, 0), VerticalAlignment = VerticalAlignment.Center,
                Child = Label("TÚ", 9, OverlayPanelStyle.Gold, bold: true)
            };
            identity.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(marker, 2);
            identity.Children.Add(marker);
        }
        return identity;
    }

    private Border ChampionTier(string champion, string? tier, double height = 28) =>
        MayhemTierStyle.ChampionBadge(tier, champion, height * TierHexagon.ArtworkAspect, height,
            mayhem ? "OP.GG" : "GGEZ.GG");

    private static void AddRowBackground(Grid grid, int row, bool selected)
    {
        var background = new Border
        {
            Background = selected ? OverlayPanelStyle.SelectionSurface : RowTint,
            BorderBrush = selected ? OverlayPanelStyle.SelectionBorder : null,
            BorderThickness = OverlayPanelStyle.FrameThickness, SnapsToDevicePixels = true
        };
        Grid.SetRow(background, row);
        Grid.SetColumnSpan(background, grid.ColumnDefinitions.Count);
        grid.Children.Insert(0, background);
    }

    private static TextBlock Heading(string value, Brush color, double size = OverlayPanelStyle.SectionSize) => new()
    {
        Text = value, FontSize = size, Foreground = color, FontFamily = OverlayPanelStyle.Heading,
        FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap
    };

    private static TextBlock Note(string value, Brush color) => new()
    {
        Text = value, FontSize = OverlayPanelStyle.DetailSize, Foreground = color,
        TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0)
    };

    private static TextBlock SourceNote(ChampionRankingDisplay view) =>
        Note(view.Caption, view.PreviousPatch ? OverlayPanelStyle.Gold : OverlayPanelStyle.Muted);

    private static void BeginSection(StackPanel sections)
    {
        if (sections.Children.Count > 0)
            sections.Children.Add(new Border
            {
                Height = 1, Background = OverlayPanelStyle.Divider, Margin = new Thickness(0, 8, 0, 7)
            });
    }

    private Grid RankingTable(ChampionRankingRow[] rows, ChampionRankingRow? selection, bool compact)
    {
        var grid = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        const int headerHeight = 19;
        int rowHeight = compact ? 20 : 24;

        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(compact ? 25 : 30) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(TierColumnWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(compact ? 55 : 60) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(headerHeight) });

        AddCell(0, 1, "Campeón", OverlayPanelStyle.DetailSize, OverlayPanelStyle.Muted);
        AddCell(0, 2, "Tier", OverlayPanelStyle.DetailSize, OverlayPanelStyle.Muted, TextAlignment.Center);
        AddCell(0, 3, "Top 3", OverlayPanelStyle.DetailSize, OverlayPanelStyle.Muted, TextAlignment.Right);
        var rule = new Border
        {
            Background = OverlayPanelStyle.Divider, Height = 1, VerticalAlignment = VerticalAlignment.Bottom
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
            AddCell(line, 0, line.ToString(Spanish), OverlayPanelStyle.DetailSize,
                selected ? OverlayPanelStyle.Gold : OverlayPanelStyle.Muted, TextAlignment.Right);
            AddCellElement(line, 1, ChampionIdentity(row.Champion, compact ? 15 : 18, selected));
            var tier = ChampionTier(row.Champion, row.Tier, rowHeight);
            tier.VerticalAlignment = VerticalAlignment.Center;
            AddCellElement(line, 2, tier);
            if (row.TopThreePercent is { } topThree)
                AddCell(line, 3, TopThree(topThree), OverlayPanelStyle.BodySize, OverlayPanelStyle.Ink, TextAlignment.Right);
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

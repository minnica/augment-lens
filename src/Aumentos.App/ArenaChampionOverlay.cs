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

internal sealed class ArenaChampionOverlay : Window
{
    private const double RankingWidth = 326;
    private const double CompactRankingWidth = 268;
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-MX");
    private static readonly Brush SecondaryText = Overlay.Paint("#A9BCD6");
    private static readonly Brush Divider = Overlay.Paint("#394B65");
    private static readonly Brush BadgeBackground = Overlay.Paint("#22334C");
    private readonly StackPanel rows = new();
    private readonly Border panel;
    private ArenaChampionDisplay? displayed;
    private bool displayedCompact;
    private nint handle;
    private Int32Rect placement;

    internal ArenaChampionOverlay()
    {
        Title = "Aumentos LoL · Tier de campeones";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        ShowActivated = false;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        Background = Brushes.Transparent;
        Focusable = false;
        IsHitTestVisible = false;
        panel = new Border
        {
            Width = RankingWidth, Padding = new Thickness(13, 10, 13, 11), CornerRadius = new CornerRadius(7),
            Background = Overlay.Paint("#F2172436"), BorderBrush = Overlay.Paint("#667C9F"),
            BorderThickness = new Thickness(1), Child = rows
        };
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

    internal void Render(ArenaChampionDisplay? view)
    {
        if (view is null)
        {
            if (IsVisible) Hide();
            displayed = null;
            return;
        }
        var bounds = view.Bounds;
        var dpi = Math.Max(96u, Native.GetDpiForWindow(view.Target)) / 96.0;
        bool compact = bounds.Width / dpi < 1600 || bounds.Height / dpi < 680;
        if (displayed is null || displayedCompact != compact || displayed.Source != view.Source || displayed.Selection != view.Selection ||
            displayed.Grade != view.Grade || !displayed.Rows.SequenceEqual(view.Rows) ||
            !displayed.Composition.SequenceEqual(view.Composition))
            RenderRows(view, compact);
        displayed = view;
        displayedCompact = compact;

        var margin = Math.Min((int)Math.Round(14 * dpi), Math.Min(bounds.Width, bounds.Height) / 4);
        panel.Measure(new Size(panel.Width, double.PositiveInfinity));
        var canvas = panel.DesiredSize;
        int availableWidth = Math.Max(1, bounds.Width - 2 * margin);
        int availableHeight = Math.Max(1, bounds.Height - 2 * margin);
        var scale = Math.Min(dpi, Math.Min(availableWidth / canvas.Width, availableHeight / canvas.Height));
        int width = Math.Max(1, (int)Math.Floor(canvas.Width * scale));
        int height = Math.Max(1, (int)Math.Floor(canvas.Height * scale));
        var position = new Int32Rect(bounds.X + bounds.Width - margin - width, bounds.Y + bounds.Height - margin - height, width, height);
        if (IsVisible && Native.IsWindowVisible(handle) && position == placement)
            return;
        Width = width / dpi;
        Height = height / dpi;
        if (!IsVisible) Show();
        if (Native.SetWindowPos(handle, -1, position.X, position.Y, position.Width, position.Height, 0x0040 | 0x0010))
            placement = position;
    }

    private void RenderRows(ArenaChampionDisplay view, bool compact)
    {
        rows.Children.Clear();
        panel.Width = compact ? CompactRankingWidth : RankingWidth;
        panel.Padding = compact ? new Thickness(10, 7, 10, 8) : new Thickness(13, 10, 13, 11);
        if (view.Selection is { } selection)
        {
            rows.Children.Add(SelectionSummary(selection, view.Source.Data is not null, view.Grade, compact));
            AddDivider(compact);
        }

        if (view.Composition.Length > 0)
        {
            AddLine("Para tu equipo", 16, Brushes.WhiteSmoke, true);
            foreach (var pick in view.Composition)
                rows.Children.Add(TeamPick(pick, compact));
            AddDivider(compact);
        }

        AddLine(view.Rows.Length > 0 ? $"Arena · Top {view.Rows.Length} general" : "Arena · Ranking general",
            compact ? 19 : 21, Brushes.WhiteSmoke, true);
        if (view.Source.Data is not { } data)
        {
            AddLine(view.Source.Status, 13, SecondaryText);
            return;
        }

        AddLine(SourceCaption(data), compact ? 12 : 13, data.IsPrevious ? TierColor("S") : SecondaryText);
        if (view.Rows.Length > 0)
            rows.Children.Add(RankingTable(view.Rows, compact));
    }

    private static StackPanel SelectionSummary(ArenaChampionRow selection, bool hasSource, ArenaCompositionGrade? grade, bool compact)
    {
        var summary = new StackPanel();
        summary.Children.Add(SummaryBadge(selection.Champion, selection.Rating is { } rating ? $"{TopThree(rating)} top 3" :
            hasSource ? "Sin tier publicado" : "Tier pendiente", selection.Rating?.Tier, compact));

        var composition = new StackPanel { Margin = new Thickness(0, compact ? 4 : 6, 0, 0) };
        var detail = grade switch
        {
            null => "Esperando un aliado",
            { Observed: { } observed } => $"GGEZ.GG · {LeaguePatch.Public(observed.Patch)}",
            _ => $"Tier local · {grade.Members}/{grade.TeamSize}"
        };
        if (!compact && grade is { Observed: null })
            detail += " campeones";
        composition.Children.Add(SummaryBadge(compact ? "Composición" : "Composición Arena", detail, grade?.Tier, compact));
        if (grade is not null)
        {
            var reason = Label(grade.Reason, compact ? 12 : 13, SecondaryText);
            reason.Margin = new Thickness(0, 3, 0, 0);
            composition.Children.Add(reason);
        }
        summary.Children.Add(composition);
        return summary;
    }

    private static DockPanel SummaryBadge(string name, string detail, string? tier, bool compact)
    {
        var group = new DockPanel { VerticalAlignment = VerticalAlignment.Top };
        if (tier is not null)
        {
            var grade = Label(tier, compact ? 22 : 27, TierColor(tier), bold: true);
            grade.TextAlignment = TextAlignment.Center;
            grade.VerticalAlignment = VerticalAlignment.Center;
            var badge = new Border
            {
                Width = compact ? 30 : 39, Height = compact ? 34 : 40, CornerRadius = new CornerRadius(6),
                Background = BadgeBackground, Margin = new Thickness(6, 0, 0, 0), Child = grade
            };
            DockPanel.SetDock(badge, Dock.Right);
            group.Children.Add(badge);
        }

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Label(name, compact ? 14 : 17, Brushes.WhiteSmoke, bold: true));
        text.Children.Add(Label(detail, compact ? 12 : 13, SecondaryText));
        group.Children.Add(text);
        return group;
    }

    private static StackPanel TeamPick(ArenaCompositionPick pick, bool compact)
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
        header.Children.Add(Label(pick.Champion, 14, Brushes.WhiteSmoke, bold: true));
        text.Children.Add(header);
        var reason = Label(pick.Reason, 12, SecondaryText);
        reason.TextWrapping = compact ? TextWrapping.NoWrap : TextWrapping.Wrap;
        text.Children.Add(reason);
        return text;
    }

    private static Grid RankingTable(ArenaChampionRow[] rows, bool compact)
    {
        var grid = new Grid { Margin = new Thickness(0, 5, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(compact ? 18 : 24) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(compact ? 21 : 26) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(compact ? 44 : 50) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(compact ? 19 : 24) });

        int headerSize = compact ? 12 : 13;
        AddCell(0, 1, "Campeón", headerSize, SecondaryText);
        AddCell(0, 2, "Tier", headerSize, SecondaryText, TextAlignment.Center);
        AddCell(0, 3, compact ? "Top 3 %" : "Top 3", headerSize, SecondaryText, TextAlignment.Right);
        var rule = new Border { Background = Divider, Height = 1, VerticalAlignment = VerticalAlignment.Bottom };
        Grid.SetColumnSpan(rule, 4);
        grid.Children.Add(rule);

        for (int i = 0; i < rows.Length; i++)
        {
            var row = rows[i];
            int line = i + 1;
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(compact ? 20 : 25) });
            AddCell(line, 0, line.ToString(Spanish), compact ? 12 : 13, SecondaryText);
            AddCell(line, 1, row.Champion, compact ? 15 : 17, Brushes.WhiteSmoke, bold: true);
            if (row.Rating is { } rating)
            {
                int valueSize = compact ? 14 : 16;
                AddCell(line, 2, rating.Tier, valueSize, TierColor(rating.Tier), TextAlignment.Center, bold: true);
                AddCell(line, 3, TopThree(rating, withUnit: !compact), valueSize, Brushes.WhiteSmoke, TextAlignment.Right);
            }
        }
        return grid;

        void AddCell(int row, int column, string value, double size, Brush color,
            TextAlignment alignment = TextAlignment.Left, bool bold = false)
        {
            var text = Label(value, size, color, bold);
            text.TextAlignment = alignment;
            text.VerticalAlignment = VerticalAlignment.Center;
            Typography.SetNumeralAlignment(text, FontNumeralAlignment.Tabular);
            Grid.SetRow(text, row);
            Grid.SetColumn(text, column);
            grid.Children.Add(text);
        }
    }

    private static string TopThree(ArenaChampionTier rating, bool withUnit = true) =>
        rating.TopThreePercent.ToString("0.#", Spanish) + (withUnit ? " %" : "");

    private static string SourceCaption(ArenaChampionTierList data) =>
        $"{ArenaChampionTierSource.Attribution} · {(data.IsPrevious ? "Parche anterior" : "Parche")} {LeaguePatch.Public(data.SourcePatch)}";

    private static Brush TierColor(string tier) => Overlay.Paint(tier switch
    {
        "S" => "#FFD76D",
        "A" => "#86EAC5",
        "B" => "#B6CAFF",
        _ => "#CFD7E3"
    });

    private void AddDivider(bool compact) => rows.Children.Add(new Border
    {
        Height = 1, Background = Divider, Margin = compact ? new Thickness(0, 4, 0, 3) : new Thickness(0, 8, 0, 5)
    });

    private void AddLine(string text, double size, Brush color, bool bold = false)
    {
        var line = Label(text, size, color, bold);
        line.TextWrapping = TextWrapping.Wrap;
        line.Margin = new Thickness(0, 2, 0, 0);
        rows.Children.Add(line);
    }

    private static TextBlock Label(string text, double size, Brush color, bool bold = false) => new()
    {
        Text = text, FontSize = size, Foreground = color, TextTrimming = TextTrimming.CharacterEllipsis,
        FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal
    };
}

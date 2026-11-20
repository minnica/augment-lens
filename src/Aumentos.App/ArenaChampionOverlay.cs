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
    ArenaChampionRow[] Rows, ArenaChampionRow? Selection, ArenaCompositionPick[] Composition)
{
    private const int GlobalRankingLimit = 15;

    internal static ArenaChampionDisplay Create(Int32Rect bounds, nint target, Catalog catalog,
        ArenaChampionTierView source, string? championKey, ArenaCompositionPick[]? composition = null)
    {
        const string tiers = "SABCD";
        var rows = source.Data?.Champions.Values
            .Select(rating => new ArenaChampionRow(Name(rating.Champion), rating))
            .OrderBy(row => row.Rating is { Tier.Length: 1 } rating && tiers.Contains(rating.Tier[0])
                ? tiers.IndexOf(rating.Tier[0]) : tiers.Length)
            .ThenByDescending(row => row.Rating?.TopThreePercent)
            .ThenBy(row => row.Champion, StringComparer.OrdinalIgnoreCase)
            .Take(GlobalRankingLimit).ToArray() ?? [];
        var selection = championKey is null ? null :
            new ArenaChampionRow(Name(championKey), source.Data?.Champions.GetValueOrDefault(championKey));
        return new(bounds, target, source, rows, selection, composition ?? []);

        string Name(string key) => catalog.Champions.FirstOrDefault(champion =>
            string.Equals(champion.Key, key, StringComparison.OrdinalIgnoreCase))?.Name ?? key;
    }
}

internal sealed class ArenaChampionOverlay : Window
{
    private const double RankingWidth = 236;
    private const double RailStart = 0.74;
    private const double PanelTop = 0.16;
    private const double PanelBottom = 0.90;
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
        bool compact = bounds.Height / dpi < 580;
        if (displayed is null || displayedCompact != compact || displayed.Source != view.Source || displayed.Selection != view.Selection ||
            !displayed.Rows.SequenceEqual(view.Rows) || !displayed.Composition.SequenceEqual(view.Composition))
            RenderRows(view, compact);
        displayed = view;
        displayedCompact = compact;

        var margin = Math.Min((int)Math.Round(14 * dpi), Math.Min(bounds.Width, bounds.Height) / 4);
        panel.Measure(new Size(panel.Width, double.PositiveInfinity));
        var canvas = panel.DesiredSize;
        int bottom = Math.Min(bounds.Height - margin, (int)(bounds.Height * PanelBottom));
        int availableWidth = Math.Max(1, bounds.Width - (int)Math.Ceiling(bounds.Width * RailStart) - margin);
        int availableHeight = Math.Max(1, bottom - (int)(bounds.Height * (compact ? 0.11 : PanelTop)));
        var scale = Math.Min(dpi, Math.Min(availableWidth / canvas.Width, availableHeight / canvas.Height));
        int width = Math.Max(1, (int)Math.Floor(canvas.Width * scale));
        int height = Math.Max(1, (int)Math.Floor(canvas.Height * scale));
        var position = new Int32Rect(bounds.X + bounds.Width - margin - width, bounds.Y + bottom - height, width, height);
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
        panel.Padding = compact ? new Thickness(10, 7, 10, 8) : new Thickness(13, 10, 13, 11);
        if (view.Selection is { } selection)
        {
            rows.Children.Add(SelectionBadge(selection, view.Source.Data is not null, compact));
            AddDivider(compact);
        }

        if (view.Composition.Length > 0)
        {
            AddLine("Para tu equipo", 15, Brushes.WhiteSmoke, true);
            foreach (var pick in view.Composition)
                rows.Children.Add(TeamPick(pick, compact));
            AddDivider(compact);
        }

        AddLine(view.Rows.Length > 0 ? $"Arena · Top {view.Rows.Length} general" : "Arena · Ranking general",
            17, Brushes.WhiteSmoke, true);
        if (view.Source.Data is not { } data)
        {
            AddLine(view.Source.Status, 12, SecondaryText);
            return;
        }

        AddLine(SourceCaption(data), 11, data.IsPrevious ? TierColor("S") : SecondaryText);
        if (view.Rows.Length > 0)
            rows.Children.Add(RankingTable(view.Rows, compact));
    }

    private static DockPanel SelectionBadge(ArenaChampionRow selection, bool hasSource, bool compact)
    {
        var group = new DockPanel { Margin = new Thickness(0, 2, 0, 0) };
        if (selection.Rating is { } rating)
        {
            var grade = Label(rating.Tier, compact ? 25 : 29, TierColor(rating.Tier), bold: true);
            grade.TextAlignment = TextAlignment.Center;
            grade.VerticalAlignment = VerticalAlignment.Center;
            var badge = new Border
            {
                Width = compact ? 36 : 43, Height = compact ? 36 : 43, CornerRadius = new CornerRadius(6), Background = BadgeBackground,
                Margin = new Thickness(8, 0, 0, 0), Child = grade
            };
            DockPanel.SetDock(badge, Dock.Right);
            group.Children.Add(badge);
        }

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Label(selection.Champion, 17, Brushes.WhiteSmoke, bold: true));
        text.Children.Add(Label(selection.Rating is { } selectedRating ? $"{TopThree(selectedRating)} top 3" :
            hasSource ? "Sin tier publicado" : "Tier pendiente", 11, SecondaryText));
        group.Children.Add(text);
        return group;
    }

    private static StackPanel TeamPick(ArenaCompositionPick pick, bool compact)
    {
        var text = new StackPanel { Margin = new Thickness(0, compact ? 1 : 3, 0, 0) };
        var header = new DockPanel();
        if (pick.Tier is { } tier)
        {
            var grade = Label(tier, 13, TierColor(tier), bold: true);
            grade.Margin = new Thickness(6, 0, 0, 0);
            DockPanel.SetDock(grade, Dock.Right);
            header.Children.Add(grade);
        }
        header.Children.Add(Label(pick.Champion, 13, Brushes.WhiteSmoke, bold: true));
        text.Children.Add(header);
        var reason = Label(pick.Reason, 11, SecondaryText);
        reason.TextWrapping = compact ? TextWrapping.NoWrap : TextWrapping.Wrap;
        text.Children.Add(reason);
        return text;
    }

    private static Grid RankingTable(ArenaChampionRow[] rows, bool compact)
    {
        var grid = new Grid { Margin = new Thickness(0, 5, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(21) });

        AddCell(0, 1, "Campeón", 11, SecondaryText);
        AddCell(0, 2, "Tier", 11, SecondaryText, TextAlignment.Center);
        AddCell(0, 3, "Top 3", 11, SecondaryText, TextAlignment.Right);
        var rule = new Border { Background = Divider, Height = 1, VerticalAlignment = VerticalAlignment.Bottom };
        Grid.SetColumnSpan(rule, 4);
        grid.Children.Add(rule);

        for (int i = 0; i < rows.Length; i++)
        {
            var row = rows[i];
            int line = i + 1;
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(compact ? 17 : 19) });
            AddCell(line, 0, line.ToString(Spanish), 11, SecondaryText);
            AddCell(line, 1, row.Champion, 13, Brushes.WhiteSmoke, bold: true);
            if (row.Rating is { } rating)
            {
                AddCell(line, 2, rating.Tier, 12, TierColor(rating.Tier), TextAlignment.Center, bold: true);
                AddCell(line, 3, TopThree(rating), 12, Brushes.WhiteSmoke, TextAlignment.Right);
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

    private static string TopThree(ArenaChampionTier rating) => $"{rating.TopThreePercent.ToString("0.#", Spanish)} %";

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

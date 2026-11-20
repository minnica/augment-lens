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
    bool VisibleOptions, ArenaChampionRow[] Rows)
{
    private const int GlobalRankingLimit = 15;

    internal static ArenaChampionDisplay Create(Int32Rect bounds, nint target, Catalog catalog,
        ArenaChampionTierView source, ArenaDraftOffer[]? offers)
    {
        var options = offers ?? [];
        var rows = options.Length > 0
            ? options.Select(offer => new ArenaChampionRow(offer.Champion.Name,
                FindRating(offer.Champion.Key)))
            : source.Data?.Champions.Values.Select(rating => new ArenaChampionRow(
                catalog.Champions.FirstOrDefault(champion => champion.Key == rating.Champion)?.Name ?? rating.Champion,
                rating)) ?? [];
        const string tiers = "SABCD";
        var sorted = rows.OrderBy(row => row.Rating is { Tier.Length: 1 } rating && tiers.Contains(rating.Tier[0])
                ? tiers.IndexOf(rating.Tier[0]) : tiers.Length)
            .ThenByDescending(row => row.Rating?.TopThreePercent).ThenBy(row => row.Champion, StringComparer.OrdinalIgnoreCase);
        return new(bounds, target, source, options.Length > 0,
            sorted.Take(options.Length > 0 ? options.Length : GlobalRankingLimit).ToArray());

        ArenaChampionTier? FindRating(string key) => source.Data?.Champions.GetValueOrDefault(key);
    }
}

internal sealed class ArenaChampionOverlay : Window
{
    private const double OptionsWidth = 300;
    private const double RankingWidth = 384;
    private const int RankingRowsPerColumn = 8;
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-MX");
    private static readonly Brush SecondaryText = Overlay.Paint("#A9BCD6");
    private static readonly Brush Divider = Overlay.Paint("#394B65");
    private readonly StackPanel rows = new();
    private readonly Border panel;
    private ArenaChampionDisplay? displayed;
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
            Width = OptionsWidth, Padding = new Thickness(13, 10, 13, 11), CornerRadius = new CornerRadius(7),
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
        if (displayed is null || displayed.Source != view.Source || displayed.VisibleOptions != view.VisibleOptions ||
            !displayed.Rows.SequenceEqual(view.Rows))
            RenderRows(view);
        displayed = view;

        var bounds = view.Bounds;
        var dpi = Math.Max(96u, Native.GetDpiForWindow(view.Target)) / 96.0;
        var margin = Math.Min((int)Math.Round(14 * dpi), Math.Min(bounds.Width, bounds.Height) / 4);
        panel.Measure(new Size(panel.Width, double.PositiveInfinity));
        var canvas = panel.DesiredSize;
        // Keep the ranking to the right of the lock-in control. On narrow clients,
        // it can use more width by ending above that control.
        bool narrow = bounds.Width < 800;
        int bottom = Math.Min(bounds.Height - margin, (int)(bounds.Height * (narrow ? 0.79 : 0.90)));
        int availableWidth = view.VisibleOptions ? bounds.Width - 2 * margin :
            (int)(bounds.Width * (narrow ? 0.68 : 0.43)) - margin;
        int availableHeight = view.VisibleOptions ? bounds.Height - 2 * margin :
            bottom - (int)(bounds.Height * 0.34);
        var scale = Math.Min(dpi, Math.Min(availableWidth / canvas.Width, availableHeight / canvas.Height));
        int width = Math.Max(1, (int)Math.Floor(canvas.Width * scale));
        int height = Math.Max(1, (int)Math.Floor(canvas.Height * scale));
        int left = view.VisibleOptions ? (int)(bounds.Width * 0.276) : bounds.Width - margin - width;
        int top = view.VisibleOptions ? (int)(bounds.Height * 0.335) : bottom - height;
        var position = new Int32Rect(bounds.X + Math.Clamp(left, margin, bounds.Width - margin - width),
            bounds.Y + Math.Clamp(top, margin, bounds.Height - margin - height), width, height);
        if (IsVisible && Native.IsWindowVisible(handle) && position == placement)
            return;
        Width = width / dpi;
        Height = height / dpi;
        if (!IsVisible) Show();
        if (Native.SetWindowPos(handle, -1, position.X, position.Y, position.Width, position.Height, 0x0040 | 0x0010))
            placement = position;
    }

    private void RenderRows(ArenaChampionDisplay view)
    {
        rows.Children.Clear();
        panel.Width = view.VisibleOptions ? OptionsWidth : RankingWidth;
        if (!view.VisibleOptions)
        {
            RenderRanking(view);
            return;
        }

        AddLine("Arena · Opciones visibles", 15, Brushes.WhiteSmoke, true);
        if (view.Source.Data is { } data)
        {
            AddLine(SourceCaption(data), 11, Overlay.Paint(data.IsPrevious ? "#FFD76D" : "#AAD1FF"));
        }
        else
        {
            AddLine(view.Source.Status, 11, Brushes.LightGray);
        }

        for (int i = 0; i < view.Rows.Length; i++)
        {
            var row = view.Rows[i];
            if (row.Rating is { } rating)
                AddLine($"{i + 1}. {row.Champion} · {rating.Tier} · {rating.TopThreePercent.ToString("0.#", Spanish)} % top 3",
                    13, TierColor(rating.Tier), true);
            else
                AddLine(row.Champion + (view.Source.Data is null ? "" : " · Sin tier"), 13, Brushes.LightGray);
        }
    }

    private void RenderRanking(ArenaChampionDisplay view)
    {
        var title = view.Rows.Length > 0 ? $"Arena · Top {view.Rows.Length} general" : "Arena · Ranking general";
        if (view.Source.Data is not { } data)
        {
            AddLine(title, 17, Brushes.WhiteSmoke, true);
            AddLine(view.Source.Status, 12, SecondaryText);
            return;
        }

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 7) };
        var source = new TextBlock
        {
            Text = SourceCaption(data), FontSize = 11, VerticalAlignment = VerticalAlignment.Center,
            Foreground = data.IsPrevious ? TierColor("S") : SecondaryText, Margin = new Thickness(8, 0, 0, 0)
        };
        DockPanel.SetDock(source, Dock.Right);
        header.Children.Add(source);
        header.Children.Add(new TextBlock
        {
            Text = title, FontSize = 17, Foreground = Brushes.WhiteSmoke, FontWeight = FontWeights.SemiBold
        });
        rows.Children.Add(header);
        if (view.Rows.Length == 0)
            return;

        int firstColumnCount = Math.Min(view.Rows.Length, RankingRowsPerColumn);
        var first = RankingColumn(view.Rows, 0, firstColumnCount);
        if (firstColumnCount == view.Rows.Length)
        {
            rows.Children.Add(first);
            return;
        }

        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition());
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(17) });
        columns.ColumnDefinitions.Add(new ColumnDefinition());
        var divider = new Border
        {
            Width = 1, Background = Divider, HorizontalAlignment = HorizontalAlignment.Center
        };
        Grid.SetColumn(divider, 1);
        var second = RankingColumn(view.Rows, firstColumnCount, view.Rows.Length - firstColumnCount);
        Grid.SetColumn(second, 2);
        columns.Children.Add(first);
        columns.Children.Add(divider);
        columns.Children.Add(second);
        rows.Children.Add(columns);
    }

    private static Grid RankingColumn(ArenaChampionRow[] rows, int start, int count)
    {
        var grid = new Grid { VerticalAlignment = VerticalAlignment.Top };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(21) });

        AddCell(0, 1, "Campeón", 11, SecondaryText);
        AddCell(0, 2, "Tier", 11, SecondaryText, TextAlignment.Center);
        AddCell(0, 3, "Top 3", 11, SecondaryText, TextAlignment.Right);
        var rule = new Border { Background = Divider, Height = 1, VerticalAlignment = VerticalAlignment.Bottom };
        Grid.SetColumnSpan(rule, 4);
        grid.Children.Add(rule);

        for (int i = 0; i < count; i++)
        {
            var row = rows[start + i];
            int line = i + 1;
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(20) });
            AddCell(line, 0, (start + line).ToString(Spanish), 11, SecondaryText);
            AddCell(line, 1, row.Champion, 13, Brushes.WhiteSmoke, bold: true);
            if (row.Rating is { } rating)
            {
                AddCell(line, 2, rating.Tier, 12, TierColor(rating.Tier), TextAlignment.Center, bold: true);
                AddCell(line, 3, $"{rating.TopThreePercent.ToString("0.#", Spanish)} %", 12,
                    Brushes.WhiteSmoke, TextAlignment.Right);
            }
        }
        return grid;

        void AddCell(int row, int column, string value, double size, Brush color,
            TextAlignment alignment = TextAlignment.Left, bool bold = false)
        {
            var text = new TextBlock
            {
                Text = value, FontSize = size, Foreground = color, TextAlignment = alignment,
                VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
                FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal
            };
            Typography.SetNumeralAlignment(text, FontNumeralAlignment.Tabular);
            Grid.SetRow(text, row);
            Grid.SetColumn(text, column);
            grid.Children.Add(text);
        }
    }

    private static string SourceCaption(ArenaChampionTierList data) =>
        $"{ArenaChampionTierSource.Attribution} · {(data.IsPrevious ? "Parche anterior" : "Parche")} {LeaguePatch.Public(data.SourcePatch)}";

    private static Brush TierColor(string tier) => Overlay.Paint(tier switch
    {
        "S" => "#FFD76D",
        "A" => "#86EAC5",
        "B" => "#B6CAFF",
        _ => "#CFD7E3"
    });

    private void AddLine(string text, double size, Brush color, bool bold = false) =>
        rows.Children.Add(new TextBlock
        {
            Text = text, FontSize = size, Foreground = color, TextWrapping = TextWrapping.Wrap,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, Margin = new Thickness(0, 3, 0, 0)
        });
}

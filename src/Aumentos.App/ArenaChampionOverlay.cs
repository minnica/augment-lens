using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace Aumentos;

internal sealed record ArenaChampionRow(string Champion, ArenaChampionTier? Rating);

internal sealed record ArenaChampionDisplay(Int32Rect Bounds, nint Target, ArenaChampionTierView Source,
    bool VisibleOptions, ArenaChampionRow[] Rows)
{
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
        return new(bounds, target, source, options.Length > 0, sorted.Take(options.Length > 0 ? options.Length : 5).ToArray());

        ArenaChampionTier? FindRating(string key) => source.Data?.Champions.GetValueOrDefault(key);
    }
}

internal sealed class ArenaChampionOverlay : Window
{
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-MX");
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
            Width = 300, Padding = new Thickness(13, 10, 13, 11), CornerRadius = new CornerRadius(7),
            Background = Overlay.Paint("#F2172436"), BorderBrush = Overlay.Paint("#667C9F"),
            BorderThickness = new Thickness(1), Child = rows
        };
        Content = new Viewbox { Child = panel, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly };
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
        var scale = Math.Min(dpi, Math.Min((bounds.Width - 2 * margin) / canvas.Width, (bounds.Height - 2 * margin) / canvas.Height));
        int width = Math.Max(1, (int)Math.Ceiling(canvas.Width * scale));
        int height = Math.Max(1, (int)Math.Ceiling(canvas.Height * scale));
        int left = view.VisibleOptions ? (int)(bounds.Width * 0.276) : bounds.Width - margin - width;
        int top = (int)(bounds.Height * (view.VisibleOptions ? 0.335 : 0.70));
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
        AddLine(view.VisibleOptions ? "Arena · Opciones visibles" : "Arena · Top 5 general", 15, Brushes.WhiteSmoke, true);
        if (view.Source.Data is { } data)
        {
            AddLine($"{ArenaChampionTierSource.Attribution} · {(data.IsPrevious ? "Parche anterior" : "Parche")} {LeaguePatch.Public(data.SourcePatch)}",
                11, Overlay.Paint(data.IsPrevious ? "#FFD76D" : "#AAD1FF"));
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

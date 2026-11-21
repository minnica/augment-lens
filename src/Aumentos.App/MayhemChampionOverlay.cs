using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace Aumentos;

internal sealed record MayhemChampionDisplay(Int32Rect Bounds, nint Target,
    MayhemChampionTierList Tiers, MayhemDraftChoice[] Choices, MayhemBenchChoice[]? Bench = null)
{
    internal int Top => Bench is not null ? 16 : Choices.Select(choice => choice.Bottom).DefaultIfEmpty(752).Max() + 1;

    internal static MayhemChampionDisplay? Create(Int32Rect bounds, nint target, MayhemChampionTierList? tiers,
        ChampionSelection? selected, MayhemDraftChoice[]? choices, MayhemBenchChoice[] bench)
    {
        if (tiers is null)
            return null;
        if (selected is not null)
            return bench.Length > 0 ? new(bounds, target, tiers, [], bench) : null;
        return choices is { Length: > 0 } ? new(bounds, target, tiers, choices) : null;
    }
}

internal sealed class MayhemChampionOverlay : Window
{
    private const int LeftEdge = 510;
    private const int WidthInClient = 980;
    private const int HeightInClient = 72;
    private readonly Canvas badges = new() { Width = WidthInClient, Height = HeightInClient };
    private MayhemChampionDisplay? displayed;
    private Int32Rect placement;
    private nint handle;

    internal MayhemChampionOverlay()
    {
        Title = "Aumentos LoL · Campeones de ARAM Caos";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        ShowActivated = false;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        Background = Brushes.Transparent;
        Focusable = false;
        IsHitTestVisible = false;
        Content = new Viewbox { Child = badges, Stretch = Stretch.Fill };
        SourceInitialized += (_, _) =>
        {
            handle = new WindowInteropHelper(this).Handle;
            const int extendedStyle = -20;
            Native.SetWindowLongW(handle, extendedStyle, Native.GetWindowLongW(handle, extendedStyle) | 0x080000A0);
        };
    }

    internal void Render(MayhemChampionDisplay? view)
    {
        if (view is null)
        {
            if (IsVisible) Hide();
            displayed = null;
            return;
        }

        if (displayed is null || displayed.Tiers != view.Tiers || displayed.Bounds.Width != view.Bounds.Width ||
            !displayed.Choices.SequenceEqual(view.Choices) || (displayed.Bench is null) != (view.Bench is null) ||
            !(displayed.Bench ?? []).SequenceEqual(view.Bench ?? []))
            Draw(view);
        displayed = view;

        var bounds = view.Bounds;
        int height = view.Bench is null ? HeightInClient : 82;
        var relative = MayhemDraftReader.Scaled(LeftEdge, view.Top, WidthInClient, height, bounds.Width, bounds.Height);
        var position = new Int32Rect(bounds.X + relative.X, bounds.Y + relative.Y, relative.Width, relative.Height);
        if (IsVisible && Native.IsWindowVisible(handle) && position == placement)
            return;

        var dpi = Math.Max(96u, Native.GetDpiForWindow(view.Target)) / 96.0;
        Width = position.Width / dpi;
        Height = position.Height / dpi;
        if (!IsVisible) Show();
        if (Native.SetWindowPos(handle, -1, position.X, position.Y, position.Width, position.Height, 0x0040 | 0x0010))
            placement = position;
    }

    private void Draw(MayhemChampionDisplay view)
    {
        badges.Children.Clear();
        badges.Height = view.Bench is null ? HeightInClient : 82;
        double scale = Math.Clamp((double)MayhemDraftReader.ReferenceWidth / view.Bounds.Width, 1, 1.35);
        if (view.Bench is { } bench)
        {
            foreach (var choice in bench)
            {
                if (view.Tiers.Letter(choice.Champion.Key) is not { } letter)
                    continue;
                var badge = MayhemTierStyle.ChampionBadge(letter, choice.Champion.Name, 31 * scale, 29 * scale, 21 * scale);
                var right = MayhemBenchPortraits.PortraitLeft(choice.Slot) + MayhemBenchPortraits.PortraitSize - 4;
                Canvas.SetLeft(badge, right - LeftEdge - badge.Width);
                Canvas.SetTop(badge, MayhemBenchPortraits.PortraitSize - 3 - badge.Height);
                badges.Children.Add(badge);
            }
            return;
        }
        foreach (var choice in view.Choices)
        {
            var badge = MayhemTierStyle.ChampionBadge(view.Tiers.Letter(choice.Champion.Key), choice.Champion.Name,
                58 * scale, 46 * scale, 32 * scale);
            Canvas.SetLeft(badge, choice.Center - LeftEdge - badge.Width / 2);
            Canvas.SetTop(badge, 7);
            badges.Children.Add(badge);
        }
    }
}

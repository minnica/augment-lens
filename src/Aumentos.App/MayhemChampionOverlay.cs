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
    internal int Top => Choices.Select(choice => choice.Bottom).DefaultIfEmpty(752).Max() + 1;

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
    internal const string TierSizeTitle = "Tamaño de los tiers de campeones";
    internal const int TierSizeMaximum = 200;
    private const int LeftEdge = 510;
    private const int WidthInClient = 980;
    private const int BenchBadgeWidth = 36;
    private const int BenchBadgeCenter = MayhemBenchPortraits.PortraitTop + MayhemBenchPortraits.PortraitSize + 2;
    private readonly Canvas badges = new() { Width = WidthInClient };
    private MayhemChampionDisplay? displayed;
    private Int32Rect placement;
    private nint handle;
    internal int TierSizePercent { get; private set; } = BuildSizePreference.Default;

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

    internal void SetTierSize(int percent)
    {
        int updated = Math.Clamp(percent, BuildSizePreference.Minimum, TierSizeMaximum);
        if (TierSizePercent == updated)
            return;
        TierSizePercent = updated;
        if (displayed is { } view)
        {
            displayed = null;
            Render(view);
        }
    }

    internal static FrameworkElement CreateTierPreview() =>
        TierHexagon.Create("A", BenchBadgeWidth, BenchBadgeWidth / TierHexagon.ArtworkAspect);

    internal void Render(MayhemChampionDisplay? view)
    {
        if (view is null)
        {
            if (IsVisible) Hide();
            displayed = null;
            return;
        }

        var bounds = view.Bounds;
        bool bench = view.Bench is not null;
        double screenScale = Math.Clamp((double)MayhemDraftReader.ReferenceWidth / bounds.Width, 1, 1.35);
        double width = (bench ? BenchBadgeWidth : 54) * screenScale * TierSizePercent / 100.0;
        if (bench)
            width = Math.Min(width, MayhemBenchPortraits.PortraitSize - 4);
        var size = new Size(width, width / TierHexagon.ArtworkAspect);
        int top = bench ? (int)Math.Floor(BenchBadgeCenter - size.Height / 2) : view.Top;
        int height = (int)Math.Ceiling(size.Height) + (bench ? 2 : 8);

        if (displayed is null || displayed.Tiers != view.Tiers || displayed.Bounds.Width != bounds.Width ||
            !displayed.Choices.SequenceEqual(view.Choices) || (displayed.Bench is null) != (view.Bench is null) ||
            !(displayed.Bench ?? []).SequenceEqual(view.Bench ?? []))
        {
            badges.Height = height;
            Draw(view, size, bench ? BenchBadgeCenter - size.Height / 2 - top : 7);
        }
        displayed = view;

        var relative = MayhemDraftReader.Scaled(LeftEdge, top, WidthInClient, height, bounds.Width, bounds.Height);
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

    private void Draw(MayhemChampionDisplay view, Size size, double top)
    {
        badges.Children.Clear();
        if (view.Bench is { } bench)
        {
            foreach (var choice in bench)
            {
                if (view.Tiers.Letter(choice.Champion.Key) is not { } letter)
                    continue;
                double center = MayhemBenchPortraits.PortraitLeft(choice.Slot) + MayhemBenchPortraits.PortraitSize / 2.0;
                AddBadge(letter, choice.Champion.Name, center);
            }
            return;
        }
        foreach (var choice in view.Choices)
            AddBadge(view.Tiers.Letter(choice.Champion.Key), choice.Champion.Name, choice.Center);

        void AddBadge(string? tier, string champion, double center)
        {
            var badge = MayhemTierStyle.ChampionBadge(tier, champion, size.Width, size.Height);
            Canvas.SetLeft(badge, center - LeftEdge - size.Width / 2);
            Canvas.SetTop(badge, top);
            badges.Children.Add(badge);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace Aumentos;

internal sealed class Overlay
{
    internal const string TierSizeTitle = "Tamaño de los tiers de cartas";
    internal const int TierSizeMaximum = 300;
    private const string TierSizeButtonLabel = "Tamaño de los tiers";
    private const int TierSizeButtonWidth = 148;
    private const double MayhemTitleToCardEdge = 0.213;
    private const double ArenaTitleToCardEdge = 0.207;
    private const double CardTopToLowerFrame = 0.465;
    private const double LowerFrameToReroll = 0.037;
    private const int CompactTierWidth = 38;
    private const int CompactTierHeight = 42;
    private readonly Func<int, bool>? saveTierSize;
    private readonly BadgeWindow[] badges = Enumerable.Range(1, 4).Select(slot => new BadgeWindow(slot)).ToArray();
    private BuildSizeButton? tierSizeButton;
    private OverlayUpdate? displayedCards;
    private (nint Target, Calibration Layout, bool UpgradeHeading, bool ItemOffer)? arenaPlacement;
    private int? arenaCardEdge;
    private int tierSizePercent = BuildSizePreference.Default;

    internal Overlay(Func<int, bool>? saveTierSize = null) => this.saveTierSize = saveTierSize;

    internal int TierSizePercent => tierSizePercent;
    internal IReadOnlyList<Window> Windows => badges;
    internal bool IsVisible => badges.Any(badge => badge.NativeVisible);

    internal void Hide()
    {
        displayedCards = null;
        arenaPlacement = null;
        arenaCardEdge = null;
        HideTierSizeButton();
        foreach (var badge in badges)
            badge.HideBadge();
    }

    internal void Close()
    {
        tierSizeButton?.Close();
        foreach (var badge in badges)
            badge.Close();
    }

    internal void SetTierSize(int percent)
    {
        int updated = Math.Clamp(percent, BuildSizePreference.Minimum, TierSizeMaximum);
        if (updated == tierSizePercent)
            return;
        tierSizePercent = updated;
        if (displayedCards is { } view && IsVisible &&
            (view.Target == 0 || Native.GetForegroundWindow() == view.Target))
            Render(view);
    }

    internal FrameworkElement CreateTierPreview() =>
        new Border { Child = CreateCardBadge("A"), IsHitTestVisible = false, Focusable = false };

    private static Border CreateCardBadge(string tier) =>
        TierHexagon.Create(tier, CompactTierWidth, CompactTierHeight);

    internal void Render(OverlayUpdate update)
    {
        if (update.Bounds is not { Width: > 0, Height: > 0 } bounds || update.Layout is not { } layout ||
            update.Labels is not { } labels || layout.Titles.Length == 0 || update.Mode is not (Mode.Caos or Mode.Arena))
        {
            Hide();
            return;
        }

        uint targetDpi = update.Target != 0 ? Native.GetDpiForWindow(update.Target) : 0;
        double dpi = targetDpi > 0 ? targetDpi / 96.0 : VisualTreeHelper.GetDpi(badges[0]).DpiScaleX;
        var titles = layout.Titles.Select(title => title.Pixels(bounds.Width, bounds.Height)).ToArray();
        int widthLimit = Math.Clamp(titles.Min(title => title.Width), 1, bounds.Width);
        int margin = (int)Math.Ceiling(6 * dpi);
        bool arenaCards = update.Mode == Mode.Arena;
        bool arenaItems = arenaCards && update.ArenaItemOffer;
        if (displayedCards is { } previous && (previous.Target != update.Target || previous.Mode != update.Mode ||
            previous.Layout != layout || previous.ArenaItemOffer != update.ArenaItemOffer ||
            previous.ArenaUpgradeHeading != update.ArenaUpgradeHeading ||
            (previous.Prismatics is null) != (update.Prismatics is null)))
            tierSizeButton?.CloseScaleEditor();
        displayedCards = update;
        double titleToEdge = arenaCards ? ArenaTitleToCardEdge : MayhemTitleToCardEdge;
        int cardEdge = titles.Min(title => title.Y) - (int)Math.Round(bounds.Height * titleToEdge);
        if (arenaCards)
        {
            var placement = (update.Target, layout, update.ArenaUpgradeHeading, arenaItems);
            if (arenaPlacement != placement)
            {
                arenaPlacement = placement;
                arenaCardEdge = null;
            }
            // The frame stays still while card glows and rerolls change the detected bright edge.
            arenaCardEdge ??= update.ArenaCardTop;
            cardEdge = arenaCardEdge ?? cardEdge;
        }
        else
        {
            arenaPlacement = null;
            arenaCardEdge = null;
        }
        bool prismaticsOnRight = update.Prismatics is not null && !BuildOverlay.PrismaticSide(bounds, titles, dpi).OnLeft;
        var tierControl = saveTierSize is not null
            ? PlaceTierSizeButton(bounds, titles, cardEdge, margin, dpi, prismaticsOnRight)
            : null;
        int badgeWidth = (int)Math.Ceiling(CompactTierWidth * dpi);
        int badgeHeight = (int)Math.Ceiling(CompactTierHeight * dpi) + 1;
        int lowerEdge = cardEdge + (int)Math.Round(bounds.Height * CardTopToLowerFrame);
        int bottomLimit = Math.Min(bounds.Height - margin, lowerEdge + (int)(bounds.Height * LowerFrameToReroll));

        for (int index = 0; index < badges.Length; index++)
        {
            var badge = badges[index];
            if (index >= labels.Length || index >= titles.Length || labels[index] is not { } label ||
                label.VisibleTierText is not ("S+" or "S" or "A" or "B" or "C" or "D" or "E"))
            {
                badge.HideBadge();
                continue;
            }

            var title = titles[index];
            int titleBottom = title.Y + title.Height;
            int heightLimit = bottomLimit - titleBottom;
            if (heightLimit <= 0)
            {
                badge.HideBadge();
                continue;
            }

            double fit = Math.Min((double)heightLimit / badgeHeight, (double)widthLimit / badgeWidth);
            double scale = Math.Min(fit, tierSizePercent / 100.0);
            int height = Math.Clamp((int)Math.Round(badgeHeight * scale), 1, heightLimit);
            // Keep the tier centered at the frame until its lower half would reach the reroll button.
            int top = Math.Clamp(lowerEdge - height / 2, titleBottom, bottomLimit - height);
            int width = Math.Clamp((int)Math.Round(badgeWidth * scale), 1, widthLimit);
            int x = Math.Clamp(title.X + (title.Width - width) / 2, 0, bounds.Width - width);
            badge.SetTier(label.VisibleTierText);
            badge.Display(new(bounds.X + x, bounds.Y + top, width, height), dpi, allowUpscale: scale > 1);
        }

        if (tierControl is { } slot && IsVisible)
            ShowTierSizeButton(bounds, slot, dpi);
        else
            HideTierSizeButton();
    }

    private static Int32Rect? PlaceTierSizeButton(
        Int32Rect bounds, Int32Rect[] titles, int cardEdge, int gap, double dpi, bool prismaticsOnRight)
    {
        int height = (int)Math.Ceiling(BuildSizeButton.ButtonHeight * dpi);
        int minimumWidth = (int)Math.Ceiling(TierSizeButtonWidth * dpi);
        int right = bounds.Width - (int)Math.Round(14 * dpi);
        int rightStart = titles.Max(title => title.X + title.Width) + gap;
        int width = right - rightStart;
        int y = prismaticsOnRight ? TierControlTop(bounds, dpi) : cardEdge - height - gap;
        if (width >= minimumWidth && y >= gap)
            return new(rightStart, y, width, height);

        // When the right gutter is narrow, keep the control near the edge above the cards.
        y = TierControlTop(bounds, dpi);
        width = right - bounds.Width * 2 / 3;
        if (width < minimumWidth || y + height + gap >= cardEdge)
            return null;
        return new(right - width, y, width, height);
    }

    internal static int TierControlTop(Int32Rect bounds, double dpi) =>
        Math.Max((int)Math.Ceiling(6 * dpi), (int)Math.Ceiling(bounds.Height * 0.055));

    private void ShowTierSizeButton(Int32Rect bounds, Int32Rect slot, double dpi)
    {
        tierSizeButton ??= new BuildSizeButton(OpenTierSizeEditor, TierSizeTitle, label: TierSizeButtonLabel);
        var owner = badges.First(badge => badge.NativeVisible);
        if (tierSizeButton.Owner != owner)
            tierSizeButton.Owner = owner;
        int preferredWidth = tierSizeButton.Content is BuildScaleEditor ? BuildSizeButton.ScaleEditorWidth : TierSizeButtonWidth;
        int width = Math.Min(slot.Width, (int)Math.Ceiling(preferredWidth * dpi));
        int x = slot.X + slot.Width - width;
        tierSizeButton.Display(new(bounds.X + x, bounds.Y + slot.Y, width, slot.Height), dpi);
    }

    private void OpenTierSizeEditor()
    {
        if (saveTierSize is not { } save || tierSizeButton is not { IsVisible: true } button ||
            displayedCards is not { } view || (view.Target != 0 && Native.GetForegroundWindow() != view.Target))
            return;
        button.OpenScaleEditor(tierSizePercent, percent =>
        {
            SetTierSize(percent);
            return save(percent);
        }, FinishTierSizeEditor, TierSizeMaximum);
        Render(view);
    }

    private void FinishTierSizeEditor()
    {
        tierSizeButton?.CloseScaleEditor();
        if (displayedCards is { } view && (view.Target == 0 || Native.GetForegroundWindow() == view.Target))
            Render(view);
    }

    private void HideTierSizeButton()
    {
        tierSizeButton?.CloseScaleEditor();
        tierSizeButton?.Hide();
    }

    internal static Brush Paint(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    private sealed class BadgeWindow : Window
    {
        private readonly Border badge = new();
        private readonly Viewbox frame;
        private string? displayed;
        private nint handle;
        private Int32Rect placement;
        internal bool NativeVisible => handle != 0 && Native.IsWindowVisible(handle);

        internal BadgeWindow(int slot)
        {
            Title = $"Aumentos LoL · Carta {slot}";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            ShowActivated = false;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
            Background = Brushes.Transparent;
            IsHitTestVisible = false;
            UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(badge, TextFormattingMode.Display);
            frame = new Viewbox { Child = badge, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly };
            Content = frame;
            SourceInitialized += (_, _) =>
            {
                handle = new WindowInteropHelper(this).Handle;
                const int exStyle = -20;
                Native.SetWindowLongW(handle, exStyle, Native.GetWindowLongW(handle, exStyle) | 0x080000A0);
            };
        }

        internal void HideBadge()
        {
            if (IsVisible)
                Hide();
        }

        internal void SetTier(string tier)
        {
            if (displayed == tier)
                return;
            badge.Child = CreateCardBadge(tier);
            displayed = tier;
        }

        internal void Display(Int32Rect target, double dpi, bool allowUpscale)
        {
            frame.StretchDirection = allowUpscale ? StretchDirection.Both : StretchDirection.DownOnly;
            if (IsVisible && NativeVisible && target == placement)
                return;
            Width = target.Width / dpi;
            Height = target.Height / dpi;
            if (!IsVisible)
                Show();
            const uint showWithoutActivation = 0x0040 | 0x0010;
            if (Native.SetWindowPos(handle, -1, target.X, target.Y, target.Width, target.Height, showWithoutActivation))
                placement = target;
        }
    }
}

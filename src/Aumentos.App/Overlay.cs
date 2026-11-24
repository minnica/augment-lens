using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;

namespace Aumentos;

internal sealed class Overlay
{
    internal const string TierSizeTitle = "Tamaño de los tiers de cartas";
    private const string TierSizeButtonLabel = "Tamaño de los tiers";
    private const int TierSizeButtonWidth = 148;
    private const int SummaryWidth = 160;
    private const int MayhemSummaryWidth = 132;
    private const int ArenaEvidenceWidth = 200;
    private const int DetailWidth = 260;
    private const double MayhemTitleToCardEdge = 0.213;
    // Limit the overlap to the upper frame, where there are no ability icons.
    private const double StandardFrameOverlap = 0.036;
    private const double ArenaTitleToCardEdge = 0.207;
    private const double ArenaStarClearance = 0.022;
    private const double ArenaDetailOverlap = 0.046;
    private const double ArenaItemOverlap = 0.052;
    private const double ArenaItemDefaultScale = 1.25;
    private const double ArenaRoundClockBottom = 0.09;
    private const double ArenaUpgradeHeadingBottom = 0.146;
    private const double CardTopToLowerFrame = 0.465;
    private const double LowerFrameToReroll = 0.037;
    private const int CompactTierWidth = 32;
    private const int CompactTierHeight = 36;
    private const int CompactTierFontSize = 19;
    private readonly Func<int, bool>? saveTierSize;
    private readonly BadgeWindow[] badges = Enumerable.Range(1, 4).Select(slot => new BadgeWindow(slot)).ToArray();
    private BuildSizeButton? tierSizeButton;
    private OverlayUpdate? displayedCards;
    private (nint Target, Calibration Layout, bool UpgradeHeading, bool ItemOffer)? arenaPlacement;
    private int? arenaCardEdge;
    private int tierSizePercent = BuildSizePreference.Default;

    internal Overlay(Func<int, bool>? saveTierSize = null) => this.saveTierSize = saveTierSize;

    internal bool ShowDetails { private get; set; }
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
        int updated = Math.Clamp(percent, BuildSizePreference.Minimum, BuildSizePreference.Maximum);
        if (updated == tierSizePercent)
            return;
        tierSizePercent = updated;
        if (displayedCards is { } view && IsVisible &&
            (view.Target == 0 || Native.GetForegroundWindow() == view.Target))
            Render(view);
    }

    internal FrameworkElement CreateTierPreview(bool arenaItems = false)
    {
        var badge = new BadgePanel();
        var example = arenaItems
            ? new CardComparison("A", ArenaChoice: new("Reverberación", Tier.A,
                "Inmovilizar activa impactos", Recommended: true, TopPosition: 4))
            : new CardComparison("A", new OpGgCard("A"));
        bool lowerTier = !ShowDetails && !arenaItems;
        int width = ShowDetails ? DetailWidth : arenaItems ? SummaryWidth : CompactTierWidth;
        badge.Render(example, width, ShowDetails, arenaItems ? Mode.Arena : Mode.Caos, lowerTier);
        if (arenaItems)
        {
            // Keep the item baseline separate from the slider's layout transform.
            badge.LayoutTransform = new ScaleTransform(ArenaItemDefaultScale, ArenaItemDefaultScale);
        }
        return new Border { Child = badge, IsHitTestVisible = false, Focusable = false };
    }

    internal void Render(OverlayUpdate update)
    {
        if (update.Bounds is not { Width: > 0, Height: > 0 } bounds || update.Layout is not { } layout ||
            update.Labels is not { } labels || layout.Titles.Length == 0)
        {
            Hide();
            return;
        }

        uint targetDpi = update.Target != 0 ? Native.GetDpiForWindow(update.Target) : 0;
        double dpi = targetDpi > 0 ? targetDpi / 96.0 : VisualTreeHelper.GetDpi(badges[0]).DpiScaleX;
        var titles = layout.Titles.Select(title => title.Pixels(bounds.Width, bounds.Height)).ToArray();
        bool mayhem = update.Mode == Mode.Caos;
        int maximumWidth = ShowDetails ? DetailWidth : mayhem ? MayhemSummaryWidth : SummaryWidth;
        int widthLimit = Math.Clamp(titles.Min(title => title.Width), 1, bounds.Width);
        int width = Math.Min(widthLimit, Math.Max(1, (int)(maximumWidth * dpi)));
        int margin = (int)Math.Ceiling(6 * dpi);
        bool arenaCards = update.Mode == Mode.Arena;
        bool arenaItems = arenaCards && update.ArenaItemOffer;
        if (displayedCards is { } previous && (previous.Target != update.Target || previous.Mode != update.Mode ||
            previous.Layout != layout || previous.ArenaItemOffer != update.ArenaItemOffer ||
            previous.ArenaUpgradeHeading != update.ArenaUpgradeHeading ||
            (previous.Prismatics is null) != (update.Prismatics is null)))
            tierSizeButton?.CloseScaleEditor();
        displayedCards = update;
        double baseScale = arenaItems ? ArenaItemDefaultScale : 1;
        var (titleToEdge, frameOverlap) = update.Mode switch
        {
            Mode.Caos => (MayhemTitleToCardEdge, StandardFrameOverlap),
            Mode.Arena when arenaItems => (ArenaTitleToCardEdge, ArenaItemOverlap),
            Mode.Arena when ShowDetails => (ArenaTitleToCardEdge, ArenaDetailOverlap),
            Mode.Arena when labels.Any(label => label?.IsArenaAugment == true) => (ArenaTitleToCardEdge, ArenaStarClearance),
            Mode.Arena => (ArenaTitleToCardEdge, StandardFrameOverlap),
            _ => (0.0, 0.0)
        };
        int? cardEdge = titleToEdge > 0
            ? titles.Min(title => title.Y) - (int)Math.Round(bounds.Height * titleToEdge)
            : null;
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
        int maxOverlap = (int)(bounds.Height * frameOverlap);
        bool prismaticsOnRight = update.Prismatics is not null && !BuildOverlay.PrismaticSide(bounds, titles, dpi).OnLeft;
        var tierControl = saveTierSize is not null && cardEdge is { } edge
            ? PlaceTierSizeButton(bounds, titles, edge, margin, dpi, prismaticsOnRight)
            : null;
        int tierClearance = tierControl is { InTopBand: true, Bounds: var reserved } ? reserved.Y + reserved.Height + margin : margin;

        int mayhemHeight = 0;
        if (mayhem)
        {
            // Reserve the same space for every frame before placing the badges on the cards.
            int visibleSlots = Math.Min(badges.Length, Math.Min(labels.Length, titles.Length));
            for (int index = 0; index < visibleSlots; index++)
                if (labels[index] is { } label && !UsesLowerTier(label, update.Mode))
                    mayhemHeight = Math.Max(mayhemHeight, badges[index].Prepare(label, width, dpi, ShowDetails, update.Mode));
        }

        for (int index = 0; index < badges.Length; index++)
        {
            var badge = badges[index];
            if (index >= labels.Length || index >= titles.Length || labels[index] is not { } label)
            {
                badge.HideBadge();
                continue;
            }

            var title = titles[index];
            int gap = label.ArenaChoice is null ? margin : Math.Max(2 * margin, title.Height / 3);
            int anchor = cardEdge ?? (title.Y - gap);
            int center = title.X + title.Width / 2;
            if (UsesLowerTier(label, update.Mode) && cardEdge is { } topEdge)
            {
                int preferred = mayhem && label.DisplayedMayhemObserved is null && !label.RecommendedAugment
                    ? CompactTierWidth : ArenaEvidenceWidth;
                int lowerWidth = Math.Min(widthLimit, Math.Max(1, (int)Math.Ceiling(preferred * dpi)));
                int desired = badge.Prepare(label, lowerWidth, dpi, ShowDetails, update.Mode, lowerTier: true);
                int lowerEdge = topEdge + (int)Math.Round(bounds.Height * CardTopToLowerFrame);
                int hexagonHalf = (int)Math.Ceiling(CompactTierHeight * dpi / 2);
                int above = lowerEdge - title.Y - title.Height;
                int below = Math.Min(bounds.Height - margin - lowerEdge, (int)(bounds.Height * LowerFrameToReroll));
                double fit = Math.Min((double)above / Math.Max(1, desired - hexagonHalf),
                    Math.Min((double)below / hexagonHalf, (double)widthLimit / lowerWidth));
                if (fit > 0)
                {
                    double lowerScale = Math.Min(fit, tierSizePercent / 100.0);
                    int lowerHeight = Math.Max(1, (int)Math.Round(desired * lowerScale));
                    int lowerBottom = lowerEdge + (int)Math.Round(hexagonHalf * lowerScale);
                    int fitted = Math.Clamp((int)Math.Round(lowerWidth * lowerScale), 1, widthLimit);
                    int lowerX = Math.Clamp(center - fitted / 2, 0, bounds.Width - fitted);
                    badge.Display(new(bounds.X + lowerX, bounds.Y + lowerBottom - lowerHeight, fitted, lowerHeight),
                        dpi, allowUpscale: lowerScale > 1, contentHeight: 0);
                    continue;
                }
            }

            int minimumTop = tierClearance;
            if (arenaCards && title.X < bounds.Width / 2 && title.X + title.Width > bounds.Width / 2)
            {
                var headerBottom = update.ArenaUpgradeHeading ? ArenaUpgradeHeadingBottom : ArenaRoundClockBottom;
                minimumTop = Math.Max(minimumTop, (int)Math.Ceiling(bounds.Height * headerBottom));
            }
            int roomAbove = anchor - minimumTop;
            int overlap = Math.Min(roomAbove, maxOverlap);
            int heightLimit = arenaCards && !label.IsArenaVariable ? 2 * overlap : roomAbove + overlap;
            if (heightLimit <= 0)
            {
                badge.HideBadge();
                continue;
            }

            int preferredWidth = label.IsArenaVariable ? DetailWidth : label.DisplayedArenaPlacement is not null
                ? ArenaEvidenceWidth : SummaryWidth;
            int badgeWidth = mayhem || ShowDetails ? width : Math.Min(widthLimit, Math.Max(1, (int)(preferredWidth * dpi)));
            int desiredHeight = mayhem && !UsesLowerTier(label, update.Mode) ? mayhemHeight :
                badge.Prepare(label, badgeWidth, dpi, ShowDetails, update.Mode);
            double fittingScale = Math.Min((double)widthLimit / badgeWidth, (double)heightLimit / desiredHeight);
            double fittedBase = Math.Min(baseScale, fittingScale);
            double scale = Math.Min(fittingScale, fittedBase * tierSizePercent / 100.0);
            int height = Math.Clamp((int)Math.Ceiling(desiredHeight * scale), 1, heightLimit);
            int bottom = anchor + Math.Min(height / 2, maxOverlap);
            int fittedWidth = Math.Clamp(badgeWidth * height / desiredHeight, 1, widthLimit);
            int x = bounds.X + Math.Clamp(center - fittedWidth / 2, 0, bounds.Width - fittedWidth);
            badge.Display(new(x, bounds.Y + bottom - height, fittedWidth, height), dpi,
                allowUpscale: scale > 1, contentHeight: mayhemHeight / dpi);
        }

        if (tierControl is { Bounds: var slot } && IsVisible)
            ShowTierSizeButton(bounds, slot, dpi);
        else
            HideTierSizeButton();
    }

    private bool UsesLowerTier(CardComparison label, Mode? mode) =>
        !ShowDetails && mode is Mode.Caos or Mode.Arena && label.ArenaChoice is null &&
        label != CardComparison.Unread && label.Local != "Variable" &&
        (label.Local != CardComparison.Missing || label.DisplayedArenaPlacement is not null ||
            mode == Mode.Caos && (label.External?.Tier is not null || label.DisplayedMayhemObserved is not null));

    private static (Int32Rect Bounds, bool InTopBand)? PlaceTierSizeButton(
        Int32Rect bounds, Int32Rect[] titles, int cardEdge, int gap, double dpi, bool prismaticsOnRight)
    {
        int height = (int)Math.Ceiling(BuildSizeButton.ButtonHeight * dpi);
        int minimumWidth = (int)Math.Ceiling(TierSizeButtonWidth * dpi);
        int right = bounds.Width - (int)Math.Round(14 * dpi);
        int rightStart = titles.Max(title => title.X + title.Width) + gap;
        int width = right - rightStart;
        int y = prismaticsOnRight ? TierControlTop(bounds, dpi) : cardEdge - height - gap;
        if (width >= minimumWidth && y >= gap)
            return (new(rightStart, y, width, height), false);

        // When the right gutter is narrow, keep the control near the edge above the cards.
        y = TierControlTop(bounds, dpi);
        width = right - bounds.Width * 2 / 3;
        if (width < minimumWidth || y + height + gap >= cardEdge)
            return null;
        return (new(right - width, y, width, height), true);
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
        }, FinishTierSizeEditor);
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
        private readonly BadgePanel badge = new();
        private readonly Viewbox frame;
        private CardComparison? displayed;
        private (int Width, double Dpi, bool ShowDetails, Mode? Mode, bool LowerTier) measuredAt;
        private int measuredHeight;
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

        internal int Prepare(CardComparison value, int width, double dpi, bool showDetails, Mode? mode, bool lowerTier = false)
        {
            if (displayed == value && measuredAt == (width, dpi, showDetails, mode, lowerTier))
                return measuredHeight;

            badge.MinHeight = 0;
            badge.Render(value, width / dpi, showDetails, mode, lowerTier);
            badge.InvalidateMeasure();
            badge.Measure(new Size(badge.Width, double.PositiveInfinity));
            displayed = value;
            measuredAt = (width, dpi, showDetails, mode, lowerTier);
            measuredHeight = (int)Math.Ceiling(badge.DesiredSize.Height * dpi) + 1;
            return measuredHeight;
        }

        internal void Display(Int32Rect target, double dpi, bool allowUpscale, double contentHeight)
        {
            badge.MinHeight = contentHeight;
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

    private sealed class BadgePanel : Border
    {
        private bool mayhem;
        private bool lowerTier;
        private Brush Divider => mayhem ? MayhemTierStyle.Divider : OverlayPanelStyle.Divider;
        private Brush Muted => mayhem ? MayhemTierStyle.Muted : OverlayPanelStyle.Muted;
        private Brush Gold => mayhem ? MayhemTierStyle.Gold : OverlayPanelStyle.Gold;
        private Brush Ink => mayhem ? MayhemTierStyle.Ink : OverlayPanelStyle.Ink;
        private static readonly Brush SPlusSurface = Paint("#FC493117");
        private static readonly Brush SPlusOutline = Paint("#FFD162");
        private static readonly Brush SPlusInk = Paint("#FFE393");
        private static readonly Brush SPlusMuted = Paint("#F2D8AE");
        private static readonly Brush SPlusDivider = Paint("#826133");
        private readonly StackPanel content = new();

        internal BadgePanel()
        {
            Child = content;
            ApplyFrame(sPlus: false, recommended: false);
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        }

        internal void Render(CardComparison value, double width, bool showDetails, Mode? mode, bool lowerTier = false)
        {
            Width = width;
            mayhem = mode == Mode.Caos;
            this.lowerTier = lowerTier;
            bool compactArena = mode == Mode.Arena && !showDetails;
            bool narrow = width < 150;
            Padding = compactArena ? new Thickness(6, 2, 6, 3) :
                narrow ? new Thickness(6, 5, 6, 6) : new Thickness(10, 5, 10, 6);
            content.Children.Clear();
            bool recommended = value.RecommendedUpgrade || value.RecommendedAugment || value.ArenaChoice?.Recommended == true;
            bool arena = value.IsArenaAugment || value.ArenaChoice is not null;
            bool variable = value.Local == "Variable";
            string? local = value.Local is CardComparison.Missing or "Variable" ? null : value.LocalText;
            var observed = value.DisplayedArenaPlacement;
            string? published = arena ? null : value.External?.Tier;
            bool sPlus = value.VisibleTierText == "S+";
            ApplyFrame(sPlus, recommended);
            if (lowerTier)
            {
                Background = Brushes.Transparent;
                BorderThickness = Padding = new Thickness(0);
            }
            if (value == CardComparison.Unread)
            {
                AddStatus("Leyendo carta…");
                return;
            }

            var context = value.UpgradeLevel is { } level ? $"Nv. {level}" :
                value.ArenaChoice?.TopPosition is { } position ? $"Top #{position}" : null;
            var recommendation = recommended ? (value.RecommendedUpgrade && showDetails ? "Mejora recomendada" : "Recomendado") : null;
            if (value.IsArenaVariable)
            {
                content.Children.Add(Line("Afinidad variable", 20, Ink, bold: true, centered: true));
                if (value.DetailText is { Length: > 0 } reason)
                    content.Children.Add(Line(reason, 15, Muted, top: 2, centered: true));
            }
            else if (published is { } tier)
                AddGrade(tier, "OP.GG", null, recommendation, narrow, localTier: local);
            else if (observed is not null)
                AddGrade(observed.Tier.ToString(), observed.Source, compactArena ? observed.ShortSample : observed.Sample,
                    compactArena && width < 170 && recommended ? "Mejor" : recommendation, narrow, compactArena: compactArena);
            else if (value.DisplayedMayhemObserved is { } mayhemObserved)
                AddGrade(mayhemObserved.Tier.ToString(), "AramHex · China", mayhemObserved.IsPrevious ? mayhemObserved.PatchText : null,
                    recommendation, narrow, localTier: local);
            else if (local is not null)
                AddGrade(local, "Local", context, recommendation, narrow, compactArena: compactArena);

            int headerLength = content.Children.Count;
            if (!value.IsArenaVariable && (showDetails || variable))
                AddDetail(value.ArenaChoice?.Reason ?? value.DetailText, narrow);
            if (showDetails)
            {
                if (arena)
                    AddArenaStats(value);
                else
                    AddMayhemStats(value, published is not null, local is not null, variable, narrow);
            }

            if (content.Children.Count > headerLength)
                AddDivider(headerLength > 0 ? headerLength : content.Children.Count, sPlus && !mayhem ? SPlusDivider : Divider);
            if (headerLength == 0)
                AddStatus(variable ? "Afinidad variable" : value.IsArenaAugment ? "Sin muestra suficiente" : arena ? "Sin tier local" :
                    value.Loading ? "Consultando OP.GG…" : "Sin muestra suficiente", context);
        }

        private void AddMayhemStats(CardComparison value, bool published, bool hasLocalTier, bool variable, bool narrow)
        {
            if (value.MayhemObserved is { } observed)
            {
                if (value.DisplayedMayhemObserved is null)
                    AddStat("AramHex · China", observed.Tier.ToString());
                content.Children.Add(Line($"{observed.Sample} · {observed.PatchText}", 11, Muted, top: 3));
            }
            if (published && variable)
                AddStat("Afinidad local", "Variable", Muted);
            if (!published && hasLocalTier && value.Loading)
                content.Children.Add(Line("Consultando OP.GG…", 11, Muted));
            if (value.Mayhem is not { } popularity)
                return;

            AddStat("Elección global", CardComparison.FormatPercent(popularity.Percent), size: narrow ? 11 : 12);
            content.Children.Add(Line(AramMayhemRatings.Attribution, 11, Muted, top: 2));
            if (popularity.IsPrevious)
                AddPreviousPatch(popularity.Patch);
        }

        private void ApplyFrame(bool sPlus, bool recommended)
        {
            if (mayhem)
            {
                CornerRadius = new CornerRadius(0);
                BorderThickness = new Thickness(2);
                Background = sPlus ? MayhemTierStyle.SPlusSurface : MayhemTierStyle.Surface;
                BorderBrush = recommended || sPlus ? MayhemTierStyle.AccentFrame : MayhemTierStyle.Frame;
                TextElement.SetFontFamily(this, MayhemTierStyle.Body);
            }
            else
            {
                CornerRadius = new CornerRadius(4);
                BorderThickness = new Thickness(2, 3, 2, 2);
                Background = sPlus ? SPlusSurface : OverlayPanelStyle.Surface;
                BorderBrush = sPlus ? SPlusOutline : recommended ? Gold : OverlayPanelStyle.Frame;
                ClearValue(TextElement.FontFamilyProperty);
            }
        }

        private void AddArenaStats(CardComparison value)
        {
            if (value.ArenaPlacement is { } observed)
            {
                if (value.DisplayedArenaPlacement is null)
                    AddStat(observed.Source, observed.Tier.ToString());
                else if (value.Local != CardComparison.Missing)
                    AddStat("Estimación local", value.LocalText);
                AddStat("Posición media", observed.AveragePlaceText);
                content.Children.Add(Line($"{observed.Sample} · Parche {observed.Patch}", 11, Muted, top: 2));
            }
            if (value.UsesWiki)
                content.Children.Add(Line("Mecánica de LoL Wiki", 11, Muted, top: 3));

            if (value.External?.PickPercent is not null || value.Arena is not null)
                content.Children.Add(Line("Frecuencia de elección", 11, Muted, bold: true, top: 5));
            if (value.External?.PickPercent is { } pick)
            {
                AddStat("OP.GG", CardComparison.FormatPercent(pick));
                if (value.ExternalPreviousPatch is { } patch)
                    AddPreviousPatch(patch);
            }
            else if (value.Loading)
                content.Children.Add(Line("Consultando OP.GG…", 12, Muted, top: 4));
            if (value.Arena is { } arena)
            {
                AddStat("GGEZ.GG", CardComparison.FormatPercent(arena.Percent));
                bool previous = arena.IsPrevious;
                content.Children.Add(Line(previous ? arena.SampleText : $"{arena.SampleText} · {arena.Patch}", 11, Muted, top: 2));
                if (previous)
                    AddPreviousPatch(arena.Patch);
            }
        }

        private void AddGrade(string tier, string label, string? context, string? recommendation, bool narrow,
            string? localTier = null, bool compactArena = false)
        {
            if (lowerTier)
            {
                AddLowerTier(tier, label, context, recommendation);
                return;
            }

            double size = narrow ? 38 : 46;
            var grade = Line(tier, size, TierInk(tier), centered: true);
            grade.FontFamily = mayhem ? MayhemTierStyle.Heading : OverlayPanelStyle.Heading;
            grade.FontWeight = FontWeights.Bold;
            grade.TextWrapping = TextWrapping.NoWrap;
            if (compactArena)
            {
                grade.LineHeight = size;
                grade.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            }
            if (tier == "S+")
            {
                grade.Inlines.Clear();
                grade.Inlines.Add(new Run("S"));
                grade.Inlines.Add(new Run("+") { FontSize = size / 2, BaselineAlignment = BaselineAlignment.Superscript });
            }
            content.Children.Add(grade);
            if (compactArena)
            {
                var metadata = Line(context is null ? label : $"{label} · {context}",
                    context is not null && recommendation is not null ? 11 : 12,
                    Ink, bold: true, centered: true);
                if (recommendation is not null)
                {
                    metadata.Inlines.Add(new Run(" · "));
                    metadata.Inlines.Add(new Run(recommendation) { Foreground = Gold });
                }
                content.Children.Add(metadata);
                return;
            }
            content.Children.Add(Line(label, 13, Ink, bold: true, centered: true));
            if (localTier is not null)
            {
                var local = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
                var caption = Line("Local", 12, Muted);
                caption.VerticalAlignment = VerticalAlignment.Center;
                var letter = Line(localTier, 16, TierInk(localTier), bold: true);
                if (mayhem)
                    letter.FontFamily = MayhemTierStyle.Heading;
                letter.Margin = new Thickness(4, 0, 0, 0);
                local.Children.Add(caption);
                local.Children.Add(letter);
                content.Children.Add(local);
            }
            if (context is not null)
                content.Children.Add(Line(context, 12, tier == "S+" && !mayhem ? SPlusMuted : Muted, centered: true));
            if (recommendation is not null)
                content.Children.Add(Line(recommendation, narrow ? 11 : 12, Gold, bold: true, top: 2, centered: true));
        }

        private void AddLowerTier(string tier, string label, string? context, string? recommendation)
        {
            bool showSource = !mayhem || context is not null || label == "AramHex · China";
            string? caption = showSource ? context is null ? label : $"{label} · {context}" : null;
            if (caption is not null || recommendation is not null)
            {
                var metadata = Line(caption ?? recommendation!, 12, caption is null ? Gold : Ink, bold: true, centered: true);
                if (caption is not null && recommendation is not null)
                {
                    metadata.Inlines.Add(new Run(" · "));
                    metadata.Inlines.Add(new Run(recommendation) { Foreground = Gold });
                }
                content.Children.Add(new Border
                {
                    Background = mayhem ? MayhemTierStyle.Surface : OverlayPanelStyle.Surface,
                    BorderBrush = Divider, BorderThickness = new Thickness(1), Padding = new Thickness(5, 1, 5, 1),
                    Margin = new Thickness(0, 0, 0, 2), HorizontalAlignment = HorizontalAlignment.Center,
                    Child = metadata
                });
            }
            content.Children.Add(TierHexagon.Create(tier, CompactTierWidth, CompactTierHeight, CompactTierFontSize));
        }

        private void AddStatus(string text, string? context = null)
        {
            var header = new StackPanel { Margin = new Thickness(4, 7, 4, 7) };
            header.Children.Add(Line(text, 14, Muted, bold: true));
            if (context is not null)
                header.Children.Add(Line(context, 11, Muted));
            content.Children.Add(header);
        }

        private void AddDetail(string text, bool compact)
        {
            if (!string.IsNullOrEmpty(text))
                content.Children.Add(Line(text, compact ? 11 : 12, Ink, top: 4));
        }

        private void AddDivider(int index, Brush ink) =>
            content.Children.Insert(index, new Border { Height = 1, Background = ink, Margin = new Thickness(0, 6, 0, 5) });

        private void AddStat(string label, string value, Brush? ink = null, double size = 12)
        {
            var row = new Grid { Margin = new Thickness(0, 2, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(Line(label, size, Muted));
            var result = Line(value, size, ink ?? Ink, bold: true);
            result.Margin = new Thickness(4, 0, 0, 0);
            result.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(result, 1);
            row.Children.Add(result);
            content.Children.Add(row);
        }

        private void AddPreviousPatch(string patch) =>
            content.Children.Add(Line($"Parche anterior {patch}", 11, Gold, top: 2));

        private static TextBlock Line(string text, double size, Brush ink, bool bold = false, double top = 0, bool centered = false) => new()
        {
            Text = text, FontSize = size, Foreground = ink, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            Margin = new Thickness(0, top, 0, 0), TextWrapping = TextWrapping.Wrap,
            TextAlignment = centered ? TextAlignment.Center : TextAlignment.Left
        };

        private Brush TierInk(string tier) => mayhem ? MayhemTierStyle.Tier(tier) :
            tier == "S+" ? SPlusInk : OverlayPanelStyle.Tier(tier);
    }
}

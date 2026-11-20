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
    private const int SummaryWidth = 160;
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
    private readonly BadgeWindow[] badges = Enumerable.Range(1, 4).Select(slot => new BadgeWindow(slot)).ToArray();
    private BuildSizeButton? itemSizeButton;
    private OverlayUpdate? displayedItems;
    private (nint Target, Calibration Layout, bool UpgradeHeading, bool ItemOffer)? arenaPlacement;
    private int? arenaCardEdge;
    private int itemSizePercent = BuildSizePreference.Default;

    internal bool ShowDetails { private get; set; }
    internal event Action? ItemSizeRequested;
    internal IReadOnlyList<Window> Windows => badges;
    internal bool IsVisible => badges.Any(badge => badge.NativeVisible);

    internal void Hide()
    {
        displayedItems = null;
        arenaPlacement = null;
        arenaCardEdge = null;
        itemSizeButton?.Hide();
        foreach (var badge in badges)
            badge.HideBadge();
    }

    internal void Close()
    {
        itemSizeButton?.Close();
        foreach (var badge in badges)
            badge.Close();
    }

    internal void SetItemTierSize(int percent)
    {
        int updated = Math.Clamp(percent, BuildSizePreference.Minimum, BuildSizePreference.Maximum);
        if (updated == itemSizePercent)
            return;
        itemSizePercent = updated;
        if (displayedItems is { } view && IsVisible &&
            (view.Target == 0 || Native.GetForegroundWindow() == view.Target))
            Render(view);
    }

    internal FrameworkElement CreateItemTierPreview()
    {
        var badge = new BadgePanel();
        var example = new CardComparison("A", ArenaChoice: new("Reverberación", Tier.A,
            "Inmovilizar activa impactos", Recommended: true, TopPosition: 4));
        badge.Render(example, ShowDetails ? DetailWidth : SummaryWidth, ShowDetails, !ShowDetails);
        // Keep the baseline transform separate from the slider's layout transform.
        badge.LayoutTransform = new ScaleTransform(ArenaItemDefaultScale, ArenaItemDefaultScale);
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
        int maximumWidth = ShowDetails ? DetailWidth : SummaryWidth;
        int widthLimit = Math.Clamp(titles.Min(title => title.Width), 1, bounds.Width);
        int width = Math.Min(widthLimit, Math.Max(1, (int)(maximumWidth * dpi)));
        int margin = (int)Math.Ceiling(6 * dpi);
        bool arenaCards = update.Mode == Mode.Arena;
        bool arenaItems = arenaCards && update.ArenaItemOffer;
        displayedItems = arenaItems ? update : null;
        double itemScale = arenaItems ? Math.Min(ArenaItemDefaultScale * itemSizePercent / 100.0, (double)widthLimit / width) : 1;
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
            int minimumTop = margin;
            if (arenaCards && title.X < bounds.Width / 2 && title.X + title.Width > bounds.Width / 2)
            {
                var headerBottom = update.ArenaUpgradeHeading ? ArenaUpgradeHeadingBottom : ArenaRoundClockBottom;
                minimumTop = Math.Max(margin, (int)Math.Ceiling(bounds.Height * headerBottom));
            }
            int roomAbove = anchor - minimumTop;
            int heightLimit = arenaCards ? 2 * Math.Min(roomAbove, maxOverlap) : roomAbove + Math.Min(roomAbove, maxOverlap);
            if (heightLimit <= 0)
            {
                badge.HideBadge();
                continue;
            }

            int badgeWidth = !ShowDetails && label.DisplayedArenaPlacement is not null
                ? Math.Min(widthLimit, Math.Max(1, (int)(ArenaEvidenceWidth * dpi))) : width;
            int desiredHeight = badge.Prepare(label, badgeWidth, dpi, ShowDetails, arenaCards && !ShowDetails);
            int height = Math.Min((int)Math.Ceiling(desiredHeight * itemScale), heightLimit);
            int bottom = anchor + Math.Min(height / 2, maxOverlap);
            int fittedWidth = Math.Clamp(badgeWidth * height / desiredHeight, 1, widthLimit);
            int x = bounds.X + Math.Clamp(center - fittedWidth / 2, 0, bounds.Width - fittedWidth);
            badge.Display(new(x, bounds.Y + bottom - height, fittedWidth, height), dpi, allowUpscale: arenaItems);
        }

        if (arenaItems && cardEdge is { } edge && ItemSizeRequested is not null && IsVisible)
            ShowItemSizeButton(bounds, titles, edge, margin, dpi);
        else
            itemSizeButton?.Hide();
    }

    private void ShowItemSizeButton(Int32Rect bounds, Int32Rect[] titles, int cardEdge, int gap, double dpi)
    {
        int width = (int)Math.Ceiling(BuildSizeButton.ButtonWidth * dpi);
        int height = (int)Math.Ceiling(BuildSizeButton.ButtonHeight * dpi);
        int x = titles.Max(title => title.X + title.Width) + gap;
        int y = cardEdge - height - gap;
        if (x + width + gap > bounds.Width || y < gap)
        {
            itemSizeButton?.Hide();
            return;
        }

        itemSizeButton ??= new BuildSizeButton(() => ItemSizeRequested?.Invoke(), "Tamaño de los tiers de objetos");
        var owner = badges.First(badge => badge.NativeVisible);
        if (itemSizeButton.Owner != owner)
            itemSizeButton.Owner = owner;
        itemSizeButton.Display(new Int32Rect(bounds.X + x, bounds.Y + y, width, height), dpi);
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
        private (int Width, double Dpi, bool ShowDetails, bool CompactArena) measuredAt;
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

        internal int Prepare(CardComparison value, int width, double dpi, bool showDetails, bool compactArena)
        {
            if (displayed == value && measuredAt == (width, dpi, showDetails, compactArena))
                return measuredHeight;

            badge.Render(value, width / dpi, showDetails, compactArena);
            badge.InvalidateMeasure();
            badge.Measure(new Size(badge.Width, double.PositiveInfinity));
            displayed = value;
            measuredAt = (width, dpi, showDetails, compactArena);
            measuredHeight = (int)Math.Ceiling(badge.DesiredSize.Height * dpi) + 1;
            return measuredHeight;
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

    private sealed class BadgePanel : Border
    {
        private static readonly Brush Surface = Paint("#FA172436");
        private static readonly Brush Outline = Paint("#586F8D");
        private static readonly Brush Divider = Paint("#3D506C");
        private static readonly Brush Muted = Paint("#B9C8DD");
        private static readonly Brush Gold = Paint("#FFD888");
        private static readonly Brush SPlusSurface = Paint("#FC493117");
        private static readonly Brush SPlusOutline = Paint("#FFD162");
        private static readonly Brush SPlusInk = Paint("#FFE393");
        private static readonly Brush SPlusMuted = Paint("#F2D8AE");
        private static readonly Brush SPlusDivider = Paint("#826133");
        private static readonly Brush Green = Paint("#A0EACD");
        private static readonly Brush Blue = Paint("#BFD1FF");
        private static readonly Brush NeutralGrade = Paint("#DCE4F2");
        private readonly StackPanel content = new();

        internal BadgePanel()
        {
            CornerRadius = new CornerRadius(10);
            Background = Surface;
            BorderThickness = new Thickness(1);
            Child = content;
        }

        internal void Render(CardComparison value, double width, bool showDetails, bool compactArena)
        {
            Width = width;
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
            Background = sPlus ? SPlusSurface : Surface;
            BorderBrush = sPlus ? SPlusOutline : recommended ? Gold : Outline;
            BorderThickness = new Thickness(sPlus ? 2 : 1);
            if (value == CardComparison.Unread)
            {
                AddStatus("Leyendo carta…");
                return;
            }

            var context = value.UpgradeLevel is { } level ? $"Nv. {level}" :
                value.ArenaChoice?.TopPosition is { } position ? $"Top #{position}" : null;
            var recommendation = recommended ? (value.RecommendedUpgrade && showDetails ? "Mejora recomendada" : "Recomendado") : null;
            if (published is { } tier)
                AddGrade(tier, "OP.GG", null, null, narrow, localTier: local);
            else if (observed is not null)
                AddGrade(observed.Tier.ToString(), observed.Source, compactArena ? observed.ShortSample : observed.Sample,
                    compactArena && width < 170 && recommended ? "Mejor" : recommendation, narrow, compactArena: compactArena);
            else if (value.DisplayedMayhemObserved is { } mayhemObserved)
                AddGrade(mayhemObserved.Tier.ToString(), "AramHex · China", mayhemObserved.IsPrevious ? mayhemObserved.PatchText : null,
                    null, narrow, localTier: local);
            else if (local is not null)
                AddGrade(local, "Local", context, recommendation, narrow, compactArena: compactArena);

            int headerLength = content.Children.Count;
            if (showDetails || variable)
                AddDetail(value.ArenaChoice?.Reason ?? value.DetailText, narrow);
            if (showDetails)
            {
                if (arena)
                    AddArenaStats(value);
                else
                    AddMayhemStats(value, published is not null, local is not null, variable, narrow);
            }

            if (content.Children.Count > headerLength)
                AddDivider(headerLength > 0 ? headerLength : content.Children.Count, sPlus ? SPlusDivider : Divider);
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
            double size = narrow ? 38 : 46;
            var grade = Line(tier, size, TierInk(tier), centered: true);
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
                    Brushes.WhiteSmoke, bold: true, centered: true);
                if (recommendation is not null)
                {
                    metadata.Inlines.Add(new Run(" · "));
                    metadata.Inlines.Add(new Run(recommendation) { Foreground = Gold });
                }
                content.Children.Add(metadata);
                return;
            }
            content.Children.Add(Line(label, 13, Brushes.WhiteSmoke, bold: true, centered: true));
            if (localTier is not null)
            {
                var local = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
                var caption = Line("Local", 12, Muted);
                caption.VerticalAlignment = VerticalAlignment.Center;
                var letter = Line(localTier, 16, Brushes.WhiteSmoke, bold: true);
                letter.Margin = new Thickness(4, 0, 0, 0);
                local.Children.Add(caption);
                local.Children.Add(letter);
                content.Children.Add(local);
            }
            if (context is not null)
                content.Children.Add(Line(context, 12, tier == "S+" ? SPlusMuted : Muted, centered: true));
            if (recommendation is not null)
                content.Children.Add(Line(recommendation, narrow ? 11 : 12, Gold, bold: true, top: 2, centered: true));
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
                content.Children.Add(Line(text, compact ? 11 : 12, Brushes.WhiteSmoke, top: 4));
        }

        private void AddDivider(int index, Brush ink) =>
            content.Children.Insert(index, new Border { Height = 1, Background = ink, Margin = new Thickness(0, 6, 0, 5) });

        private void AddStat(string label, string value, Brush? ink = null, double size = 12)
        {
            var row = new Grid { Margin = new Thickness(0, 2, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(Line(label, size, Muted));
            var result = Line(value, size, ink ?? Brushes.WhiteSmoke, bold: true);
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

        private static Brush TierInk(string tier) => tier switch
        {
            "S+" => SPlusInk, "S" => Gold, "A" => Green, "B" => Blue, _ => NeutralGrade
        };
    }
}

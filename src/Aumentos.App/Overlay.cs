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
    private const int DetailWidth = 260;
    private readonly BadgeWindow[] badges = Enumerable.Range(1, 4).Select(slot => new BadgeWindow(slot)).ToArray();
    internal bool ShowDetails { private get; set; }
    internal IReadOnlyList<Window> Windows => badges;
    internal bool IsVisible => badges.Any(badge => badge.NativeVisible);
    internal void Hide() { foreach (var badge in badges) badge.HideBadge(); }
    internal void Close() { foreach (var badge in badges) badge.Close(); }

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
        int width = Math.Clamp(titles.Min(title => title.Width), 1, Math.Min((int)(maximumWidth * dpi), bounds.Width));
        int margin = (int)Math.Ceiling(6 * dpi);

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
            int bottom = title.Y - gap;
            if (bottom <= margin)
            {
                badge.HideBadge();
                continue;
            }

            int desiredHeight = badge.Prepare(label, width, dpi, ShowDetails);
            int height = Math.Min(desiredHeight, bottom - margin);
            int fittedWidth = Math.Max(1, width * height / desiredHeight);
            int center = bounds.X + title.X + title.Width / 2;
            int x = Math.Clamp(center - fittedWidth / 2, bounds.X, bounds.X + bounds.Width - fittedWidth);
            badge.Display(new(x, bounds.Y + bottom - height, fittedWidth, height), dpi);
        }
    }

    internal static Brush Paint(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    private sealed class BadgeWindow : Window
    {
        private static readonly Brush Surface = Paint("#FA172436");
        private static readonly Brush Outline = Paint("#586F8D");
        private static readonly Brush Divider = Paint("#3D506C");
        private static readonly Brush Muted = Paint("#B9C8DD");
        private static readonly Brush Gold = Paint("#FFD888");
        private static readonly Brush Green = Paint("#A0EACD");
        private static readonly Brush Blue = Paint("#BFD1FF");
        private static readonly Brush NeutralGrade = Paint("#DCE4F2");

        private readonly StackPanel content = new();
        private readonly Border badge;
        private CardComparison? displayed;
        private (int Width, double Dpi, bool ShowDetails) measuredAt;
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
            badge = new Border
            {
                CornerRadius = new CornerRadius(10), Background = Surface, BorderThickness = new Thickness(1), Child = content
            };
            Content = new Viewbox { Child = badge, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly };
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

        internal int Prepare(CardComparison value, int width, double dpi, bool showDetails)
        {
            if (displayed == value && measuredAt == (width, dpi, showDetails))
                return measuredHeight;

            badge.Width = width / dpi;
            bool narrow = badge.Width < 150;
            badge.Padding = narrow ? new Thickness(6, 5, 6, 6) : new Thickness(10, 5, 10, 6);
            RenderContent(value, showDetails, narrow);
            badge.InvalidateMeasure();
            badge.Measure(new Size(badge.Width, double.PositiveInfinity));
            displayed = value;
            measuredAt = (width, dpi, showDetails);
            measuredHeight = (int)Math.Ceiling(badge.DesiredSize.Height * dpi) + 1;
            return measuredHeight;
        }

        internal void Display(Int32Rect target, double dpi)
        {
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

        private void RenderContent(CardComparison value, bool showDetails, bool narrow)
        {
            content.Children.Clear();
            bool recommended = value.RecommendedUpgrade || value.RecommendedAugment || value.ArenaChoice?.Recommended == true;
            badge.BorderBrush = recommended ? Gold : Outline;
            if (value == CardComparison.Unread)
            {
                AddStatus("Leyendo carta…");
                return;
            }

            bool arena = value.IsArenaAugment || value.ArenaChoice is not null;
            bool variable = value.Local == "Variable";
            string? local = value.Local is CardComparison.Missing or "Variable" ? null : value.LocalText;
            string? published = arena ? null : value.External?.Tier;
            if (showDetails || variable)
                AddDetail(value.ArenaChoice?.Reason ?? value.DetailText, narrow);
            if (showDetails)
            {
                if (arena)
                    AddArenaStats(value);
                else
                    AddMayhemStats(value, published is not null, local is not null, variable, narrow);
            }

            AddDivider();
            var context = value.UpgradeLevel is { } level ? $"Nv. {level}" :
                value.ArenaChoice?.TopPosition is { } position ? $"Top #{position}" : null;
            var recommendation = recommended ? (value.RecommendedUpgrade && showDetails ? "Mejora recomendada" : "Recomendado") : null;
            if (published is { } tier)
                AddGrade(tier, "OP.GG", null, null, narrow, localTier: local);
            else if (local is not null)
                AddGrade(local, "Local", context, recommendation, narrow);
            else
                AddStatus(variable ? "Afinidad variable" : arena ? "Sin tier local" :
                    value.Loading ? "Consultando OP.GG…" : "Sin tier", context);
        }

        private void AddMayhemStats(CardComparison value, bool published, bool hasLocalTier, bool variable, bool narrow)
        {
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

        private void AddGrade(string tier, string label, string? context, string? recommendation, bool narrow, string? localTier = null)
        {
            var header = new DockPanel { MinHeight = narrow ? 48 : 56 };
            var grade = Line(tier, narrow ? 38 : 46, TierInk(tier), bold: true);
            grade.FontWeight = FontWeights.Bold;
            grade.TextWrapping = TextWrapping.NoWrap;
            grade.TextAlignment = TextAlignment.Center;
            grade.Width = narrow ? 49 : 59;
            grade.Margin = new Thickness(0, 0, narrow ? 5 : 8, 0);
            grade.VerticalAlignment = VerticalAlignment.Center;
            if (tier == "S+")
            {
                grade.Inlines.Clear();
                grade.Inlines.Add(new Run("S"));
                grade.Inlines.Add(new Run("+") { FontSize = narrow ? 19 : 23, BaselineAlignment = BaselineAlignment.Superscript });
            }
            header.Children.Add(grade);

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(Line(label, 13, Brushes.WhiteSmoke, bold: true));
            if (localTier is not null)
            {
                var local = new StackPanel { Orientation = Orientation.Horizontal };
                var caption = Line("Local", 12, Muted);
                caption.VerticalAlignment = VerticalAlignment.Center;
                var letter = Line(localTier, 16, Brushes.WhiteSmoke, bold: true);
                letter.Margin = new Thickness(4, 0, 0, 0);
                local.Children.Add(caption);
                local.Children.Add(letter);
                text.Children.Add(local);
            }
            if (context is not null)
                text.Children.Add(Line(context, 12, Muted));
            if (recommendation is not null)
                text.Children.Add(Line(recommendation, narrow ? 11 : 12, Gold, bold: true, top: 2));
            header.Children.Add(text);
            content.Children.Add(header);
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

        private void AddDivider()
        {
            if (content.Children.Count > 0)
                content.Children.Add(new Border { Height = 1, Background = Divider, Margin = new Thickness(0, 6, 0, 5) });
        }

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

        private static TextBlock Line(string text, double size, Brush ink, bool bold = false, double top = 0) => new()
        {
            Text = text, FontSize = size, Foreground = ink, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            Margin = new Thickness(0, top, 0, 0), TextWrapping = TextWrapping.Wrap
        };

        private static Brush TierInk(string tier) => tier switch
        {
            "S" or "S+" => Gold, "A" => Green, "B" => Blue, _ => NeutralGrade
        };
    }
}

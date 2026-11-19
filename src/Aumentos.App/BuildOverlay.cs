using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace Aumentos;

internal sealed record PrismaticDisplay(ArenaPrismaticTop Top, Calibration Layout, string Patch);
internal sealed record BuildDisplay(Int32Rect Bounds, nint Target, string Champion, Mode Mode, OpGgBuildView? Source,
    Int32Rect? Shop = null, MayhemBuildPlan? Plan = null, PrismaticDisplay? Prismatics = null,
    string? ClientPatch = null, bool ArenaRivalsDetected = false);

internal sealed class BuildVisibility
{
    private enum Preference { Automatic, Forced, Dismissed }

    private readonly object gate = new();
    private Preference preference;
    private bool shopOpen;

    internal void ObserveShop(bool open)
    {
        lock (gate)
        {
            shopOpen = open;
            if (!open && preference == Preference.Dismissed)
                preference = Preference.Automatic;
        }
    }

    internal bool ShouldShow(bool shopInFrame)
    {
        lock (gate)
            return preference == Preference.Forced || (preference == Preference.Automatic && shopInFrame);
    }

    internal bool Toggle()
    {
        lock (gate)
        {
            bool visible = preference == Preference.Forced || (preference == Preference.Automatic && shopOpen);
            preference = shopOpen
                ? (visible ? Preference.Dismissed : Preference.Automatic)
                : (visible ? Preference.Automatic : Preference.Forced);
            return !visible;
        }
    }
}

internal sealed class BuildOverlay : Window
{
    private const int HotkeyId = 1;
    private const int HotkeyMessage = 0x0312;
    private const uint CtrlAltNoRepeat = 0x4003;
    private const uint KeyB = 0x42;
    private const double PanelWidth = 320;

    private static readonly Brush Muted = Overlay.Paint("#B1C1D8");
    private static readonly Brush Accent = Overlay.Paint("#AAD1FF");
    private static readonly Brush PreviousPatch = Overlay.Paint("#FFD76D");
    private static readonly Brush PurchaseAccent = Overlay.Paint("#F6D28C");
    private static readonly Brush PurchaseSurface = Overlay.Paint("#30333A");
    private readonly StackPanel content = new();
    private readonly Border panel;
    private readonly BuildItemIcons? itemIcons;
    private BuildDisplay? displayed;
    private Int32Rect placement;
    private nint handle;
    private HwndSource? hotkeySource;
    private Action? onShortcut;

    internal BuildOverlay(BuildItemIcons? itemIcons = null)
    {
        this.itemIcons = itemIcons;
        Title = "Aumentos LoL · Recomendaciones de objetos";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        ShowActivated = false;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        Focusable = false;
        IsHitTestVisible = false;
        Background = Brushes.Transparent;
        UseLayoutRounding = true;

        panel = new Border
        {
            Width = PanelWidth,
            Padding = new Thickness(16, 12, 16, 14),
            CornerRadius = new CornerRadius(9),
            BorderBrush = Overlay.Paint("#667C9F"),
            BorderThickness = new Thickness(1),
            Background = Overlay.Paint("#F2172436"),
            Child = content
        };
        Content = new Viewbox { Child = panel, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly };
        SourceInitialized += (_, _) =>
        {
            handle = new WindowInteropHelper(this).Handle;
            const int exStyle = -20;
            const int noActivateToolWindow = 0x080000A0;
            Native.SetWindowLongW(handle, exStyle, Native.GetWindowLongW(handle, exStyle) | noActivateToolWindow);
        };
    }

    internal bool RegisterShortcut(Action toggle)
    {
        if (hotkeySource is not null)
        {
            onShortcut = toggle;
            return true;
        }

        var window = new WindowInteropHelper(this).EnsureHandle();
        var source = HwndSource.FromHwnd(window);
        if (source is null || !RegisterHotKey(window, HotkeyId, CtrlAltNoRepeat, KeyB))
            return false;
        hotkeySource = source;
        onShortcut = toggle;
        source.AddHook(OnWindowMessage);
        return true;
    }

    protected override void OnClosed(EventArgs e)
    {
        if (hotkeySource is { } source)
        {
            UnregisterHotKey(handle, HotkeyId);
            if (!source.IsDisposed)
                source.RemoveHook(OnWindowMessage);
        }
        onShortcut = null;
        hotkeySource = null;
        base.OnClosed(e);
    }

    private nint OnWindowMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == HotkeyMessage && wParam == HotkeyId)
        {
            handled = true;
            onShortcut?.Invoke();
        }
        return 0;
    }

    internal void Render(BuildDisplay? view)
    {
        if (view is null || view.Bounds.Width <= 0 || view.Bounds.Height <= 0)
        {
            if (IsVisible)
                Hide();
            displayed = null;
            return;
        }

        if (displayed is null || displayed.Champion != view.Champion || displayed.Mode != view.Mode ||
            displayed.Source != view.Source || displayed.Plan != view.Plan || displayed.Prismatics != view.Prismatics ||
            displayed.ClientPatch != view.ClientPatch || displayed.ArenaRivalsDetected != view.ArenaRivalsDetected)
            RenderContent(view);
        displayed = view;

        var bounds = view.Bounds;
        var targetDpi = view.Target == 0 ? 0 : Native.GetDpiForWindow(view.Target);
        double dpi = targetDpi > 0 ? targetDpi / 96.0 : VisualTreeHelper.GetDpi(this).DpiScaleX;
        int margin = Math.Min((int)Math.Round(14 * dpi), Math.Min(bounds.Width, bounds.Height) / 4);
        int maxWidth = bounds.Width - 2 * margin, maxHeight = bounds.Height - 2 * margin;
        bool placeLeft = false;
        if (view.Prismatics is { Layout: var layout })
        {
            var titles = layout.Titles.Select(region => region.Pixels(bounds.Width, bounds.Height)).ToArray();
            // Los recortes del OCR cubren solo el texto. Deja espacio para los bordes y el resplandor.
            int padding = bounds.Width / 24;
            int leftSpace = titles.Min(title => title.X) - padding - 2 * margin;
            int rightSpace = bounds.Width - titles.Max(title => title.X + title.Width) - padding - 2 * margin;
            int desiredWidth = (int)Math.Ceiling(PanelWidth * dpi);
            placeLeft = leftSpace >= desiredWidth || leftSpace >= rightSpace;
            maxWidth = Math.Min(maxWidth, Math.Max(1, placeLeft ? leftSpace : rightSpace));
        }
        else if (view.Shop is { } shop)
        {
            int leftSpace = shop.X - shop.Width / 12 - 2 * margin;
            int rightSpace = bounds.Width - shop.X - shop.Width - 2 * margin;
            int desiredWidth = (int)Math.Ceiling(PanelWidth * dpi);
            placeLeft = rightSpace < desiredWidth && leftSpace > rightSpace && leftSpace >= desiredWidth * 3 / 4;
            int sideSpace = placeLeft ? leftSpace : rightSpace;
            if (sideSpace >= desiredWidth * 3 / 4)
                maxWidth = Math.Min(maxWidth, sideSpace);
        }

        panel.Measure(new Size(PanelWidth, double.PositiveInfinity));
        var desired = panel.DesiredSize;
        double scale = Math.Min(dpi, Math.Min(maxWidth / desired.Width, maxHeight / desired.Height));
        int width = Math.Clamp((int)Math.Ceiling(desired.Width * scale), 1, maxWidth);
        int height = Math.Clamp((int)Math.Ceiling(desired.Height * scale), 1, maxHeight);
        var target = new Int32Rect(bounds.X + (placeLeft ? margin : bounds.Width - margin - width),
            bounds.Y + Math.Clamp((int)(bounds.Height * 0.12), margin, bounds.Height - margin - height), width, height);

        if (IsVisible && Native.IsWindowVisible(handle) && target == placement)
            return;
        Width = width / dpi;
        Height = height / dpi;
        if (!IsVisible)
            Show();
        const uint showWithoutActivation = 0x0040 | 0x0010;
        if (Native.SetWindowPos(handle, -1, target.X, target.Y, target.Width, target.Height, showWithoutActivation))
            placement = target;
    }

    internal void RenderContent(BuildDisplay view)
    {
        content.Children.Clear();
        if (view.Prismatics is { } prismatics)
        {
            RenderPrismatics(view.Champion, prismatics);
            return;
        }

        var build = view.Source?.Data;
        var mode = view.Mode == Mode.Caos ? "ARAM Caos" : "Arena";
        var patch = build is null ? mode :
            $"{mode} · {(build.IsPrevious ? "Parche anterior" : "Parche")} {LeaguePatch.Public(build.SourcePatch)}";
        AddLine($"{view.Champion} · {(view.Plan is null ? "OP.GG" : "Build en partida")}", 17, Brushes.WhiteSmoke, bold: true);
        AddLine(view.Plan is not null && build is not null ? $"{patch} · Base OP.GG" : patch,
            12, build?.IsPrevious == true ? PreviousPatch : Muted);

        if (view.Mode == Mode.Arena && view.Plan is { } arenaPlan)
        {
            AddLine(view.ArenaRivalsDetected ? "Rivales de rondas jugadas" : "Según tu inventario y campeón", 11, Muted);
            AddArenaRoute(arenaPlan, view.ClientPatch ?? build?.ClientPatch ?? "");
            if (build?.Groups.FirstOrDefault(group => group.Section == OpGgBuildSection.Prismatic) is { } prismaticsGroup)
            {
                AddLine("Referencia de prismáticos · OP.GG", 12, Accent, bold: true, top: 9);
                AddItems(prismaticsGroup.Choices.SelectMany(choice => choice).DistinctBy(item => item.Id).Take(3).ToArray(),
                    build.SourcePatch, ordered: false);
            }
            return;
        }

        if (build is null || build.Groups.Length == 0)
        {
            if (view.Source is { } source)
                AddLine(source.Status, 13, Brushes.WhiteSmoke, top: 9);
            return;
        }

        foreach (var group in build.Groups)
        {
            if (group.Section == OpGgBuildSection.Core && view.Plan is { } plan)
            {
                AddRoute(plan, build.ClientPatch);
                if (group.Choices.ElementAtOrDefault(1) is { } alternative)
                {
                    AddLine("Núcleo alternativo · OP.GG", 11, Muted, top: 5);
                    AddItems(alternative, build.SourcePatch, ordered: true);
                }
                continue;
            }
            AddLine(SectionLabel(group.Section), 12, Accent, bold: true, top: 9);
            if ((group.Section is OpGgBuildSection.Boots or OpGgBuildSection.Prismatic) &&
                group.Choices.All(choice => choice.Length == 1))
            {
                AddItems(group.Choices.Select(choice => choice[0]).ToArray(), build.SourcePatch, ordered: false);
                continue;
            }
            foreach (var (choice, index) in group.Choices.Take(2).Select((items, index) => (items, index)))
            {
                if (index == 1)
                    AddLine("Alternativa", 11, Muted, top: 4);
                AddItems(choice, build.SourcePatch, ordered: group.Section == OpGgBuildSection.Core);
            }
        }
    }

    private void AddRoute(MayhemBuildPlan plan, string patch)
    {
        AddLine("Ruta a 6 objetos", 12, Accent, bold: true, top: 9);
        foreach (var row in plan.Steps.Chunk(3))
            AddItems(row.Select(step => step.Item).ToArray(), patch, ordered: true,
                details: row.Select(step => step.Detail).ToArray());
    }

    private void AddArenaRoute(MayhemBuildPlan plan, string patch)
    {
        var byPurchase = plan.Steps.ToLookup(step => step.Owned);
        var purchased = byPurchase[true].ToArray();
        var pending = byPurchase[false].ToArray();
        if (pending.Length > 0)
        {
            AddLine($"Por comprar · {pending.Length}", 12, Accent, bold: true, top: 9);
            AddArenaPurchase(pending[0], patch, 1);
            if (pending.Length > 1)
                AddLine("Después", 12, Accent, bold: true, top: 8);
            for (int index = 1; index < pending.Length; index++)
                AddArenaPurchase(pending[index], patch, index + 1);
        }
        else if (purchased.Length == 6)
            AddLine("Build completa", 14, Accent, bold: true, top: 9);

        AddLine($"Comprados · {purchased.Length}/6", 12, Muted, bold: true, top: 9);
        foreach (var row in purchased.Chunk(3))
            AddItems(row.Select(step => step.Item).ToArray(), patch, ordered: false);
    }

    private void AddArenaPurchase(BuildStep step, string patch, int position)
    {
        bool next = position == 1;
        var row = new DockPanel { Margin = next ? new Thickness(0) : new Thickness(0, 6, 0, 0) };
        if (!next)
        {
            var order = new TextBlock { Text = $"{position}.", Width = 20, Foreground = Accent, FontSize = 12 };
            DockPanel.SetDock(order, Dock.Left);
            row.Children.Add(order);
        }

        var icon = new Image { Width = next ? 36 : 27, Height = next ? 36 : 27, Margin = new Thickness(0, 2, 8, 0),
            VerticalAlignment = VerticalAlignment.Top };
        System.Windows.Automation.AutomationProperties.SetName(icon, step.Item.Name);
        DockPanel.SetDock(icon, Dock.Left);
        row.Children.Add(icon);
        if (itemIcons is not null)
            _ = SetIcon(icon, itemIcons, patch, step.Item.Id);

        var text = new StackPanel();
        if (next)
            text.Children.Add(new TextBlock { Text = "Siguiente compra", FontSize = 11,
                Foreground = PurchaseAccent, FontWeight = FontWeights.Bold });
        text.Children.Add(new TextBlock { Text = step.Item.Name, FontSize = next ? 15 : 13, Foreground = Brushes.WhiteSmoke,
            FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        if (step.Detail is { Length: > 0 } detail)
            text.Children.Add(new TextBlock { Text = detail, FontSize = 11, Foreground = Muted, TextWrapping = TextWrapping.Wrap });
        row.Children.Add(text);

        if (next)
            content.Children.Add(new Border { Child = row, Background = PurchaseSurface, BorderBrush = PurchaseAccent,
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Padding = new Thickness(8),
                Margin = new Thickness(0, 4, 0, 0) });
        else
            content.Children.Add(row);
    }

    private void RenderPrismatics(string champion, PrismaticDisplay view)
    {
        AddLine("Top 5 prismáticos", 17, Brushes.WhiteSmoke, bold: true);
        AddLine($"Afinidad con {champion}", 12, Muted);
        foreach (var (entry, index) in view.Top.Entries.Select((entry, index) => (entry, index)))
        {
            bool offered = view.Top.IsOffered(index);
            var row = new DockPanel { Margin = new Thickness(0, 9, 0, 0) };
            row.Children.Add(new TextBlock
            {
                Text = (index + 1).ToString(), Width = 20, FontSize = 13,
                FontWeight = FontWeights.SemiBold, Foreground = Accent
            });
            var icon = new Image { Width = 26, Height = 26, Margin = new Thickness(0, 1, 8, 0),
                VerticalAlignment = VerticalAlignment.Top };
            row.Children.Add(icon);
            DockPanel.SetDock(icon, Dock.Left);
            if (itemIcons is not null)
                _ = SetIcon(icon, itemIcons, view.Patch, entry.Id);
            var text = new StackPanel();
            text.Children.Add(new TextBlock
            {
                Text = $"{entry.Rating.Name} · {entry.Rating.TierText}",
                FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Brushes.WhiteSmoke,
                TextWrapping = TextWrapping.Wrap
            });
            text.Children.Add(new TextBlock
            {
                Text = entry.Rating.Reason, FontSize = 11, Foreground = Muted, TextWrapping = TextWrapping.Wrap
            });
            if (offered)
                text.Children.Add(new TextBlock { Text = "En oferta", FontSize = 11, Foreground = Accent });
            row.Children.Add(text);
            content.Children.Add(row);
        }
    }

    private void AddItems(OpGgBuildItem[] items, string patch, bool ordered, string?[]? details = null)
    {
        var row = new Grid { Margin = new Thickness(0, 4, 0, 1), HorizontalAlignment = HorizontalAlignment.Left };
        double cellWidth = Math.Min(90, (PanelWidth - 34 - (ordered ? (items.Length - 1) * 12 : 0)) / items.Length);
        for (int i = 0; i < items.Length; i++)
        {
            if (i > 0 && ordered)
            {
                var arrow = new TextBlock { Text = "→", Foreground = Accent, FontSize = 12, Margin = new Thickness(0, 8, 0, 0) };
                AddColumn(arrow, 12);
            }
            var item = items[i];
            var image = new Image { Width = 32, Height = 32, Stretch = Stretch.UniformToFill };
            System.Windows.Automation.AutomationProperties.SetName(image, item.Name);
            var tile = new StackPanel { Margin = new Thickness(2, 0, 2, 0) };
            tile.Children.Add(new Border
            {
                Child = image, Width = 34, Height = 34, CornerRadius = new CornerRadius(3),
                BorderBrush = Accent, BorderThickness = new Thickness(1), Background = Overlay.Paint("#253851")
            });
            tile.Children.Add(new TextBlock
            {
                Text = item.Name, FontSize = 10.5, Foreground = Brushes.WhiteSmoke,
                TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 3, 0, 0)
            });
            if (details?[i] is { } detail)
                tile.Children.Add(new TextBlock
                {
                    Text = detail, FontSize = 10, Foreground = Muted, TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 2, 0, 2)
                });
            AddColumn(tile, cellWidth);
            if (itemIcons is not null)
                _ = SetIcon(image, itemIcons, patch, item.Id);
        }
        content.Children.Add(row);

        void AddColumn(UIElement element, double width)
        {
            Grid.SetColumn(element, row.ColumnDefinitions.Count);
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });
            row.Children.Add(element);
        }
    }

    private static async Task SetIcon(Image image, BuildItemIcons icons, string patch, int id)
    {
        if (await icons.Read(patch, id) is { } icon)
            image.Source = icon;
    }

    internal static string SectionLabel(OpGgBuildSection section) => section switch
    {
        OpGgBuildSection.Start => "Objetos iniciales",
        OpGgBuildSection.Boots => "Botas",
        OpGgBuildSection.Core => "Build principal",
        OpGgBuildSection.Prismatic => "Objetos prismáticos",
        _ => "Objetos"
    };

    internal static string ChoiceText(OpGgBuildSection section, OpGgBuildItem[] items) =>
        string.Join(section == OpGgBuildSection.Core ? " → " : " + ", items.Select(item => item.Name));

    private void AddLine(string text, double size, Brush foreground, bool bold = false, double top = 0) =>
        content.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = size,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = foreground,
            Margin = new Thickness(0, top, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint hwnd, int id);
}

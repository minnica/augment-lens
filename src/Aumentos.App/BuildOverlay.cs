using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace Aumentos;

internal sealed record PrismaticDisplay(ArenaPrismaticTop Top, Calibration Layout, string Patch);
internal sealed record BuildDisplay(Int32Rect Bounds, nint Target, string Champion, Mode Mode, OpGgBuildView? Source,
    Int32Rect? Shop = null, MayhemBuildPlan? Plan = null, PrismaticDisplay? Prismatics = null,
    string? ClientPatch = null, bool ArenaRivalsDetected = false, EnemyDamageMix? EnemyDamage = null,
    bool HideStartingItems = false);

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
    private const int ToggleHotkeyId = 1;
    private const int SizeHotkeyId = 2;
    private const int HotkeyMessage = 0x0312;
    private const uint CtrlAltNoRepeat = 0x4003;
    private const uint CtrlShiftNoRepeat = 0x4006;
    private const uint KeyB = 0x42;
    private const uint Digit9 = 0x39;
    internal const string SizeShortcutLabel = "Ctrl+Shift+9";
    private const double MinimumShopSideWidth = 210;

    private readonly BuildPanel panel;
    private readonly Viewbox frame;
    private BuildDisplay? displayed;
    private Int32Rect placement;
    private nint handle;
    private HwndSource? hotkeySource;
    private Action? onToggle;
    private bool sizeShortcutRegistered;
    private BuildSizeButton? sizeButton;
    private int sizePercent = BuildSizePreference.Default;

    internal event Action? SizeRequested;

    internal BuildOverlay(BuildItemIcons? itemIcons = null)
    {
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

        panel = new BuildPanel(itemIcons);
        frame = new Viewbox { Child = panel, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.Both };
        Content = frame;
        SourceInitialized += (_, _) =>
        {
            handle = new WindowInteropHelper(this).Handle;
            const int exStyle = -20;
            const int noActivateToolWindow = 0x080000A0;
            Native.SetWindowLongW(handle, exStyle, Native.GetWindowLongW(handle, exStyle) | noActivateToolWindow);
        };
    }

    internal (bool Toggle, bool Size) RegisterShortcuts(Action toggle)
    {
        if (hotkeySource is not null)
            return (onToggle is not null, sizeShortcutRegistered);

        var window = new WindowInteropHelper(this).EnsureHandle();
        var source = HwndSource.FromHwnd(window);
        if (source is null)
            return (false, false);
        if (RegisterHotKey(window, ToggleHotkeyId, CtrlAltNoRepeat, KeyB))
            onToggle = toggle;
        sizeShortcutRegistered = RegisterHotKey(window, SizeHotkeyId, CtrlShiftNoRepeat, Digit9);
        if (onToggle is not null || sizeShortcutRegistered)
        {
            hotkeySource = source;
            source.AddHook(OnWindowMessage);
        }
        return (onToggle is not null, sizeShortcutRegistered);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (hotkeySource is { } source)
        {
            if (onToggle is not null)
                UnregisterHotKey(handle, ToggleHotkeyId);
            if (sizeShortcutRegistered)
                UnregisterHotKey(handle, SizeHotkeyId);
            if (!source.IsDisposed)
                source.RemoveHook(OnWindowMessage);
        }
        onToggle = null;
        sizeShortcutRegistered = false;
        hotkeySource = null;
        base.OnClosed(e);
    }

    private nint OnWindowMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == HotkeyMessage)
        {
            var action = wParam == ToggleHotkeyId ? onToggle :
                wParam == SizeHotkeyId && sizeShortcutRegistered ? SizeRequested : null;
            if (action is not null)
            {
                handled = true;
                action();
            }
        }
        return 0;
    }

    internal void SetSize(int percent)
    {
        int updated = Math.Clamp(percent, BuildSizePreference.Minimum, BuildSizePreference.Maximum);
        if (sizePercent == updated)
            return;
        sizePercent = updated;
        if (IsVisible && displayed is { Prismatics: null } view &&
            (view.Target == 0 || Native.GetForegroundWindow() == view.Target))
            Render(view);
    }

    internal void Render(BuildDisplay? view)
    {
        if (view is null || view.Bounds.Width <= 0 || view.Bounds.Height <= 0)
        {
            sizeButton?.Hide();
            if (IsVisible)
                Hide();
            displayed = null;
            return;
        }

        var bounds = view.Bounds;
        var targetDpi = view.Target == 0 ? 0 : Native.GetDpiForWindow(view.Target);
        double dpi = targetDpi > 0 ? targetDpi / 96.0 : VisualTreeHelper.GetDpi(this).DpiScaleX;
        double requestedScale = view.Prismatics is null ? dpi * sizePercent / 100.0 : dpi;
        int margin = Math.Min((int)Math.Round(14 * dpi), Math.Min(bounds.Width, bounds.Height) / 4);
        int maxWidth = bounds.Width - 2 * margin, maxHeight = bounds.Height - 2 * margin;
        bool placeLeft = false;
        double layoutWidth = BuildPanel.ShopWidth;
        if (view.Prismatics is { Layout: var layout })
        {
            layoutWidth = BuildPanel.PrismaticWidth;
            var titles = layout.Titles.Select(region => region.Pixels(bounds.Width, bounds.Height)).ToArray();
            // Los recortes del OCR cubren solo el texto. Deja espacio para los bordes y el resplandor.
            int padding = bounds.Width / 24;
            int leftSpace = titles.Min(title => title.X) - padding - 2 * margin;
            int rightSpace = bounds.Width - titles.Max(title => title.X + title.Width) - padding - 2 * margin;
            int desiredWidth = (int)Math.Ceiling(layoutWidth * dpi);
            placeLeft = leftSpace >= desiredWidth || leftSpace >= rightSpace;
            maxWidth = Math.Min(maxWidth, Math.Max(1, placeLeft ? leftSpace : rightSpace));
        }
        else if (view.Shop is { } shop)
        {
            int leftSpace = shop.X - shop.Width / 12 - 2 * margin;
            int rightSpace = bounds.Width - shop.X - shop.Width - 2 * margin;
            int desiredWidth = (int)Math.Ceiling(BuildPanel.ShopWidth * requestedScale);
            int minimumWidth = (int)Math.Ceiling(MinimumShopSideWidth * dpi);
            placeLeft = rightSpace < desiredWidth && leftSpace > rightSpace;
            int sideSpace = placeLeft ? leftSpace : rightSpace;
            if (sideSpace >= minimumWidth)
                maxWidth = Math.Min(maxWidth, sideSpace);
            else
                maxWidth = Math.Min(maxWidth, (int)Math.Ceiling(BuildPanel.CompactWidth * dpi));
        }

        if (view.Prismatics is null)
            layoutWidth = Math.Clamp(Math.Floor(maxWidth / requestedScale), MinimumShopSideWidth, BuildPanel.ShopWidth);
        if (displayed is null || displayed.Champion != view.Champion || displayed.Mode != view.Mode ||
            displayed.Source != view.Source || displayed.Plan != view.Plan || displayed.Prismatics != view.Prismatics ||
            displayed.ClientPatch != view.ClientPatch || displayed.ArenaRivalsDetected != view.ArenaRivalsDetected ||
            displayed.EnemyDamage != view.EnemyDamage || displayed.HideStartingItems != view.HideStartingItems ||
            panel.Compact != (layoutWidth < BuildPanel.CompactWidth))
        {
            panel.Render(view, layoutWidth);
            panel.InvalidateMeasure();
        }
        panel.Width = layoutWidth;
        frame.StretchDirection = view.Prismatics is null ? StretchDirection.Both : StretchDirection.DownOnly;
        displayed = view;

        panel.Measure(new Size(panel.Width, double.PositiveInfinity));
        var desired = panel.DesiredSize;
        int buttonWidth = (int)Math.Ceiling(BuildSizeButton.ButtonWidth * dpi);
        int buttonHeight = (int)Math.Ceiling(BuildSizeButton.ButtonHeight * dpi);
        int buttonGap = (int)Math.Ceiling(5 * dpi);
        bool showSizeButton = view.Prismatics is null && SizeRequested is not null &&
            maxWidth >= buttonWidth && maxHeight >= 2 * buttonHeight + buttonGap;
        int top = margin + (showSizeButton ? buttonHeight + buttonGap : 0);
        maxHeight -= top - margin;
        double scale = Math.Min(requestedScale, Math.Min(maxWidth / desired.Width, maxHeight / desired.Height));
        int width = Math.Clamp((int)Math.Ceiling(desired.Width * scale), 1, maxWidth);
        int height = Math.Clamp((int)Math.Ceiling(desired.Height * scale), 1, maxHeight);
        var target = new Int32Rect(bounds.X + (placeLeft ? margin : bounds.Width - margin - width),
            bounds.Y + Math.Clamp((int)(bounds.Height * 0.12), top, bounds.Height - margin - height), width, height);

        if (!IsVisible || !Native.IsWindowVisible(handle) || target != placement)
        {
            Width = width / dpi;
            Height = height / dpi;
            if (!IsVisible)
                Show();
            const uint showWithoutActivation = 0x0040 | 0x0010;
            if (Native.SetWindowPos(handle, -1, target.X, target.Y, target.Width, target.Height, showWithoutActivation))
                placement = target;
        }

        if (showSizeButton && Native.IsWindowVisible(handle))
        {
            sizeButton ??= new BuildSizeButton(() => SizeRequested?.Invoke()) { Owner = this };
            int x = Math.Max(target.X + target.Width - buttonWidth, bounds.X + margin);
            sizeButton.Display(new Int32Rect(x, target.Y - buttonHeight - buttonGap, buttonWidth, buttonHeight), dpi);
        }
        else
            sizeButton?.Hide();
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint hwnd, int id);
}

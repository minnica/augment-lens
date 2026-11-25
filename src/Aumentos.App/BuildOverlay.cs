using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Aumentos;

internal sealed record PrismaticDisplay(ArenaPrismaticTop Top, Calibration Layout, string Patch);
internal sealed record BuildDisplay(Int32Rect Bounds, nint Target, string? Champion, Mode Mode, OpGgBuildView? Source,
    Int32Rect? Shop = null, MayhemBuildPlan? Plan = null, PrismaticDisplay? Prismatics = null,
    string? ClientPatch = null, bool ArenaRivalsDetected = false, EnemyDamageMix? EnemyDamage = null,
    bool HideStartingItems = false, EnemyDamageMix? LobbyDamage = null);

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
    internal const string PrismaticSizeTitle = "Tamaño del panel de prismáticos";
    private const double MinimumShopSideWidth = 210;

    private readonly BuildItemIcons? itemIcons;
    private readonly ShopItemCatalog? shopItems;
    private readonly DispatcherTimer itemHoverTimer;
    private readonly Func<int, bool>? saveSize;
    private readonly Func<int, bool>? savePrismaticSize;
    private readonly BuildPanel panel;
    private BuildDisplay? displayed;
    private Int32Rect placement;
    private nint handle;
    private HwndSource? hotkeySource;
    private Action? onToggle;
    private bool sizeShortcutRegistered;
    private BuildSizeButton? sizeButton;
    private BuildSizeButton? prismaticSizeButton;
    private BuildDragSurface? dragSurface;
    private BuildItemCard? itemCard;
    private FrameworkElement? hoveredRow;
    private long hoveredSince;
    private Int32Rect itemCardPanel;
    private (nint Target, Mode Mode, Point Origin)? floatingPosition;
    private (Point Cursor, Int32Rect Origin, (nint Handle, Int32Rect Bounds)[] Windows, bool Moving)? drag;
    private (int Margin, int Top) floatingLimits;
    private BuildDisplay? deferredBuild;
    private bool sizeEditorOpen;
    private bool panelOnLeft;
    private bool mayhemCollapsed;
    private int sizePercent = BuildSizePreference.Default;
    private int prismaticSizePercent = BuildSizePreference.Default;

    internal BuildSizeButton? MayhemDisclosure { get; private set; }
    internal int SizePercent => sizePercent;
    internal int PrismaticSizePercent => prismaticSizePercent;

    internal BuildOverlay(BuildItemIcons? itemIcons = null, Func<int, bool>? saveSize = null,
        Func<int, bool>? savePrismaticSize = null, ShopItemCatalog? shopItems = null)
    {
        this.itemIcons = itemIcons;
        this.shopItems = shopItems;
        this.saveSize = saveSize;
        this.savePrismaticSize = savePrismaticSize;
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
        Content = new Viewbox { Child = panel, Stretch = Stretch.Uniform };
        itemHoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(65) };
        itemHoverTimer.Tick += (_, _) =>
        {
            if (GetCursorPos(out var cursor))
                TrackItemHover(new Point(cursor.X, cursor.Y), Environment.TickCount64);
            else
                ClearItemHover();
        };
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
                itemHoverTimer.Start();
            else
            {
                itemHoverTimer.Stop();
                ClearItemHover();
            }
        };
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
        itemHoverTimer.Stop();
        StopDrag();
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
        MayhemDisclosure?.Close();
        base.OnClosed(e);
    }

    private nint OnWindowMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == HotkeyMessage)
        {
            if (wParam == ToggleHotkeyId && onToggle is { } toggle)
            {
                handled = true;
                toggle();
            }
            else if (wParam == SizeHotkeyId && sizeShortcutRegistered)
            {
                handled = true;
                if (displayed?.Prismatics is null)
                    ToggleSizeEditor();
            }
        }
        return 0;
    }

    private void ToggleSizeEditor()
    {
        if (!IsVisible || displayed is not { } view || (view.Target != 0 && Native.GetForegroundWindow() != view.Target))
            return;
        bool prismatics = view.Prismatics is not null;
        var button = prismatics ? prismaticSizeButton : sizeButton;
        var save = prismatics ? savePrismaticSize : saveSize;
        if (save is null || button is not { IsVisible: true })
            return;
        if (sizeEditorOpen)
            CloseSizeEditor();
        else
        {
            sizeEditorOpen = true;
            button.OpenScaleEditor(prismatics ? prismaticSizePercent : sizePercent, percent =>
            {
                if (prismatics)
                    SetPrismaticSize(percent);
                else
                    SetSize(percent);
                return save(percent);
            }, FinishSizeEditing);
        }
        Render(view);
    }

    private void FinishSizeEditing()
    {
        if (!sizeEditorOpen)
            return;
        CloseSizeEditor();
        if (displayed is { } view && (view.Target == 0 || Native.GetForegroundWindow() == view.Target))
            Render(view);
    }

    private void CloseSizeEditor()
    {
        if (!sizeEditorOpen)
            return;
        sizeEditorOpen = false;
        sizeButton?.CloseScaleEditor();
        prismaticSizeButton?.CloseScaleEditor();
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

    internal void SetPrismaticSize(int percent)
    {
        int updated = Math.Clamp(percent, BuildSizePreference.Minimum, BuildSizePreference.Maximum);
        if (prismaticSizePercent == updated)
            return;
        prismaticSizePercent = updated;
        if (IsVisible && displayed is { Prismatics: not null } view &&
            (view.Target == 0 || Native.GetForegroundWindow() == view.Target))
            Render(view);
    }

    internal FrameworkElement CreatePrismaticPreview(string champion, ArenaPrismaticTop top, string patch)
    {
        var preview = new BuildPanel(itemIcons) { Width = BuildPanel.PrismaticWidth, IsHitTestVisible = false };
        if (displayed is { Champion: { } activeChampion, Prismatics: { } active })
            preview.RenderPrismatics(activeChampion, active.Top, active.Patch);
        else
            preview.RenderPrismatics(champion, top, patch);
        return preview;
    }

    private void ToggleMayhemPanel()
    {
        if (displayed is not { Mode: Mode.Caos, Prismatics: null } view ||
            (view.Target != 0 && Native.GetForegroundWindow() != view.Target))
            return;
        mayhemCollapsed = !mayhemCollapsed;
        MayhemDisclosure?.SetDisclosure(mayhemCollapsed);
        Render(view);
    }

    private bool CanDrag(BuildDisplay? view) => IsVisible && !sizeEditorOpen &&
        view is { Shop: not null, Prismatics: null } && (view.Target == 0 || Native.GetForegroundWindow() == view.Target);

    private void BeginDrag()
    {
        if (GetCursorPos(out var cursor))
            StartDrag(new Point(cursor.X, cursor.Y));
    }

    internal void StartDrag(Point cursor)
    {
        if (drag is not null || !CanDrag(displayed))
            return;
        ClearItemHover();
        Window?[] windows = [this, dragSurface, sizeButton, MayhemDisclosure];
        var visible = windows.OfType<Window>().Where(window => window.IsVisible)
            .Select(window => new WindowInteropHelper(window).Handle)
            .Where(window => window != 0 && Native.IsWindowVisible(window))
            .Select(window => (window, window == handle ? placement : Native.Bounds(window))).ToArray();
        if (visible.Length == 0)
            return;
        drag = (cursor, placement, visible, false);
        CompositionTarget.Rendering += TrackDrag;
    }

    private void TrackDrag(object? sender, EventArgs args)
    {
        if (!CanDrag(displayed) || !GetCursorPos(out var cursor))
        {
            StopDrag();
            return;
        }
        bool pressed = Native.PrimaryMousePressed();
        MoveDrag(new Point(cursor.X, cursor.Y));
        if (!pressed)
            StopDrag(refresh: true);
    }

    internal void MoveDrag(Point cursor)
    {
        if (drag is not { } start || displayed is not { } view)
            return;
        var delta = cursor - start.Cursor;
        if (!start.Moving && Math.Abs(delta.X) + Math.Abs(delta.Y) < 3)
            return;
        var bounds = view.Bounds;
        var origin = new Point(start.Origin.X - bounds.X + delta.X, start.Origin.Y - bounds.Y + delta.Y);
        var target = FloatingBounds(bounds, origin, floatingLimits.Margin, floatingLimits.Top, placement.Width, placement.Height);
        if (target == placement)
            return;
        // Move the existing native surfaces together. WPF layout and data updates wait for mouse-up.
        const uint moveOnly = 0x0001 | 0x0004 | 0x0010 | 0x0200; // NOSIZE, NOZORDER, NOACTIVATE, NOOWNERZORDER
        var batch = BeginDeferWindowPos(start.Windows.Length);
        foreach (var window in start.Windows)
        {
            if (batch == 0)
                return;
            batch = DeferWindowPos(batch, window.Handle, 0,
                window.Bounds.X + target.X - start.Origin.X, window.Bounds.Y + target.Y - start.Origin.Y, 0, 0, moveOnly);
        }
        if (batch == 0 || !EndDeferWindowPos(batch))
            return;
        placement = target;
        drag = (start.Cursor, start.Origin, start.Windows, true);
    }

    internal void StopDrag(bool refresh = false)
    {
        if (drag is not { } ended)
            return;
        CompositionTarget.Rendering -= TrackDrag;
        drag = null;
        var latest = deferredBuild;
        deferredBuild = null;
        if (ended.Moving && displayed is { } view)
        {
            floatingPosition = (view.Target, view.Mode,
                new Point(placement.X - view.Bounds.X, placement.Y - view.Bounds.Y));
            dragSurface?.InvalidatePlacement();
            sizeButton?.InvalidatePlacement();
            MayhemDisclosure?.InvalidatePlacement();
        }
        if (refresh && (latest is not null || ended.Moving))
            Render(latest ?? displayed);
    }

    private void PositionDragSurface(BuildDisplay view, double dpi)
    {
        if (!CanDrag(view))
        {
            StopDrag();
            dragSurface?.Hide();
            return;
        }

        UpdateLayout();
        var header = panel.DragHandle;
        if (!header.IsVisible || header.ActualWidth <= 0 || header.ActualHeight <= 0)
        {
            dragSurface?.Hide();
            return;
        }

        var start = header.PointToScreen(new Point(0, 0));
        var end = header.PointToScreen(new Point(header.ActualWidth, header.ActualHeight));
        var area = new Int32Rect((int)Math.Round(start.X), (int)Math.Round(start.Y),
            Math.Max(1, (int)Math.Round(end.X - start.X)), Math.Max(1, (int)Math.Round(end.Y - start.Y)));
        dragSurface ??= new BuildDragSurface(BeginDrag, () => CanDrag(displayed)) { Owner = this };
        dragSurface.Display(area, dpi);
    }

    private void ClearItemHover()
    {
        hoveredRow = null;
        if (itemCard is { IsVisible: true })
            itemCard.Hide();
    }

    internal void TrackItemHover(Point cursor, long now)
    {
        if (!IsVisible || displayed is not { } view || drag is not null || sizeEditorOpen ||
            view.Target != 0 && Native.GetForegroundWindow() != view.Target ||
            cursor.X < placement.X || cursor.X >= placement.X + placement.Width ||
            cursor.Y < placement.Y || cursor.Y >= placement.Y + placement.Height ||
            panel.ItemAt(panel.PointFromScreen(cursor)) is not { } hit)
        {
            ClearItemHover();
            return;
        }

        if (hoveredRow != hit.Row)
        {
            ClearItemHover();
            hoveredRow = hit.Row;
            hoveredSince = now;
        }
        if (now - hoveredSince < 260 || itemCard is { IsVisible: true } && itemCardPanel == placement)
            return;

        var start = hit.Row.PointToScreen(new Point(0, 0));
        var end = hit.Row.PointToScreen(new Point(hit.Row.ActualWidth, hit.Row.ActualHeight));
        var patch = view.Prismatics?.Patch ?? view.ClientPatch ?? view.Source?.Data?.ClientPatch ?? "";
        var icon = panel.ItemIcon(hit.Item, patch, 65);
        itemCard ??= new BuildItemCard { Owner = this };
        itemCard.Present(hit.Item, icon, shopItems?.Find(hit.Item.Id, view.Mode), new Rect(start, end),
            placement, view.Bounds, Math.Clamp(placement.Width / panel.ActualWidth, .85, 1.4));
        itemCardPanel = placement;
    }

    internal void Render(BuildDisplay? view)
    {
        if (view is null || view.Bounds.Width <= 0 || view.Bounds.Height <= 0)
        {
            StopDrag();
            dragSurface?.Hide();
            CloseSizeEditor();
            sizeButton?.Hide();
            prismaticSizeButton?.Hide();
            MayhemDisclosure?.Hide();
            if (IsVisible)
                Hide();
            displayed = null;
            return;
        }

        var bounds = view.Bounds;
        if (drag is not null)
        {
            if (displayed is { } current && CanDrag(view) && current.Target == view.Target && current.Mode == view.Mode &&
                current.Bounds == bounds)
            {
                deferredBuild = view;
                return;
            }
            StopDrag();
        }
        var targetDpi = view.Target == 0 ? 0 : Native.GetDpiForWindow(view.Target);
        double dpi = targetDpi > 0 ? targetDpi / 96.0 : VisualTreeHelper.GetDpi(this).DpiScaleX;
        bool prismatics = view.Prismatics is not null;
        if (displayed is null || displayed.Target != view.Target || displayed.Mode != view.Mode ||
            displayed.Prismatics?.Layout != view.Prismatics?.Layout)
        {
            StopDrag();
            CloseSizeEditor();
        }
        var floatingOrigin = !prismatics && view.Shop is not null && floatingPosition is { } floating &&
            floating.Target == view.Target && floating.Mode == view.Mode ? (Point?)floating.Origin : null;
        double requestedScale = dpi * sizePercent / 100.0;
        int margin = Math.Min((int)Math.Round(14 * dpi), Math.Min(bounds.Width, bounds.Height) / 4);
        int maxWidth = bounds.Width - 2 * margin, maxHeight = bounds.Height - 2 * margin;
        bool placeLeft = false;
        double layoutWidth = BuildPanel.ShopWidth;
        if (view.Prismatics is { Layout: var layout })
        {
            layoutWidth = BuildPanel.PrismaticWidth;
            var titles = layout.Titles.Select(region => region.Pixels(bounds.Width, bounds.Height)).ToArray();
            var side = PrismaticSide(bounds, titles, dpi);
            placeLeft = side.OnLeft;
            maxWidth = Math.Min(maxWidth, Math.Max(1, side.AvailableWidth));
        }
        else if (view.Shop is { } shop)
        {
            int leftSpace = shop.X - shop.Width / 12 - 2 * margin;
            int rightSpace = bounds.Width - shop.X - shop.Width - 2 * margin;
            int desiredWidth = (int)Math.Ceiling(BuildPanel.ShopWidth * requestedScale);
            int minimumWidth = (int)Math.Ceiling(MinimumShopSideWidth * dpi);
            // Keep the slider on the same side even if the preferred side changes with the scale.
            placeLeft = sizeEditorOpen ? panelOnLeft : rightSpace < desiredWidth && leftSpace > rightSpace;
            int sideSpace = placeLeft ? leftSpace : rightSpace;
            maxWidth = Math.Min(maxWidth, sideSpace >= minimumWidth ? sideSpace : (int)Math.Ceiling(BuildPanel.CompactWidth * dpi));
        }

        if (!prismatics)
            layoutWidth = Math.Clamp(Math.Floor(maxWidth / requestedScale), MinimumShopSideWidth, BuildPanel.ShopWidth);
        if (displayed is null || displayed.Champion != view.Champion || displayed.Mode != view.Mode ||
            displayed.Source != view.Source || displayed.Plan != view.Plan || displayed.Prismatics != view.Prismatics ||
            displayed.ClientPatch != view.ClientPatch || displayed.ArenaRivalsDetected != view.ArenaRivalsDetected ||
            displayed.EnemyDamage != view.EnemyDamage || displayed.LobbyDamage != view.LobbyDamage ||
            displayed.HideStartingItems != view.HideStartingItems ||
            panel.Compact != (layoutWidth < BuildPanel.CompactWidth))
        {
            ClearItemHover();
            panel.Render(view, layoutWidth);
            panel.InvalidateMeasure();
        }
        panel.Width = layoutWidth;
        displayed = view;
        panelOnLeft = placeLeft;

        panel.Measure(new Size(panel.Width, double.PositiveInfinity));
        var desired = panel.DesiredSize;
        int buttonWidth = (int)Math.Ceiling(BuildSizeButton.ButtonWidth * dpi);
        int buttonHeight = (int)Math.Ceiling(BuildSizeButton.ButtonHeight * dpi);
        int buttonGap = (int)Math.Ceiling(5 * dpi);
        bool roomForControls = maxHeight >= 2 * buttonHeight + buttonGap;
        var restorePosition = !prismatics && view.Mode == Mode.Caos && roomForControls && maxWidth >= buttonHeight &&
            view.Shop is { } openShop ? PlaceRestoreButton(bounds, openShop, placeLeft, buttonHeight, buttonGap) : null;
        bool collapsed = restorePosition.HasValue && mayhemCollapsed;
        bool inlineSizeFits = (prismatics ? savePrismaticSize : saveSize) is not null && roomForControls &&
            maxWidth >= (int)Math.Ceiling(BuildSizeButton.ScaleEditorMinimumWidth * dpi);
        if (!inlineSizeFits || collapsed)
            CloseSizeEditor();
        bool showDisclosure = restorePosition.HasValue && !sizeEditorOpen;
        int controlsWidth = buttonWidth + (showDisclosure ? buttonHeight + buttonGap : 0);
        bool sizeButtonFits = inlineSizeFits && maxWidth >= controlsWidth;
        bool showSizeButton = !collapsed && (sizeEditorOpen || sizeButtonFits);
        if (sizeEditorOpen)
            buttonWidth = Math.Min(maxWidth, (int)Math.Ceiling(BuildSizeButton.ScaleEditorWidth * dpi));
        int preferredTop = (int)(bounds.Height * 0.12);
        int top = margin + (showDisclosure || showSizeButton ? buttonHeight + buttonGap : 0);
        if (prismatics && !placeLeft && roomForControls)
        {
            // Leave the right corner clear for the card-size control and its slider.
            int tierBottom = Overlay.TierControlTop(bounds, dpi) + buttonHeight;
            int panelTop = tierBottom + buttonGap + (showSizeButton ? buttonHeight + buttonGap : 0);
            if (panelTop < bounds.Height - margin)
                top = Math.Max(top, panelTop);
        }
        if (sizeEditorOpen && floatingOrigin is null)
            top = Math.Max(top, preferredTop);
        maxHeight -= top - margin;
        double fittingScale = Math.Min(maxWidth / desired.Width, maxHeight / desired.Height);
        if (prismatics)
            requestedScale = Math.Min(dpi, fittingScale) * prismaticSizePercent / 100.0;
        double scale = Math.Min(requestedScale, fittingScale);
        int width = Math.Clamp((int)Math.Ceiling(desired.Width * scale), 1, maxWidth);
        int height = Math.Clamp((int)Math.Ceiling(desired.Height * scale), 1, maxHeight);
        floatingLimits = (margin, top);
        var target = floatingOrigin is { } origin
            ? FloatingBounds(bounds, origin, margin, top, width, height)
            : new Int32Rect(bounds.X + (placeLeft ? margin : bounds.Width - margin - width),
                bounds.Y + Math.Clamp(preferredTop, top, bounds.Height - margin - height), width, height);

        if (collapsed)
        {
            if (IsVisible)
                Hide();
        }
        else if (!IsVisible || !Native.IsWindowVisible(handle) || target != placement)
        {
            Width = width / dpi;
            Height = height / dpi;
            if (!IsVisible)
                Show();
            const uint showWithoutActivation = 0x0040 | 0x0010;
            if (Native.SetWindowPos(handle, -1, target.X, target.Y, target.Width, target.Height, showWithoutActivation))
                placement = target;
        }

        int controlsY = target.Y - buttonHeight - buttonGap;
        int disclosureX = target.X;
        if (showDisclosure)
        {
            int toolbarWidth = sizeButtonFits ? controlsWidth : buttonHeight;
            if (!placeLeft)
                disclosureX = Math.Clamp(target.X + target.Width - toolbarWidth, bounds.X + margin, target.X);
            if (MayhemDisclosure is null)
            {
                MayhemDisclosure = new BuildSizeButton(ToggleMayhemPanel, "Recomendaciones de objetos de ARAM Caos");
                MayhemDisclosure.SetDisclosure(mayhemCollapsed);
            }
            var buttonPosition = collapsed ? restorePosition.GetValueOrDefault()
                : new Int32Rect(disclosureX, controlsY, buttonHeight, buttonHeight);
            MayhemDisclosure.Display(buttonPosition, dpi);
        }
        else
            MayhemDisclosure?.Hide();

        if (prismatics)
            sizeButton?.Hide();
        else
            prismaticSizeButton?.Hide();
        if (showSizeButton && Native.IsWindowVisible(handle))
        {
            var button = prismatics
                ? prismaticSizeButton ??= new BuildSizeButton(ToggleSizeEditor, PrismaticSizeTitle) { Owner = this }
                : sizeButton ??= new BuildSizeButton(ToggleSizeEditor) { Owner = this };
            int x = (prismatics || sizeEditorOpen) && placeLeft
                ? target.X : Math.Max(target.X + target.Width - buttonWidth, bounds.X + margin);
            if (showDisclosure)
                x = Math.Max(x, disclosureX + buttonHeight + buttonGap);
            button.Display(new Int32Rect(x, controlsY, buttonWidth, buttonHeight), dpi);
        }
        else
        {
            sizeButton?.Hide();
            prismaticSizeButton?.Hide();
        }
        panel.SetShopDragEnabled(view.Shop.HasValue && !prismatics);
        PositionDragSurface(view, dpi);
    }

    internal static Int32Rect FloatingBounds(Int32Rect bounds, Point origin, int margin, int top, int width, int height) =>
        new(bounds.X + Math.Clamp((int)origin.X, margin, bounds.Width - margin - width),
            bounds.Y + Math.Clamp((int)origin.Y, top, bounds.Height - margin - height), width, height);

    internal static (bool OnLeft, int AvailableWidth) PrismaticSide(Int32Rect bounds, Int32Rect[] titles, double dpi)
    {
        int margin = Math.Min((int)Math.Round(14 * dpi), Math.Min(bounds.Width, bounds.Height) / 4);
        // Los recortes del OCR cubren solo el texto. Deja espacio para los bordes y el resplandor.
        int clearance = bounds.Width / 24 + 2 * margin;
        int left = titles.Min(title => title.X) - clearance;
        int right = bounds.Width - titles.Max(title => title.X + title.Width) - clearance;
        bool onLeft = left >= Math.Ceiling(BuildPanel.PrismaticWidth * dpi) || left >= right;
        return (onLeft, onLeft ? left : right);
    }

    private static Int32Rect? PlaceRestoreButton(Int32Rect bounds, Int32Rect shop, bool placeLeft, int size, int gap)
    {
        int maxX = bounds.Width - size;
        int maxY = bounds.Height - size;
        int left = shop.X - size - gap;
        int right = shop.X + shop.Width + gap;
        int y = Math.Clamp(shop.Y, 0, maxY);
        if (left >= 0 && (placeLeft || right > maxX))
            return new Int32Rect(bounds.X + left, bounds.Y + y, size, size);
        if (right <= maxX)
            return new Int32Rect(bounds.X + right, bounds.Y + y, size, size);

        int x = Math.Clamp(shop.X + shop.Width - size, 0, maxX);
        int above = shop.Y - size - gap;
        int below = shop.Y + shop.Height + gap;
        y = above >= 0 ? above : below;
        return y <= maxY ? new Int32Rect(bounds.X + x, bounds.Y + y, size, size) : null;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint hwnd, int id);

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorPoint { public int X, Y; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out CursorPoint point);

    [DllImport("user32.dll")]
    private static extern nint BeginDeferWindowPos(int windows);

    [DllImport("user32.dll")]
    private static extern nint DeferWindowPos(nint batch, nint window, nint after, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EndDeferWindowPos(nint batch);
}

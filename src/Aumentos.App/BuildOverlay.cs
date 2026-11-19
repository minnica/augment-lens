using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace Aumentos;

internal sealed record BuildDisplay(Int32Rect Bounds, nint Target, string Champion, Mode Mode, OpGgBuildView Source);

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
    private readonly StackPanel content = new();
    private readonly Border panel;
    private BuildDisplay? displayed;
    private Int32Rect placement;
    private nint handle;
    private HwndSource? hotkeySource;
    private Action? onShortcut;

    internal BuildOverlay()
    {
        Title = "Aumentos LoL · Build de OP.GG";
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
            return;
        }

        if (displayed is null || displayed.Champion != view.Champion || displayed.Mode != view.Mode ||
            displayed.Source != view.Source)
            RenderContent(view);
        displayed = view;

        var bounds = view.Bounds;
        var targetDpi = view.Target == 0 ? 0 : Native.GetDpiForWindow(view.Target);
        double dpi = targetDpi > 0 ? targetDpi / 96.0 : VisualTreeHelper.GetDpi(this).DpiScaleX;
        int margin = Math.Min((int)Math.Round(14 * dpi), Math.Min(bounds.Width, bounds.Height) / 4);
        int maxWidth = bounds.Width - 2 * margin, maxHeight = bounds.Height - 2 * margin;

        panel.Measure(new Size(PanelWidth, double.PositiveInfinity));
        var desired = panel.DesiredSize;
        double scale = Math.Min(dpi, Math.Min(maxWidth / desired.Width, maxHeight / desired.Height));
        int width = Math.Clamp((int)Math.Ceiling(desired.Width * scale), 1, maxWidth);
        int height = Math.Clamp((int)Math.Ceiling(desired.Height * scale), 1, maxHeight);
        var target = new Int32Rect(bounds.X + bounds.Width - margin - width,
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

    private void RenderContent(BuildDisplay view)
    {
        var build = view.Source.Data;
        var mode = view.Mode == Mode.Caos ? "ARAM Caos" : "Arena";
        var patch = build is null ? mode :
            $"{mode} · {(build.IsPrevious ? "Parche anterior" : "Parche")} {LeaguePatch.Public(build.SourcePatch)}";
        content.Children.Clear();
        AddLine($"{view.Champion} · OP.GG", 17, Brushes.WhiteSmoke, bold: true);
        AddLine(patch, 12, build?.IsPrevious == true ? PreviousPatch : Muted);

        if (build is null || build.Groups.Length == 0)
        {
            AddLine(view.Source.Status, 13, Brushes.WhiteSmoke, top: 9);
            return;
        }

        foreach (var group in build.Groups)
        {
            AddLine(SectionLabel(group.Section), 12, Accent, bold: true, top: 9);
            foreach (var (choice, index) in group.Choices.Take(2).Select((items, index) => (items, index)))
            {
                var text = ChoiceText(group.Section, choice);
                AddLine(index == 0 ? text : $"Alternativa · {text}", index == 0 ? 13 : 12,
                    index == 0 ? Brushes.WhiteSmoke : Muted, top: index == 0 ? 2 : 3);
            }
        }
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

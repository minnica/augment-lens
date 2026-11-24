using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace Aumentos;

// A separate input window keeps the rest of the build overlay click-through.
internal sealed class BuildDragSurface : Window
{
    private nint handle;
    private Int32Rect placement;

    internal BuildDragSurface(Action beginDrag, Func<bool> canDrag)
    {
        Title = "Mover recomendaciones de objetos";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        ShowActivated = false;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        Focusable = false;
        Cursor = Cursors.SizeAll;
        Background = Brushes.Transparent;
        Content = new Border { Background = Overlay.Paint("#01000000") };
        PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (canDrag())
                beginDrag();
            e.Handled = true;
        };
        SourceInitialized += (_, _) =>
        {
            handle = new WindowInteropHelper(this).Handle;
            const int extendedStyle = -20, noActivateToolWindow = 0x08000080;
            Native.SetWindowLongW(handle, extendedStyle, Native.GetWindowLongW(handle, extendedStyle) | noActivateToolWindow);
            HwndSource.FromHwnd(handle)?.AddHook(OnWindowMessage);
        };

        nint OnWindowMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
        {
            const int mouseActivate = 0x0021, noActivate = 3;
            const int hitTest = 0x0084, hitTransparent = -1;
            if (message == mouseActivate)
            {
                handled = true;
                return noActivate;
            }
            if (message == hitTest && !canDrag())
            {
                Hide();
                handled = true;
                return hitTransparent;
            }
            return 0;
        }
    }

    internal void InvalidatePlacement() => placement = default;

    internal void Display(Int32Rect target, double dpi)
    {
        if (IsVisible && Native.IsWindowVisible(handle) && target == placement)
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

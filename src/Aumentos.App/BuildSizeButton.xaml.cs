using System;
using System.Windows;
using System.Windows.Interop;

namespace Aumentos;

internal sealed partial class BuildSizeButton : Window
{
    internal const int ButtonWidth = 92;
    internal const int ButtonHeight = 34;
    private nint handle;
    private Int32Rect placement;

    internal BuildSizeButton(Action open)
    {
        InitializeComponent();
        OpenButton.Background = BuildPanel.WindowSurface;
        OpenButton.BorderBrush = BuildPanel.WindowFrame;
        OpenButton.Click += (_, _) => open();
        SourceInitialized += (_, _) =>
        {
            handle = new WindowInteropHelper(this).Handle;
            const int exStyle = -20;
            const int noActivateToolWindow = 0x08000080;
            Native.SetWindowLongW(handle, exStyle, Native.GetWindowLongW(handle, exStyle) | noActivateToolWindow);
        };
    }

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

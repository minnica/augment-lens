using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Aumentos;

internal sealed partial class BuildSizeButton : Window
{
    internal const int ButtonWidth = 92;
    internal const int ButtonHeight = 34;
    internal const int ScaleEditorWidth = 240;
    internal const int ScaleEditorMinimumWidth = 132;
    private static readonly Geometry MinimizeMark = CreateMark("M 2,7 H 12");
    private static readonly Geometry RestoreMark = CreateMark("M 2,7 H 12 M 7,2 V 12");
    private nint handle;
    private Int32Rect placement;

    internal BuildSizeButton(Action open, string? title = null, string? label = null)
    {
        InitializeComponent();
        if (title is not null)
        {
            Title = title;
            AutomationProperties.SetName(OpenButton, title);
        }
        if (label is not null)
            OpenButton.Content = label;
        OpenButton.Click += (_, _) => open();
        SourceInitialized += (_, _) =>
        {
            handle = new WindowInteropHelper(this).Handle;
            const int exStyle = -20;
            const int noActivateToolWindow = 0x08000080;
            Native.SetWindowLongW(handle, exStyle, Native.GetWindowLongW(handle, exStyle) | noActivateToolWindow);
            HwndSource.FromHwnd(handle)?.AddHook(KeepGameFocus);
        };
    }

    internal void SetDisclosure(bool collapsed)
    {
        var label = collapsed ? "Mostrar recomendaciones de objetos" : "Minimizar recomendaciones de objetos";
        AutomationProperties.SetName(OpenButton, label);
        OpenButton.ToolTip = label;
        var content = new Canvas { Width = 14, Height = 14 };
        content.Children.Add(new Path
        {
            Data = collapsed ? RestoreMark : MinimizeMark, Stroke = OpenButton.Foreground, StrokeThickness = 2
        });
        OpenButton.Content = content;
    }

    internal void OpenScaleEditor(int percent, Func<int, bool> apply, Action close, int maximum = BuildSizePreference.Maximum)
    {
        if (Content is not BuildScaleEditor)
            Content = new BuildScaleEditor(percent, apply, close, maximum);
    }

    internal void CloseScaleEditor()
    {
        if (Content is BuildScaleEditor)
            Content = OpenButton;
    }

    private static Geometry CreateMark(string data)
    {
        var geometry = Geometry.Parse(data);
        geometry.Freeze();
        return geometry;
    }

    private static nint KeepGameFocus(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        const int mouseActivate = 0x0021;
        const int noActivate = 3;
        if (message != mouseActivate)
            return 0;
        handled = true;
        return noActivate;
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

using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Aumentos;

internal sealed partial class BuildScaleEditor : UserControl
{
    private const string SaveError = "Se aplicó el tamaño. Cambia el valor para intentar guardarlo de nuevo.";
    private const string ScaleHint = "Escala del panel · 75 % a 150 %";
    internal static readonly TimeSpan PointerExitDelay = TimeSpan.FromMilliseconds(350);

    internal BuildScaleEditor(int percent, Func<int, bool> apply, Action close)
    {
        InitializeComponent();
        Frame.Background = OverlayPanelStyle.Surface;
        Frame.BorderBrush = OverlayPanelStyle.Frame;
        PercentText.Foreground = OverlayPanelStyle.Ink;
        SaveStatus.Foreground = OverlayPanelStyle.Gold;
        SizeSlider.Minimum = BuildSizePreference.Minimum;
        SizeSlider.Maximum = BuildSizePreference.Maximum;
        SizeSlider.Value = percent;
        SizeSlider.ToolTip = ScaleHint;
        PercentText.Text = $"{SizeSlider.Value:0} %";

        SizeSlider.ValueChanged += (_, args) =>
        {
            int current = (int)args.NewValue;
            PercentText.Text = $"{current} %";
            bool saved = apply(current);
            SaveStatus.Visibility = saved ? Visibility.Collapsed : Visibility.Visible;
            SizeSlider.ToolTip = saved ? ScaleHint : SaveError;
            AutomationProperties.SetHelpText(SizeSlider, saved ? string.Empty : SaveError);
        };
        var dismiss = new DispatcherTimer { Interval = PointerExitDelay };
        dismiss.Tick += (_, _) =>
        {
            dismiss.Stop();
            if (IsLoaded && !IsMouseOver && !IsMouseCaptureWithin)
                close();
        };
        MouseEnter += (_, _) => dismiss.Stop();
        MouseLeave += (_, _) => dismiss.Start();
        IsMouseCaptureWithinChanged += (_, _) =>
        {
            if (IsLoaded && !IsMouseCaptureWithin && !IsMouseOver)
                dismiss.Start();
        };
        Unloaded += (_, _) => dismiss.Stop();
    }
}

using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Aumentos;

internal sealed partial class BuildSizeWindow : Window
{
    private readonly ScaleTransform previewScale = new();

    internal BuildSizeWindow(int percent, string patch, BuildItemIcons? icons, Func<int, bool> apply)
    {
        InitializeComponent();
        var workArea = SystemParameters.WorkArea;
        MinWidth = Math.Min(MinWidth, workArea.Width);
        MinHeight = Math.Min(MinHeight, workArea.Height);
        Width = Math.Min(Width, workArea.Width);
        Height = Math.Min(Height, workArea.Height);

        var panel = new BuildPanel(icons) { IsHitTestVisible = false };
        panel.RenderPreview(patch);
        panel.Measure(new Size(panel.Width, double.PositiveInfinity));
        double maxScale = BuildSizePreference.Maximum / 100.0;
        PreviewStage.Width = panel.DesiredSize.Width * maxScale;
        PreviewStage.Height = panel.DesiredSize.Height * maxScale;
        panel.LayoutTransform = previewScale;
        PreviewHost.Content = panel;
        SizeSlider.Minimum = BuildSizePreference.Minimum;
        SizeSlider.Maximum = BuildSizePreference.Maximum;
        SizeSlider.Value = percent;
        UpdatePreview(percent);

        SizeSlider.ValueChanged += (_, args) =>
        {
            int current = (int)args.NewValue;
            UpdatePreview(current);
            SaveStatus.Text = apply(current)
                ? "Se guarda automáticamente."
                : "Tamaño aplicado durante esta sesión. Intenta de nuevo para guardarlo.";
        };
        ResetButton.Click += (_, _) => SizeSlider.Value = BuildSizePreference.Default;
        Loaded += (_, _) => SizeSlider.Focus();
        PreviewKeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape)
            {
                args.Handled = true;
                Close();
            }
        };
    }

    private void UpdatePreview(int percent)
    {
        PercentText.Text = $"{percent} %";
        previewScale.ScaleX = previewScale.ScaleY = percent / 100.0;
        ResetButton.IsEnabled = percent != BuildSizePreference.Default;
    }
}

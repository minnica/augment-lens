using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;

namespace Aumentos;

internal sealed partial class BuildSizeWindow : Window
{
    private readonly ScaleTransform previewScale = new();

    internal BuildSizeWindow(int percent, string patch, BuildItemIcons? icons, Func<int, bool> apply)
        : this(percent, BuildPreview(patch, icons), apply, itemTiers: false)
    {
    }

    internal BuildSizeWindow(int percent, FrameworkElement preview, Func<int, bool> apply)
        : this(percent, preview, apply, itemTiers: true)
    {
    }

    private BuildSizeWindow(int percent, FrameworkElement preview, Func<int, bool> apply, bool itemTiers)
    {
        InitializeComponent();
        if (itemTiers)
        {
            Heading.Text = "Tamaño de los tiers de objetos";
            Title = $"{Heading.Text} · Aumentos LoL";
            ScaleLabel.Text = "Escala del distintivo";
            AutomationProperties.SetName(SizeSlider, "Escala del distintivo en porcentaje");
            Width = 510;
            Height = 385;
            MinHeight = 355;
        }

        var workArea = SystemParameters.WorkArea;
        MinWidth = Math.Min(MinWidth, workArea.Width);
        MinHeight = Math.Min(MinHeight, workArea.Height);
        Width = Math.Min(Width, workArea.Width);
        Height = Math.Min(Height, workArea.Height);

        preview.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double maxScale = BuildSizePreference.Maximum / 100.0;
        PreviewStage.Width = preview.DesiredSize.Width * maxScale;
        PreviewStage.Height = preview.DesiredSize.Height * maxScale;
        preview.LayoutTransform = previewScale;
        PreviewHost.Content = preview;
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

    private static FrameworkElement BuildPreview(string patch, BuildItemIcons? icons)
    {
        var panel = new BuildPanel(icons) { IsHitTestVisible = false };
        panel.RenderPreview(patch);
        return panel;
    }

    private void UpdatePreview(int percent)
    {
        PercentText.Text = $"{percent} %";
        previewScale.ScaleX = previewScale.ScaleY = percent / 100.0;
        ResetButton.IsEnabled = percent != BuildSizePreference.Default;
    }
}

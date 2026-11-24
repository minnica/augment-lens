using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Shell;

namespace Aumentos;

internal static class OverlayWindowFrame
{
    private const double CaptionHeight = 36;

    internal static void Apply(Window window, UIElement content)
    {
        window.WindowStyle = WindowStyle.None;
        window.AllowsTransparency = true;
        window.Background = Brushes.Transparent;
        window.UseLayoutRounding = true;
        WindowChrome.SetWindowChrome(window, new WindowChrome
        {
            CaptionHeight = CaptionHeight,
            ResizeBorderThickness = new Thickness(5),
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false
        });

        var buttonStyle = (Style)window.FindResource("OverlayButton");
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 6, 0) };
        actions.Children.Add(CaptionButton("Minimizar", "M 1,8 H 11", () => SystemCommands.MinimizeWindow(window)));
        var maximize = CaptionButton("Maximizar", "M 1,1 H 11 V 9 H 1 Z", ToggleMaximized);
        actions.Children.Add(maximize);
        actions.Children.Add(CaptionButton("Cerrar", "M 2,1 L 10,9 M 10,1 L 2,9", () => SystemCommands.CloseWindow(window)));
        window.StateChanged += (_, _) =>
        {
            bool restored = window.WindowState != WindowState.Maximized;
            var label = restored ? "Maximizar" : "Restaurar";
            maximize.ToolTip = label;
            AutomationProperties.SetName(maximize, label);
            ((Path)maximize.Content).Data = Geometry.Parse(restored ? "M 1,1 H 11 V 9 H 1 Z" : "M 3,3 V 1 H 11 V 7 H 9 M 1,3 H 9 V 9 H 1 Z");
        };

        var caption = new DockPanel { Height = CaptionHeight, Background = OverlayPanelStyle.HeaderSurface };
        DockPanel.SetDock(actions, Dock.Right);
        caption.Children.Add(actions);
        var title = new TextBlock
        {
            Text = window.Title, Foreground = OverlayPanelStyle.Gold, FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(16, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        caption.Children.Add(title);

        var layout = new DockPanel();
        var header = new Border { BorderBrush = OverlayPanelStyle.Divider, BorderThickness = new Thickness(0, 0, 0, 1), Child = caption };
        DockPanel.SetDock(header, Dock.Top);
        layout.Children.Add(header);
        layout.Children.Add(content);
        var frame = new Border { Child = layout };
        OverlayPanelStyle.ApplyFrame(frame);
        window.Content = frame;

        Button CaptionButton(string label, string glyph, Action click)
        {
            var button = new Button
            {
                Style = buttonStyle, Width = 30, Height = 23, Padding = new Thickness(0), Margin = new Thickness(3, 0, 0, 0),
                ToolTip = label, Content = new Path { Data = Geometry.Parse(glyph), Stroke = OverlayPanelStyle.Ink }
            };
            AutomationProperties.SetName(button, label);
            WindowChrome.SetIsHitTestVisibleInChrome(button, true);
            button.Click += (_, _) => click();
            return button;
        }

        void ToggleMaximized()
        {
            if (window.WindowState == WindowState.Maximized)
                SystemCommands.RestoreWindow(window);
            else
                SystemCommands.MaximizeWindow(window);
        }
    }
}

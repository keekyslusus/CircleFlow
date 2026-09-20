namespace CircleToSearch.Capture;

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using CircleToSearch.Ui;

public sealed class FloatingToolbar
{
    private readonly StackPanel _actions;
    private readonly FloatingToolbarPalette _palette;

    internal FloatingToolbar(FloatingToolbarPalette palette)
    {
        _palette = palette;
        _actions = new StackPanel { Orientation = Orientation.Horizontal };
        Surface = new Border
        {
            Child = _actions,
            Background = OverlayVisualResources.Frozen(palette.Surface),
            BorderBrush = OverlayVisualResources.Frozen(palette.Border),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(4),
            Visibility = Visibility.Collapsed,
        };
        Layer = new Canvas { Background = null, IsHitTestVisible = true };
        Layer.Children.Add(Surface);
    }

    internal Canvas Layer { get; }
    internal Border Surface { get; }
    internal bool IsOpen => Surface.Visibility == Visibility.Visible;

    internal Button AddAction(string label)
    {
        var button = new Button
        {
            Content = label,
            Foreground = OverlayVisualResources.Frozen(_palette.Text),
            Background = OverlayVisualResources.Frozen(_palette.Surface),
            BorderThickness = new Thickness(),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(2),
            Cursor = Cursors.Hand,
            FontFamily = OverlayVisualResources.Font,
            FontSize = 13,
        };
        OverlayVisualResources.ApplyButtonTemplate(button, 8, _palette.ButtonHover, _palette.Text);
        AutomationProperties.SetName(button, label);
        _actions.Children.Add(button);
        return button;
    }

    internal void Show(Rect anchorDips, Size viewportDips)
    {
        Surface.Visibility = Visibility.Visible;
        Surface.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var placement = FloatingToolbarLayout.Place(anchorDips, Surface.DesiredSize, viewportDips);
        Canvas.SetLeft(Surface, placement.X);
        Canvas.SetTop(Surface, placement.Y);
    }

    internal void Hide() => Surface.Visibility = Visibility.Collapsed;
}

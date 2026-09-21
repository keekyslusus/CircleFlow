namespace CircleToSearch.Capture;

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using CircleToSearch.Ui;

public sealed class FloatingToolbar
{
    private const double ActionPadding = 16;
    private const double CompactActionPadding = 4;
    private readonly WrapPanel _actions;
    private readonly FloatingToolbarPalette _palette;
    private readonly Func<bool> _animationsEnabled;
    private CardTransitions.ExitHandle? _exit;

    internal FloatingToolbar(FloatingToolbarPalette palette, Func<bool>? animationsEnabled = null)
    {
        _palette = palette;
        _animationsEnabled = animationsEnabled ?? OverlayVisualResources.AnimationsEnabled;
        _actions = new WrapPanel { Orientation = Orientation.Horizontal };
        Surface = new Border
        {
            Child = _actions,
            Background = OverlayVisualResources.Frozen(palette.Surface),
            CornerRadius = new CornerRadius(24),
            MinHeight = 44,
            Padding = new Thickness(6, 4, 6, 4),
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
            IsEnabled = false,
        };
        Surface.SizeChanged += (_, _) => Surface.CornerRadius = new CornerRadius(Surface.ActualHeight / 2);
        Surface.Unloaded += (_, _) => Hide(animate: false);
        Layer = new Canvas { Background = null, IsHitTestVisible = true };
        Layer.Children.Add(Surface);
    }

    internal Canvas Layer { get; }
    internal Border Surface { get; }
    internal bool IsOpen { get; private set; }

    internal Button AddAction(string label)
    {
        var button = new Button
        {
            Content = label,
            Foreground = OverlayVisualResources.Frozen(_palette.Text),
            Background = OverlayVisualResources.Frozen(_palette.Surface),
            BorderThickness = new Thickness(),
            Padding = new Thickness(ActionPadding, 0, ActionPadding, 0),
            MinHeight = 36,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
            FontFamily = OverlayVisualResources.Font,
            FontSize = 14,
        };
        OverlayVisualResources.ApplyButtonTemplate(button, 20, _palette.ButtonHover, _palette.Text);
        AutomationProperties.SetName(button, label);
        _actions.Children.Add(button);
        return button;
    }

    internal void Show(Rect anchorDips, Size viewportDips)
    {
        var wasOpen = IsOpen;
        var wasVisible = Surface.Visibility == Visibility.Visible;
        _exit?.Dispose();
        _exit = null;
        IsOpen = true;
        Surface.IsEnabled = true;
        Surface.IsHitTestVisible = true;
        Surface.Visibility = Visibility.Visible;
        MeasureWithin(viewportDips.Width);
        var placement = FloatingToolbarLayout.Place(anchorDips, Surface.DesiredSize, viewportDips);
        Canvas.SetLeft(Surface, placement.X);
        Canvas.SetTop(Surface, placement.Y);
        if (!wasOpen)
            CardTransitions.BeginEntrance(
                Surface, _animationsEnabled(), TimeSpan.FromMilliseconds(220), 0.96,
                preserveCurrentValues: wasVisible);
    }

    private void MeasureWithin(double width)
    {
        var buttons = _actions.Children.OfType<Button>().ToArray();
        foreach (var button in buttons) button.Padding = new Thickness(ActionPadding, 0, ActionPadding, 0);
        Surface.MaxWidth = double.PositiveInfinity;
        Surface.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var overflow = Surface.DesiredSize.Width - width;
        if (overflow > 0 && buttons.Length > 0)
        {
            var padding = Math.Max(CompactActionPadding, ActionPadding - overflow / (2 * buttons.Length));
            foreach (var button in buttons) button.Padding = new Thickness(padding, 0, padding, 0);
        }
        Surface.MaxWidth = Math.Max(0, width);
        Surface.Measure(new Size(Surface.MaxWidth, double.PositiveInfinity));
    }

    internal void Hide(bool animate = true)
    {
        var wasOpen = IsOpen;
        IsOpen = false;
        Surface.IsHitTestVisible = false;
        Surface.IsEnabled = false;
        if (!animate || !_animationsEnabled())
        {
            CompleteExit();
            return;
        }
        if (!wasOpen) return;
        _exit = CardTransitions.BeginExit(
            Surface, animationsEnabled: true, CompleteExit, TimeSpan.FromMilliseconds(150));
    }

    private void CompleteExit()
    {
        _exit?.Dispose();
        _exit = null;
        CardTransitions.Settle(Surface);
        Surface.Visibility = Visibility.Collapsed;
    }
}

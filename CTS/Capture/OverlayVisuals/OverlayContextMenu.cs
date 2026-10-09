using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using CircleToSearch.Ui;

namespace CircleToSearch.Capture;

internal sealed record OverlayContextMenuItem(Geometry Icon, string Title, Action Invoke);

// Drawn inside the overlay like the provider menu, so it keeps the overlay's theme, sounds and input routing.
internal sealed class OverlayContextMenu : IDisposable
{
    private const double PointerGap = 2;
    private static readonly TimeSpan ExitDuration = TimeSpan.FromMilliseconds(150);

    private readonly Grid _root;
    private readonly ColorRoles _roles;
    private readonly Canvas _layer = new();
    private readonly StackPanel _items = new();
    private readonly Border _menu;
    private bool _disposed;

    internal OverlayContextMenu(Grid root, bool lightTheme)
    {
        _root = root;
        _roles = PluginPalette.For(lightTheme).Roles;
        _menu = new Border
        {
            Child = _items,
            Visibility = Visibility.Collapsed,
            Padding = new Thickness(6),
            CornerRadius = new CornerRadius(16),
            Background = OverlayVisualResources.Frozen(_roles.Surface),
            BorderBrush = OverlayVisualResources.Frozen(_roles.Outline),
            BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect
            {
                Color = PluginPalette.OpaqueBlack,
                BlurRadius = 20,
                ShadowDepth = 10,
                Direction = -90,
                Opacity = _roles.ShadowOpacity,
            },
        };
        _layer.Children.Add(_menu);
        Grid.SetRowSpan(_layer, int.MaxValue);
        Grid.SetColumnSpan(_layer, int.MaxValue);
        // Above the bottom controls (2) and the debug panel (3), since it opens from cards inside them.
        Panel.SetZIndex(_layer, 4);
        _root.Children.Add(_layer);
        _root.PreviewMouseDown += OnRootPreviewMouseDown;
        _root.PreviewMouseWheel += OnRootPreviewMouseWheel;
    }

    internal bool IsOpen { get; private set; }

    // Opens beside the point, flipping toward the screen's inside where the menu would not fit.
    internal void Open(UIElement anchor, Point position, IReadOnlyList<OverlayContextMenuItem> items, bool focusFirst)
    {
        if (_disposed || items.Count == 0) return;
        _items.Children.Clear();
        foreach (var item in items) _items.Children.Add(CreateItem(item));
        IsOpen = true;
        _menu.Visibility = Visibility.Visible;
        _menu.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = _menu.DesiredSize;
        var point = anchor.TranslatePoint(position, _layer);
        var flipX = point.X + PointerGap + size.Width > _layer.ActualWidth;
        var flipY = point.Y + PointerGap + size.Height > _layer.ActualHeight;
        var left = flipX ? point.X - PointerGap - size.Width : point.X + PointerGap;
        var top = flipY ? point.Y - PointerGap - size.Height : point.Y + PointerGap;
        Canvas.SetLeft(_menu, Math.Clamp(left, 0, Math.Max(0, _layer.ActualWidth - size.Width)));
        Canvas.SetTop(_menu, Math.Clamp(top, 0, Math.Max(0, _layer.ActualHeight - size.Height)));
        _menu.RenderTransformOrigin = new Point(flipX ? 1 : 0, flipY ? 1 : 0);
        if (focusFirst) _items.Dispatcher.BeginInvoke(() => (_items.Children[0] as Button)?.Focus());
        if (!OverlayVisualResources.AnimationsEnabled())
        {
            _menu.BeginAnimation(UIElement.OpacityProperty, null);
            _menu.Opacity = 1;
            _menu.RenderTransform = Transform.Identity;
            return;
        }
        var scale = new ScaleTransform(0.95, 0.95);
        _menu.RenderTransform = scale;
        _menu.BeginAnimation(UIElement.OpacityProperty,
            OverlayVisualResources.Animate(0, 1, OverlayVisualResources.EntranceDuration));
        scale.BeginAnimation(ScaleTransform.ScaleXProperty,
            OverlayVisualResources.Animate(0.95, 1, OverlayVisualResources.EntranceDuration));
        scale.BeginAnimation(ScaleTransform.ScaleYProperty,
            OverlayVisualResources.Animate(0.95, 1, OverlayVisualResources.EntranceDuration));
    }

    internal void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        if (!OverlayVisualResources.AnimationsEnabled())
        {
            _menu.BeginAnimation(UIElement.OpacityProperty, null);
            _menu.Visibility = Visibility.Collapsed;
            return;
        }
        var fade = OverlayVisualResources.Animate(_menu.Opacity, 0, ExitDuration);
        fade.Completed += (_, _) =>
        {
            if (!IsOpen) _menu.Visibility = Visibility.Collapsed;
        };
        _menu.BeginAnimation(UIElement.OpacityProperty, fade);
        if (_menu.RenderTransform is not ScaleTransform scale || scale.IsFrozen) return;
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, OverlayVisualResources.Animate(scale.ScaleX, 0.95, ExitDuration));
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, OverlayVisualResources.Animate(scale.ScaleY, 0.95, ExitDuration));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        IsOpen = false;
        _root.PreviewMouseDown -= OnRootPreviewMouseDown;
        _root.PreviewMouseWheel -= OnRootPreviewMouseWheel;
        _root.Children.Remove(_layer);
    }

    private Button CreateItem(OverlayContextMenuItem item)
    {
        var icon = new Path
        {
            Data = item.Icon,
            StrokeThickness = 1.65,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
        };
        icon.SetBinding(Shape.StrokeProperty, new Binding(nameof(Control.Foreground))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Control), 1),
        });
        var canvas = new Canvas { Width = 24, Height = 24 };
        canvas.Children.Add(icon);
        var title = new TextBlock
        {
            Text = item.Title,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
            FontFamily = PluginTypography.Font,
            FontSize = PluginTypography.Body,
            FontWeight = FontWeights.Medium,
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(new Viewbox { Width = 20, Height = 20, Child = canvas, IsHitTestVisible = false });
        row.Children.Add(title);
        var button = new Button
        {
            Content = row,
            Padding = new Thickness(12, 4, 12, 4),
            MinHeight = 36,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Foreground = OverlayVisualResources.Frozen(_roles.OnSurface),
            Background = OverlayVisualResources.Frozen(PluginPalette.Transparent),
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            FontFamily = PluginTypography.Font,
        };
        OverlayVisualResources.ApplyButtonTemplate(button, 12, _roles.SecondaryContainer, _roles.OnSecondaryContainer);
        AutomationProperties.SetName(button, item.Title);
        button.Click += (_, e) =>
        {
            e.Handled = true;
            if (!IsOpen) return;
            Close();
            item.Invoke();
        };
        return button;
    }

    // A press outside only dismisses the menu, as in Windows menus; a right press goes through so it can reopen there.
    // Side buttons are left to the overlay, whose Back closes the menu without also closing the card under it.
    private void OnRootPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsOpen || e.ChangedButton is MouseButton.XButton1 or MouseButton.XButton2 ||
            OverlayVisualResources.IsWithin(e.OriginalSource as DependencyObject, _menu)) return;
        Close();
        if (e.ChangedButton != MouseButton.Right) e.Handled = true;
    }

    private void OnRootPreviewMouseWheel(object sender, MouseWheelEventArgs e) => Close();
}

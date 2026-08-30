namespace CircleToSearch.Capture;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using CircleToSearch.Ui;
using CircleToSearch.Ui.Effects;

internal static class OverlayVisualResources
{
    internal static readonly FontFamily Font = new("Segoe UI Variable Text");
    internal static readonly TimeSpan EntranceDuration = TimeSpan.FromMilliseconds(200);

    internal static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    internal static Geometry FrozenGeometry(string data)
    {
        var geometry = Geometry.Parse(data);
        geometry.Freeze();
        return geometry;
    }

    internal static DoubleAnimation Animate(double from, double to, TimeSpan duration) =>
        new(from, to, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };

    internal static bool AnimationsEnabled() =>
        SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast;

    internal static bool HardwareEffectsEnabled() => RenderCapability.Tier >> 16 >= 2;

    internal static Path Icon(Geometry geometry, double size, Color color) => new()
    {
        Data = geometry,
        Width = size,
        Height = size,
        Stretch = Stretch.Uniform,
        Fill = Frozen(color),
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
        IsHitTestVisible = false,
    };

    internal static DropShadowEffect DockShadow(double depth, double opacity) => new()
    {
        Color = PluginPalette.OpaqueBlack,
        BlurRadius = depth == 8 ? 24 : 20,
        ShadowDepth = depth,
        Direction = -90,
        Opacity = opacity,
    };

    internal static void ApplyButtonTemplate(
        Button button,
        double radius,
        Color hoverBackground,
        Color hoverForeground)
    {
        var chrome = new FrameworkElementFactory(typeof(Border), "Chrome");
        chrome.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
        chrome.SetBinding(Border.BackgroundProperty, TemplateBinding(Control.BackgroundProperty));
        chrome.SetBinding(Border.BorderBrushProperty, TemplateBinding(Control.BorderBrushProperty));
        chrome.SetBinding(Border.BorderThicknessProperty, TemplateBinding(Control.BorderThicknessProperty));
        chrome.SetBinding(Border.PaddingProperty, TemplateBinding(Control.PaddingProperty));

        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetBinding(
            FrameworkElement.HorizontalAlignmentProperty,
            TemplateBinding(Control.HorizontalContentAlignmentProperty));
        presenter.SetBinding(
            FrameworkElement.VerticalAlignmentProperty,
            TemplateBinding(Control.VerticalContentAlignmentProperty));
        presenter.SetBinding(ContentPresenter.ContentProperty, TemplateBinding(ContentControl.ContentProperty));
        presenter.SetBinding(ContentPresenter.ContentTemplateProperty, TemplateBinding(ContentControl.ContentTemplateProperty));
        chrome.AppendChild(presenter);

        var template = new ControlTemplate(typeof(Button)) { VisualTree = chrome };
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, Frozen(hoverBackground), "Chrome"));
        hover.Setters.Add(new Setter(Control.ForegroundProperty, Frozen(hoverForeground)));
        template.Triggers.Add(hover);
        var pressed = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(UIElement.OpacityProperty, 0.82));
        template.Triggers.Add(pressed);
        button.Template = template;
        button.FocusVisualStyle = CreateFocusVisualStyle(radius);
    }

    internal static IReadOnlyList<ControlRippleHost> AttachControlRipples(DependencyObject root) =>
        Descendants(root).OfType<Control>().Select(ControlRippleHost.Attach).ToArray();

    private static Style CreateFocusVisualStyle(double radius)
    {
        var ring = new FrameworkElementFactory(typeof(Border));
        ring.SetValue(Border.BorderBrushProperty, Frozen(SystemAccentColor.Read()));
        ring.SetValue(Border.BorderThicknessProperty, new Thickness(2));
        ring.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius + 2));
        ring.SetValue(FrameworkElement.MarginProperty, new Thickness(-2));
        ring.SetValue(UIElement.IsHitTestVisibleProperty, false);
        var template = new ControlTemplate(typeof(Control)) { VisualTree = ring };
        var style = new Style(typeof(Control));
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        return style;
    }

    private static Binding TemplateBinding(DependencyProperty property) => new()
    {
        Path = new PropertyPath(property),
        RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent),
    };

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}

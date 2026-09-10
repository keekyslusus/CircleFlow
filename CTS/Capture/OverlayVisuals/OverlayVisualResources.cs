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
    private static readonly DependencyProperty ButtonShadowEffectProperty = DependencyProperty.RegisterAttached(
        "ButtonShadowEffect",
        typeof(Effect),
        typeof(OverlayVisualResources));

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

    internal static Effect? GetButtonShadow(Button button) =>
        (Effect?)button.GetValue(ButtonShadowEffectProperty);

    internal static void SetButtonShadow(Button button, Effect? effect) =>
        button.SetValue(ButtonShadowEffectProperty, effect);

    internal static void ApplyButtonTemplate(
        Button button,
        double radius,
        Color hoverBackground,
        Color hoverForeground) =>
        ApplyButtonTemplate(button, radius, hoverBackground, hoverForeground, AnimationsEnabled());

    internal static void ApplyButtonTemplate(
        Button button,
        double radius,
        Color hoverBackground,
        Color hoverForeground,
        bool animationsEnabled)
    {
        var shadowEffect = button.Effect;
        button.Effect = null;
        SetButtonShadow(button, shadowEffect);

        var scaleHost = new FrameworkElementFactory(typeof(Grid), "ScaleHost");
        scaleHost.SetValue(UIElement.RenderTransformOriginProperty, new Point(0.5, 0.5));
        scaleHost.SetValue(UIElement.RenderTransformProperty, new ScaleTransform(1, 1));

        if (shadowEffect is not null)
        {
            var shadow = new FrameworkElementFactory(typeof(Border), "Shadow");
            shadow.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
            shadow.SetValue(UIElement.IsHitTestVisibleProperty, false);
            shadow.SetBinding(Border.BackgroundProperty, TemplateBinding(Control.BackgroundProperty));
            shadow.SetBinding(
                UIElement.EffectProperty,
                new Binding
                {
                    Path = new PropertyPath("(0)", ButtonShadowEffectProperty),
                    RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent),
                });
            scaleHost.AppendChild(shadow);
        }

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
        scaleHost.AppendChild(chrome);

        var template = new ControlTemplate(typeof(Button)) { VisualTree = scaleHost };
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, Frozen(hoverBackground), "Chrome"));
        if (shadowEffect is not null)
            hover.Setters.Add(new Setter(Border.BackgroundProperty, Frozen(hoverBackground), "Shadow"));
        hover.Setters.Add(new Setter(Control.ForegroundProperty, Frozen(hoverForeground)));
        template.Triggers.Add(hover);
        var pressed = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(UIElement.OpacityProperty, 0.82));
        if (animationsEnabled)
        {
            pressed.EnterActions.Add(BeginScaleAnimation(0.96, TimeSpan.FromMilliseconds(80)));
            pressed.ExitActions.Add(BeginScaleAnimation(1, TimeSpan.FromMilliseconds(140)));
        }
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

    private static BeginStoryboard BeginScaleAnimation(double to, TimeSpan duration)
    {
        var storyboard = new Storyboard();
        storyboard.Children.Add(ScaleAnimation(ScaleTransform.ScaleXProperty, to, duration));
        storyboard.Children.Add(ScaleAnimation(ScaleTransform.ScaleYProperty, to, duration));
        return new BeginStoryboard
        {
            Storyboard = storyboard,
            HandoffBehavior = HandoffBehavior.SnapshotAndReplace,
        };
    }

    private static DoubleAnimation ScaleAnimation(DependencyProperty property, double to, TimeSpan duration)
    {
        var animation = new DoubleAnimation
        {
            To = to,
            Duration = duration,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTargetName(animation, "ScaleHost");
        Storyboard.SetTargetProperty(
            animation,
            new PropertyPath("(0).(1)", UIElement.RenderTransformProperty, property));
        return animation;
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

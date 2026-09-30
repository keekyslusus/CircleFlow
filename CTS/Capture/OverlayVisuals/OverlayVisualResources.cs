namespace CircleToSearch.Capture;

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
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

    internal static DoubleAnimation Animate(double from, double to, TimeSpan duration) =>
        new(from, to, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };

    internal static bool AnimationsEnabled() => UiAnimationPolicy.Enabled;

    // Remote artwork stays hidden over the card's placeholder until it is decoded, then fades in; a failed
    // download leaves the placeholder. A bitmap that is already loaded shows at once, without a second fade.
    internal static Image FadeInImage(BitmapImage bitmap)
    {
        var image = new Image { Stretch = Stretch.UniformToFill, Source = bitmap, Opacity = bitmap.IsDownloading ? 0 : 1 };
        var revealed = !bitmap.IsDownloading;
        image.ImageFailed += (_, _) =>
        {
            image.BeginAnimation(UIElement.OpacityProperty, null);
            image.Source = null;
            image.Opacity = 0;
        };
        void Reveal()
        {
            if (revealed || !image.IsLoaded || bitmap.IsDownloading || image.Source is null) return;
            revealed = true;
            image.Opacity = 1;
            if (!AnimationsEnabled()) return;
            var fade = Animate(0, 1, TimeSpan.FromMilliseconds(300));
            fade.FillBehavior = FillBehavior.Stop;
            image.BeginAnimation(UIElement.OpacityProperty, fade);
        }
        // Artwork can finish downloading before the card joins the visual tree.
        image.Loaded += (_, _) => Reveal();
        bitmap.DownloadCompleted += (_, _) => Reveal();
        image.Unloaded += (_, _) => image.BeginAnimation(UIElement.OpacityProperty, null);
        return image;
    }

    // Same thin overlay bar as the settings page, so AutoHideScrollbarController can drive it.
    internal static void ApplyAutoHideScrollbar(ScrollViewer scroll, Color thumb)
    {
        var inset = OverlayScrollbarPolicy.EdgeInsetPixels;
        scroll.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse($$"""
            <ControlTemplate TargetType="ScrollViewer"
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Grid ClipToBounds="True">
                <ScrollContentPresenter x:Name="PART_ScrollContentPresenter" Content="{TemplateBinding Content}"
                    CanContentScroll="{TemplateBinding CanContentScroll}"/>
                <ScrollBar x:Name="PART_VerticalScrollBar" HorizontalAlignment="Right" Margin="0,{{inset}},0,{{inset}}"
                    OverridesDefaultStyle="True" MinWidth="0" Width="{{OverlayScrollbarPolicy.TrackWidthPixels}}"
                    Opacity="0" IsHitTestVisible="False" Focusable="False"
                    Maximum="{TemplateBinding ScrollableHeight}" ViewportSize="{TemplateBinding ViewportHeight}"
                    Value="{Binding VerticalOffset, RelativeSource={RelativeSource TemplatedParent}, Mode=OneWay}"
                    Visibility="{TemplateBinding ComputedVerticalScrollBarVisibility}">
                  <ScrollBar.Template>
                    <ControlTemplate TargetType="ScrollBar">
                      <Grid Background="{{PluginPalette.Transparent}}">
                        <Track x:Name="PART_Track" Orientation="Vertical" IsDirectionReversed="True">
                          <Track.Thumb>
                            <Thumb OverridesDefaultStyle="True" MinHeight="{{OverlayScrollbarPolicy.MinimumThumbHeightPixels}}">
                              <Thumb.Template>
                                <ControlTemplate TargetType="Thumb">
                                  <Border Background="{{thumb}}" Width="{{OverlayScrollbarPolicy.ThumbWidthPixels}}"
                                      HorizontalAlignment="Right" CornerRadius="2" Margin="0,0,{{inset}},0"/>
                                </ControlTemplate>
                              </Thumb.Template>
                            </Thumb>
                          </Track.Thumb>
                        </Track>
                      </Grid>
                    </ControlTemplate>
                  </ScrollBar.Template>
                </ScrollBar>
              </Grid>
            </ControlTemplate>
            """);
    }

    internal static bool HardwareEffectsEnabled() => RenderCapability.Tier >> 16 >= 2;

    internal static Image BrandMark(double size, params (Brush Fill, Geometry Geometry)[] layers)
    {
        var drawing = new DrawingGroup();
        // DrawingImage crops to drawn bounds; the frame keeps the marks' shared 24x24 optical sizing.
        drawing.Children.Add(new GeometryDrawing(Frozen(PluginPalette.Transparent), null, BrandMarkFrame));
        foreach (var (fill, geometry) in layers) drawing.Children.Add(new GeometryDrawing(fill, null, geometry));
        drawing.Freeze();
        return new Image
        {
            Source = new DrawingImage(drawing),
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    private static readonly Geometry BrandMarkFrame = CreateBrandMarkFrame();

    private static Geometry CreateBrandMarkFrame()
    {
        var frame = new RectangleGeometry(new Rect(0, 0, 24, 24));
        frame.Freeze();
        return frame;
    }

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

    internal static Button IconButton(
        Geometry geometry,
        string name,
        Color foreground,
        Color hoverBackground,
        Color hoverForeground,
        double iconSize = 15)
    {
        var button = new Button
        {
            Content = Icon(geometry, iconSize, foreground),
            Width = 30,
            Height = 30,
            Margin = new Thickness(2, 0, 0, 0),
            Padding = new Thickness(7.5),
            Foreground = Frozen(foreground),
            Background = Frozen(PluginPalette.Transparent),
            BorderBrush = Frozen(PluginPalette.Transparent),
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            ToolTip = name,
        };
        ApplyButtonTemplate(button, 15, hoverBackground, hoverForeground);
        AutomationProperties.SetName(button, name);
        return button;
    }

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

    // Scrolling containers are controls too, but a press inside them belongs to the tile under the pointer,
    // so a ripple on them would flood the whole viewport.
    internal static IReadOnlyList<ControlRippleHost> AttachControlRipples(DependencyObject root) =>
        Descendants(root).OfType<Control>()
            .Where(control => control is not (ScrollViewer or ScrollBar) && control.TemplatedParent is not ScrollBar)
            .Select(ControlRippleHost.Attach).ToArray();

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

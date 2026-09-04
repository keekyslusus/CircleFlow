namespace CircleToSearch.Capture;

using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using CircleToSearch.Ui;

internal static class TranslationActionVisualPresenter
{
    internal static void SetTranslatingState(
        TranslationActionVisual visual,
        bool translating,
        bool lightTheme,
        bool animationsEnabled)
    {
        var palette = PluginPalette.For(lightTheme).Translation;
        var accent = SystemAccentColor.Read();
        var background = translating
            ? PluginPalette.Composite(palette.Surface, PluginPalette.WithAlpha(accent, 0.16))
            : palette.Surface;
        var border = translating ? PluginPalette.WithAlpha(accent, 0.4) : palette.Border;
        var foreground = translating ? accent : palette.Text;
        visual.Button.Background = TransitionBrush(visual.Button.Background, background, animationsEnabled);
        visual.Button.BorderBrush = TransitionBrush(visual.Button.BorderBrush, border, animationsEnabled);
        visual.Button.Foreground = TransitionBrush(visual.Button.Foreground, foreground, animationsEnabled);
        visual.Icon.Fill = visual.Button.Foreground;
        visual.LoadingIndicator.Fill = visual.Button.Foreground;

        if (translating)
        {
            BeginGlyphEntrance(visual, animationsEnabled);
            BeginActiveHalo(visual, accent, animationsEnabled);
        }
        else
        {
            BeginGlyphExit(visual, animationsEnabled);
            RestoreDockShadow(visual, lightTheme, animationsEnabled);
        }
    }

    private static void BeginGlyphEntrance(TranslationActionVisual visual, bool animationsEnabled)
    {
        visual.LoadingIndicator.Visibility = Visibility.Visible;
        visual.LoadingIndicator.Start(animationsEnabled);
        if (!animationsEnabled)
        {
            SetGlyphState(visual.Icon, opacity: 0, scale: 0.72);
            SetGlyphState(visual.LoadingIndicator, opacity: 1, scale: 1);
            return;
        }

        var duration = TimeSpan.FromMilliseconds(220);
        AnimateGlyph(visual.Icon, opacity: 0, scale: 0.72, duration);
        AnimateGlyph(visual.LoadingIndicator, opacity: 1, scale: 1, duration);
    }

    private static void BeginGlyphExit(TranslationActionVisual visual, bool animationsEnabled)
    {
        visual.LoadingIndicator.BeginStop();
        if (!animationsEnabled)
        {
            SetGlyphState(visual.Icon, opacity: 1, scale: 1);
            SetGlyphState(visual.LoadingIndicator, opacity: 0, scale: 0.72);
            visual.LoadingIndicator.Visibility = Visibility.Collapsed;
            visual.LoadingIndicator.Stop();
            return;
        }

        var duration = TimeSpan.FromMilliseconds(180);
        AnimateGlyph(visual.Icon, opacity: 1, scale: 1, duration);
        AnimateGlyph(visual.LoadingIndicator, opacity: 0, scale: 0.72, duration, () =>
        {
            if (visual.LoadingIndicator.IsRequestedActive) return;
            visual.LoadingIndicator.Visibility = Visibility.Collapsed;
            visual.LoadingIndicator.Stop();
        });
    }

    private static void SetGlyphState(UIElement element, double opacity, double scale)
    {
        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.Opacity = opacity;
        var transform = (ScaleTransform)element.RenderTransform;
        transform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        transform.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        transform.ScaleX = scale;
        transform.ScaleY = scale;
    }

    private static void AnimateGlyph(
        UIElement element,
        double opacity,
        double scale,
        TimeSpan duration,
        Action? completed = null)
    {
        var currentOpacity = element.Opacity;
        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.Opacity = opacity;
        var opacityAnimation = OverlayVisualResources.Animate(currentOpacity, opacity, duration);
        if (completed is not null) opacityAnimation.Completed += (_, _) => completed();
        element.BeginAnimation(UIElement.OpacityProperty, opacityAnimation);

        var transform = (ScaleTransform)element.RenderTransform;
        AnimateScale(transform, ScaleTransform.ScaleXProperty, transform.ScaleX, scale, duration);
        AnimateScale(transform, ScaleTransform.ScaleYProperty, transform.ScaleY, scale, duration);
    }

    private static void AnimateScale(
        ScaleTransform transform,
        DependencyProperty property,
        double current,
        double target,
        TimeSpan duration)
    {
        transform.BeginAnimation(property, null);
        transform.SetValue(property, target);
        transform.BeginAnimation(property, OverlayVisualResources.Animate(current, target, duration));
    }

    private static Brush TransitionBrush(Brush current, Color target, bool animationsEnabled)
    {
        if (!animationsEnabled) return OverlayVisualResources.Frozen(target);
        var from = current is SolidColorBrush solid ? solid.Color : target;
        var brush = new SolidColorBrush(target);
        brush.BeginAnimation(
            SolidColorBrush.ColorProperty,
            new ColorAnimation(from, target, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });
        return brush;
    }

    private static void BeginActiveHalo(
        TranslationActionVisual visual,
        Color accent,
        bool animationsEnabled)
    {
        if (!animationsEnabled) return;
        var halo = new DropShadowEffect
        {
            Color = accent,
            ShadowDepth = 0,
            BlurRadius = 18,
            Opacity = 0.45,
        };
        visual.Button.Effect = halo;
        var entrance = OverlayVisualResources.Animate(0, 0.45, TimeSpan.FromMilliseconds(220));
        entrance.Completed += (_, _) =>
        {
            if (!visual.LoadingIndicator.IsRequestedActive || !ReferenceEquals(visual.Button.Effect, halo)) return;
            BeginHaloPulse(halo);
        };
        halo.BeginAnimation(DropShadowEffect.OpacityProperty, entrance);
        halo.BeginAnimation(
            DropShadowEffect.BlurRadiusProperty,
            OverlayVisualResources.Animate(6, 18, TimeSpan.FromMilliseconds(220)));
    }

    private static void BeginHaloPulse(DropShadowEffect halo)
    {
        var blur = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromMilliseconds(1800),
            RepeatBehavior = RepeatBehavior.Forever,
        };
        blur.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
        blur.KeyFrames.Add(new LinearDoubleKeyFrame(24, KeyTime.FromPercent(1)));
        halo.BeginAnimation(DropShadowEffect.BlurRadiusProperty, blur);
        var fade = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromMilliseconds(1800),
            RepeatBehavior = RepeatBehavior.Forever,
        };
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.45, KeyTime.FromPercent(0)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
        halo.BeginAnimation(DropShadowEffect.OpacityProperty, fade);
    }

    private static void RestoreDockShadow(
        TranslationActionVisual visual,
        bool lightTheme,
        bool animationsEnabled)
    {
        var depth = lightTheme ? 8 : 6;
        var opacity = lightTheme ? 0.3 : 0.35;
        if (!animationsEnabled || visual.Button.Effect is not DropShadowEffect current)
        {
            visual.Button.Effect = OverlayVisualResources.DockShadow(depth, opacity);
            return;
        }

        var from = current.Opacity;
        current.BeginAnimation(DropShadowEffect.OpacityProperty, null);
        current.Opacity = 0;
        var exit = OverlayVisualResources.Animate(from, 0, TimeSpan.FromMilliseconds(180));
        exit.Completed += (_, _) =>
        {
            if (visual.LoadingIndicator.IsRequestedActive || !ReferenceEquals(visual.Button.Effect, current)) return;
            visual.Button.Effect = OverlayVisualResources.DockShadow(depth, opacity);
        };
        current.BeginAnimation(DropShadowEffect.OpacityProperty, exit);
    }
}

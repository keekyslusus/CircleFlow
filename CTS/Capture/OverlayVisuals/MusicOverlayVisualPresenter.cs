namespace CircleToSearch.Capture;

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using CircleToSearch.MusicRecognition;
using CircleToSearch.Ui;

internal static class MusicOverlayVisualPresenter
{
    internal static void SetListeningState(MusicOverlayVisual visual, bool listening, bool lightTheme)
        => SetListeningState(
            visual,
            listening,
            lightTheme,
            OverlayVisualResources.AnimationsEnabled());

    internal static void SetListeningState(
        MusicOverlayVisual visual,
        bool listening,
        bool lightTheme,
        bool animationsEnabled)
    {
        var theme = PluginPalette.For(lightTheme);
        var accent = SystemAccentColor.Read();
        var background = listening
            ? PluginPalette.Composite(theme.MusicButton.Surface, PluginPalette.WithAlpha(accent, 0.16))
            : theme.MusicButton.Surface;
        var border = listening ? PluginPalette.WithAlpha(accent, 0.4) : theme.MusicButton.Border;
        var foreground = listening ? accent : theme.MusicButton.Foreground;
        visual.Button.Background = TransitionBrush(visual.Button.Background, background, animationsEnabled);
        visual.Button.BorderBrush = TransitionBrush(visual.Button.BorderBrush, border, animationsEnabled);
        visual.Button.Foreground = TransitionBrush(visual.Button.Foreground, foreground, animationsEnabled);
        visual.Icon.Fill = visual.Button.Foreground;
        visual.LoadingIndicator.Fill = visual.Button.Foreground;

        if (!listening)
        {
            BeginGlyphExit(visual, animationsEnabled);
            RestoreDockShadow(visual, lightTheme, animationsEnabled);
            return;
        }

        BeginGlyphEntrance(visual, animationsEnabled);
        if (animationsEnabled) BeginListeningHalo(visual, accent);
    }

    private static void BeginGlyphEntrance(MusicOverlayVisual visual, bool animationsEnabled)
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

    private static void BeginGlyphExit(MusicOverlayVisual visual, bool animationsEnabled)
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

    private static void BeginListeningHalo(MusicOverlayVisual visual, Color accent)
    {
        var halo = new DropShadowEffect
        {
            Color = accent,
            ShadowDepth = 0,
            BlurRadius = 18,
            Opacity = 0.45,
        };
        OverlayVisualResources.SetButtonShadow(visual.Button, halo);
        var entrance = new DoubleAnimation(0, 0.45, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        entrance.Completed += (_, _) =>
        {
            if (!visual.LoadingIndicator.IsRequestedActive ||
                !ReferenceEquals(OverlayVisualResources.GetButtonShadow(visual.Button), halo)) return;
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
        MusicOverlayVisual visual,
        bool lightTheme,
        bool animationsEnabled)
    {
        var depth = lightTheme ? 8 : 6;
        var opacity = lightTheme ? 0.3 : 0.35;
        if (!animationsEnabled || OverlayVisualResources.GetButtonShadow(visual.Button) is not DropShadowEffect current)
        {
            OverlayVisualResources.SetButtonShadow(visual.Button, OverlayVisualResources.DockShadow(depth, opacity));
            return;
        }

        var from = current.Opacity;
        current.BeginAnimation(DropShadowEffect.OpacityProperty, null);
        current.Opacity = 0;
        var exit = OverlayVisualResources.Animate(from, 0, TimeSpan.FromMilliseconds(180));
        exit.Completed += (_, _) =>
        {
            if (visual.LoadingIndicator.IsRequestedActive ||
                !ReferenceEquals(OverlayVisualResources.GetButtonShadow(visual.Button), current)) return;
            OverlayVisualResources.SetButtonShadow(visual.Button, OverlayVisualResources.DockShadow(depth, opacity));
        };
        current.BeginAnimation(DropShadowEffect.OpacityProperty, exit);
    }

    internal static FrameworkElement PresentResult(
        MusicOverlayVisual visual,
        MusicRecognitionOutcome outcome,
        UiStrings strings,
        bool lightTheme,
        Action<IOverlayCommand> publish,
        Action<string, Button> copy)
    {
        visual.ResultHost.Children.Clear();
        var theme = PluginPalette.For(lightTheme);
        Border card;
        if (outcome.Status == MusicRecognitionStatus.Matched && outcome.Recognition is { } recognition)
        {
            card = MusicResultCardVisualFactory.Create(recognition, lightTheme, strings, publish, copy,
                visual.ResultHost.MaxWidth);
        }
        else
        {
            var options = CreateStateCardOptions(outcome.Status, strings, publish);
            card = StateCardVisualFactory.Create(options, theme.StateCard).Card;
        }
        visual.ResultHost.Children.Add(card);
        visual.ResultHost.Visibility = Visibility.Visible;
        return card;
    }

    internal static void SetCopyConfirmed(Button button, bool confirmed, UiStrings strings, bool lightTheme)
    {
        var palette = PluginPalette.For(lightTheme).MusicOverlay;
        button.Content = OverlayVisualResources.Icon(
            confirmed ? PluginIcons.CheckFilled : PluginIcons.CopyFilled,
            12,
            confirmed ? palette.Primary : palette.MutedText);
        var name = confirmed ? strings.Copied : strings.CopyTrackInfo;
        button.ToolTip = name;
        AutomationProperties.SetName(button, name);
    }

    private static StateCardOptions CreateStateCardOptions(
        MusicRecognitionStatus status,
        UiStrings strings,
        Action<IOverlayCommand> publish)
    {
        var (message, icon, actionLabel) = status switch
        {
            MusicRecognitionStatus.NoMatch => (strings.MusicNoMatch, PluginIcons.MusicOffFilled, strings.TryAgain),
            MusicRecognitionStatus.NoAudio => (strings.MusicNoAudio, PluginIcons.NoSoundFilled, strings.Retry),
            MusicRecognitionStatus.RateLimited => (strings.MusicRateLimited, PluginIcons.NoSoundFilled, null),
            MusicRecognitionStatus.DeviceError => (strings.MusicDeviceError, PluginIcons.NoSoundFilled, strings.Retry),
            MusicRecognitionStatus.ServiceError => (strings.MusicNetworkError, PluginIcons.NoSoundFilled, strings.Retry),
            _ => (strings.MusicNetworkError, PluginIcons.NoSoundFilled, strings.Retry),
        };
        var action = actionLabel is null
            ? null
            : new StateCardAction(actionLabel, () => publish(new RetryMusicRecognition()));
        return new StateCardOptions(
            icon,
            message,
            strings.MusicResultTitle,
            strings.Close,
            () => publish(new DismissMusicResult()),
            action);
    }
}

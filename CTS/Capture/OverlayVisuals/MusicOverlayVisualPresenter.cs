namespace CircleToSearch.Capture;

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using CircleToSearch.MusicRecognition;
using CircleToSearch.Ui;

internal static class MusicOverlayVisualPresenter
{
    internal static readonly Geometry CloseIconGeometry = OverlayVisualResources.FrozenGeometry(
        "M19 6.41 17.59 5 12 10.59 6.41 5 5 6.41 10.59 12 5 17.59 6.41 19 12 13.41 17.59 19 19 17.59 13.41 12Z");
    private static readonly Geometry LinkIconGeometry = OverlayVisualResources.FrozenGeometry(
        "M3.9 12c0-1.71 1.39-3.1 3.1-3.1h4V7H7c-2.76 0-5 2.24-5 5s2.24 5 5 5h4v-1.9H7c-1.71 0-3.1-1.39-3.1-3.1ZM8 13h8v-2H8v2Zm9-6h-4v1.9h4c1.71 0 3.1 1.39 3.1 3.1s-1.39 3.1-3.1 3.1h-4V17h4c2.76 0 5-2.24 5-5s-2.24-5-5-5Z");
    internal static readonly Geometry CopyIconGeometry = OverlayVisualResources.FrozenGeometry(
        "M16 1H4c-1.1 0-2 .9-2 2v14h2V3h12V1Zm3 4H8c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h10c1.1 0 2-.9 2-2V7c0-1.1-.9-2-2-2Zm0 16H8V7h10v14Z");
    internal static readonly Geometry CheckIconGeometry = OverlayVisualResources.FrozenGeometry(
        "M9 16.17 4.83 12l-1.42 1.41L9 19 21 7l-1.41-1.41Z");
    private static readonly Geometry NoSoundIconGeometry = OverlayVisualResources.FrozenGeometry(
        "M611-323l-43-43 114-113-114-113 43-43 113 114 113-114 43 43-114 113 114 113-43 43-113-114-113 114ZM120-360v-240h160l200-200v640L280-360H120Zm300-288L307-540H180v120h127l113 109v-337ZM311-481Z");
    private static readonly Geometry MusicOffIconGeometry = OverlayVisualResources.FrozenGeometry(
        "M806-56 57-805l43-43L849-99l-43 43ZM546-487l-60-60v-293h234v135H546v218ZM396-120q-63 0-106.5-43.5T246-270q0-63 43.5-106.5T396-420q28 0 50.5 8t39.5 22v-72l60 60v132q0 63-43.5 106.5T396-120Z");

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
            visual.ListeningLayer.BeginAnimation(UIElement.OpacityProperty, null);
            visual.ListeningLayer.Opacity = 0;
            visual.ListeningLayer.Visibility = Visibility.Collapsed;
            RestoreDockShadow(visual, lightTheme, animationsEnabled);
            return;
        }

        BeginGlyphEntrance(visual, animationsEnabled);
        visual.ListeningLayer.Visibility = Visibility.Visible;
        if (!animationsEnabled)
        {
            visual.ListeningLayer.Opacity = 1;
            return;
        }
        visual.ListeningLayer.BeginAnimation(
            UIElement.OpacityProperty,
            OverlayVisualResources.Animate(0, 1, TimeSpan.FromMilliseconds(220)));
        BeginListeningHalo(visual, accent);
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
        var palette = PluginPalette.For(lightTheme).MusicOverlay;
        FrameworkElement content;
        CornerRadius radius;
        Thickness padding;
        double? width;
        double? height;
        if (outcome.Status == MusicRecognitionStatus.Matched && outcome.Recognition is { } recognition)
        {
            content = CreateMatchPill(recognition, palette, strings, publish, copy);
            radius = new CornerRadius(24);
            padding = new Thickness(14, 0, 6, 0);
            width = null;
            height = 48;
        }
        else
        {
            content = CreateStateCard(outcome.Status, palette, strings, publish);
            radius = new CornerRadius(18);
            padding = new Thickness(14, 12, 14, 12);
            width = 340;
            height = null;
        }
        var card = new Border
        {
            Child = content,
            Background = OverlayVisualResources.Frozen(palette.Surface),
            BorderBrush = OverlayVisualResources.Frozen(palette.Border),
            BorderThickness = new Thickness(1),
            CornerRadius = radius,
            Padding = padding,
            MaxWidth = 540,
            Effect = new DropShadowEffect
            {
                Color = PluginPalette.OpaqueBlack,
                BlurRadius = 20,
                ShadowDepth = 10,
                Direction = -90,
                Opacity = palette.ShadowOpacity,
            },
        };
        if (width is { } fixedWidth) card.Width = fixedWidth;
        if (height is { } fixedHeight) card.Height = fixedHeight;
        AutomationProperties.SetName(card, strings.MusicResultTitle);
        visual.ResultHost.Children.Add(card);
        visual.ResultHost.Visibility = Visibility.Visible;
        return card;
    }

    internal static void SetCopyConfirmed(Button button, bool confirmed, UiStrings strings, bool lightTheme)
    {
        var palette = PluginPalette.For(lightTheme).MusicOverlay;
        button.Content = OverlayVisualResources.Icon(
            confirmed ? CheckIconGeometry : CopyIconGeometry,
            15,
            palette.MutedText);
        var name = confirmed ? strings.Copied : strings.CopyTrackInfo;
        button.ToolTip = name;
        AutomationProperties.SetName(button, name);
    }

    private static FrameworkElement CreateMatchPill(
        MusicRecognition.Shazam.ShazamRecognition recognition,
        MusicOverlayPalette palette,
        UiStrings strings,
        Action<IOverlayCommand> publish,
        Action<string, Button> copy)
    {
        var row = new DockPanel { LastChildFill = true };
        var close = IconButton(CloseIconGeometry, strings.Close, palette, iconSize: 12);
        close.Margin = new Thickness(0);
        close.Click += (_, _) => publish(new DismissMusicResult());
        DockPanel.SetDock(close, Dock.Right);
        row.Children.Add(close);
        if (Search.MusicResultPresenter.IsSafeShazamUrl(recognition.ShazamUrl))
        {
            var open = IconButton(LinkIconGeometry, strings.OpenInShazam, palette);
            open.Click += (_, _) => publish(new OpenMusicResult());
            DockPanel.SetDock(open, Dock.Right);
            row.Children.Add(open);
        }
        var copyButton = IconButton(CopyIconGeometry, strings.CopyTrackInfo, palette);
        copyButton.Click += (_, _) => copy($"{recognition.Title} - {recognition.Artist}", copyButton);
        DockPanel.SetDock(copyButton, Dock.Right);
        row.Children.Add(copyButton);
        var note = OverlayVisualResources.Icon(MusicOverlayVisualFactory.MusicIconGeometry, 16, palette.Primary);
        note.Margin = new Thickness(0, 0, 6, 0);
        DockPanel.SetDock(note, Dock.Left);
        row.Children.Add(note);
        var text = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            FontFamily = OverlayVisualResources.Font,
            FontSize = 13,
            Foreground = OverlayVisualResources.Frozen(palette.Text),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 390,
            Margin = new Thickness(0, 0, 4, 0),
        };
        text.Inlines.Add(new System.Windows.Documents.Run(recognition.Title) { FontWeight = FontWeights.SemiBold });
        text.Inlines.Add(new System.Windows.Documents.Run($" - {recognition.Artist}")
        {
            Foreground = OverlayVisualResources.Frozen(palette.MutedText),
        });
        row.Children.Add(text);
        return row;
    }

    private static FrameworkElement CreateStateCard(
        MusicRecognitionStatus status,
        MusicOverlayPalette palette,
        UiStrings strings,
        Action<IOverlayCommand> publish)
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var close = IconButton(CloseIconGeometry, strings.Close, palette, iconSize: 12);
        close.HorizontalAlignment = HorizontalAlignment.Right;
        close.VerticalAlignment = VerticalAlignment.Top;
        close.Margin = new Thickness(0, -4, -6, 0);
        close.Click += (_, _) => publish(new DismissMusicResult());
        Panel.SetZIndex(close, 1);
        root.Children.Add(close);
        var message = status switch
        {
            MusicRecognitionStatus.NoMatch => strings.MusicNoMatch,
            MusicRecognitionStatus.NoAudio => strings.MusicNoAudio,
            MusicRecognitionStatus.RateLimited => strings.MusicRateLimited,
            MusicRecognitionStatus.DeviceError => strings.MusicDeviceError,
            _ => strings.MusicNetworkError,
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new Border
        {
            Width = 44,
            Height = 44,
            CornerRadius = new CornerRadius(22),
            Background = OverlayVisualResources.Frozen(palette.SecondaryContainer),
            Child = OverlayVisualResources.Icon(
                status == MusicRecognitionStatus.NoMatch ? MusicOffIconGeometry : NoSoundIconGeometry,
                22,
                palette.OnSecondaryContainer),
        });
        row.Children.Add(new TextBlock
        {
            Text = message,
            FontFamily = OverlayVisualResources.Font,
            FontSize = 13,
            LineHeight = 19,
            TextWrapping = TextWrapping.Wrap,
            Foreground = OverlayVisualResources.Frozen(palette.Text),
            Width = 230,
            Margin = new Thickness(12, 2, 26, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        Grid.SetRow(row, 0);
        root.Children.Add(row);
        if (status != MusicRecognitionStatus.RateLimited)
        {
            var retry = TextPillButton(
                status == MusicRecognitionStatus.NoMatch ? strings.TryAgain : strings.Retry,
                palette);
            retry.HorizontalAlignment = HorizontalAlignment.Right;
            retry.Margin = new Thickness(0, 10, 0, 0);
            retry.Click += (_, _) => publish(new RetryMusicRecognition());
            Grid.SetRow(retry, 1);
            root.Children.Add(retry);
        }
        return root;
    }

    internal static Button IconButton(
        Geometry geometry,
        string name,
        MusicOverlayPalette palette,
        double iconSize = 15)
    {
        var button = new Button
        {
            Content = OverlayVisualResources.Icon(geometry, iconSize, palette.MutedText),
            Width = 30,
            Height = 30,
            Margin = new Thickness(2, 0, 0, 0),
            Padding = new Thickness(7.5),
            Foreground = OverlayVisualResources.Frozen(palette.MutedText),
            Background = OverlayVisualResources.Frozen(PluginPalette.Transparent),
            BorderBrush = OverlayVisualResources.Frozen(PluginPalette.Transparent),
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            ToolTip = name,
        };
        OverlayVisualResources.ApplyButtonTemplate(
            button, 15, palette.SecondaryContainer, palette.OnSecondaryContainer);
        AutomationProperties.SetName(button, name);
        return button;
    }

    private static Button TextPillButton(string label, MusicOverlayPalette palette)
    {
        var button = new Button
        {
            Content = label,
            Height = 34,
            Padding = new Thickness(14, 0, 14, 0),
            FontFamily = OverlayVisualResources.Font,
            FontSize = 12.5,
            FontWeight = FontWeights.Medium,
            Foreground = OverlayVisualResources.Frozen(palette.OnPrimaryContainer),
            Background = OverlayVisualResources.Frozen(palette.PrimaryContainer),
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
        };
        OverlayVisualResources.ApplyButtonTemplate(
            button, 17, palette.SecondaryContainer, palette.OnSecondaryContainer);
        AutomationProperties.SetName(button, label);
        return button;
    }
}

namespace CircleToSearch.Capture;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Data;
using CircleToSearch.Ui;
using CircleToSearch.Search;
using CircleToSearch.Ui.Effects;
using CircleToSearch.MusicRecognition;

public sealed record OverlayVisual(
    bool LightTheme,
    Grid Root,
    Image Screenshot,
    Path Dim,
    Path DimRect,
    Path Sheen,
    Polyline Halo,
    Polyline Accent,
    Path SelectionFrame,
    Grid SelectionInputSurface,
    Canvas SceneRippleLayer,
    SceneRippleHost SceneRipples,
    StackPanel ListeningLayer,
    AudioWaveformVisual Waveform,
    Grid ActionUiRoot,
    StackPanel ActionTray,
    Border Chip,
    Button? ProviderButton,
    ContentControl? ProviderContent,
    Path? ProviderChevron,
    Border ProviderMenu,
    Button MusicButton,
    Path MusicIcon,
    Grid ResultHost,
    Border DebugPanel,
    StackPanel DebugScenarioButtons,
    TranslateTransform ActionTrayLift);

// Composes the overlay visual tree layer by layer; the window only keeps references.
public static class OverlayVisualFactory
{
    private const double HaloThickness = 12;
    private const double HaloBlurRadius = 8;
    private const double AccentThickness = 2.5;
    private const double DimBlurRadius = 28;
    // The reveal's outer figure must sit well past the window edges, or the blur softens
    // the screen borders instead of just the selection boundary.
    private const double RevealBleed = 96;
    private const double SheenBlurRadius = 14;
    private const double FrameCornerRadius = 6;
    private const double FrameGlowRadius = 18;
    private const double ChipEntranceLift = 24;

    // Tweak point for the chip outline width, in DIPs.
    private const double ChipBorderThicknessDips = 1;
    private static readonly TimeSpan EntranceDuration = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan ExitDuration = TimeSpan.FromMilliseconds(160);
    private static readonly TimeSpan RevealDuration = TimeSpan.FromMilliseconds(150);

    public static OverlayVisual CreateRoot(
        BitmapSource? frame,
        Size size,
        double chipBottomMargin,
        UiStrings strings) =>
        CreateRoot(frame, size, chipBottomMargin, SystemTheme.IsLight(), strings);

    internal static OverlayVisual CreateRoot(
        BitmapSource? frame,
        Size size,
        double chipBottomMargin,
        bool lightTheme,
        UiStrings strings) =>
        CreateRoot(frame, size, chipBottomMargin, lightTheme, strings, [], null);

    internal static OverlayVisual CreateRoot(
        BitmapSource? frame,
        Size size,
        double chipBottomMargin,
        bool lightTheme,
        UiStrings strings,
        IReadOnlyList<SearchProviderDescriptor> providers,
        string? selectedProviderId)
    {
        var screenshot = new Image { Source = frame, Stretch = Stretch.Fill, IsHitTestVisible = false };

        // Blurring the dim itself is what melts the boundary between the dimmed desktop
        // and the revealed lasso interior into a wide gradient.
        var dim = new Path
        {
            Fill = Frozen(PluginPalette.SelectionDim),
            Data = BuildRevealGeometry(size, []),
            IsHitTestVisible = false,
        };
        if (HardwareEffectsEnabled()) dim.Effect = new BlurEffect { Radius = DimBlurRadius };

        // Final-rectangle twin of the dim layer; the window cross-fades Dim into it on
        // mouse-up. Linear opacities sum to a constant dim, so the retraction from the
        // lasso outline to the rectangle reads as one seamless move.
        var dimRect = new Path
        {
            Fill = Frozen(PluginPalette.SelectionDim),
            Data = Geometry.Empty,
            Opacity = 0,
            IsHitTestVisible = false,
        };
        if (HardwareEffectsEnabled()) dimRect.Effect = new BlurEffect { Radius = DimBlurRadius };

        // Light translucent fill inside the lasso; the blur softens its contours so the
        // interior glows instead of showing a hard polygon edge.
        var sheen = new Path
        {
            Fill = Frozen(PluginPalette.SelectionSheen),
            Data = Geometry.Empty,
            IsHitTestVisible = false,
        };
        if (HardwareEffectsEnabled()) sheen.Effect = new BlurEffect { Radius = SheenBlurRadius };

        var accentColor = SystemAccentColor.Read();
        // Blurred so the halo reads as a soft light glow under the crisp accent line
        // instead of a hard-edged band on the selection boundary.
        var halo = new Polyline
        {
            Stroke = Frozen(PluginPalette.SelectionHalo),
            StrokeThickness = HaloThickness,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            IsHitTestVisible = false,
        };
        if (HardwareEffectsEnabled()) halo.Effect = new BlurEffect { Radius = HaloBlurRadius };

        var accent = new Polyline
        {
            Stroke = Frozen(accentColor),
            StrokeThickness = AccentThickness,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            IsHitTestVisible = false,
        };

        var selectionFrame = new Path
        {
            Fill = Frozen(PluginPalette.SelectionFrameFill),
            Stroke = Frozen(accentColor),
            StrokeThickness = AccentThickness,
            Opacity = 0,
            IsHitTestVisible = false,
            Effect = new DropShadowEffect
            {
                Color = accentColor,
                BlurRadius = FrameGlowRadius,
                ShadowDepth = 0,
                Opacity = 0.7,
            },
        };

        var palette = PluginPalette.For(lightTheme);
        var lift = new TranslateTransform();
        var chip = CreateChip(palette.SelectionChip, strings);
        ContentControl? providerContent = null;
        Button? providerButton = null;
        Path? providerChevron = null;
        if (providers.Count > 0)
        {
            var selected = providers.First(provider =>
                string.Equals(provider.Id, selectedProviderId, StringComparison.OrdinalIgnoreCase));
            providerContent = new ContentControl
            {
                Content = ProviderVisualCatalog.Create(selected, strings, lightTheme),
                IsHitTestVisible = false,
            };
            (providerButton, providerChevron) = CreateProviderButton(
                providerContent,
                selected,
                palette.Provider,
                strings);
        }
        var (musicButton, musicIcon) = CreateMusicButton(palette.MusicButton, strings);
        var tray = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, chipBottomMargin),
            RenderTransform = lift,
        };
        tray.Children.Add(chip);
        if (providerButton is not null) tray.Children.Add(providerButton);
        tray.Children.Add(musicButton);

        var providerMenu = CreateProviderMenu(
            providers,
            selectedProviderId,
            palette.Provider,
            lightTheme,
            strings);
        var providerMenuLayer = new Canvas();
        providerMenuLayer.Children.Add(providerMenu);
        var actionUiRoot = new Grid { IsHitTestVisible = true };
        actionUiRoot.Children.Add(tray);
        actionUiRoot.Children.Add(providerMenuLayer);
        Panel.SetZIndex(actionUiRoot, 2);

        var waveform = new AudioWaveformVisual(lightTheme);
        var listeningLayer = new StackPanel
        {
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, size.Height * 0.45 - 34, 0, 0),
            Opacity = 0,
            IsHitTestVisible = false,
        };
        listeningLayer.Children.Add(waveform);
        listeningLayer.Children.Add(new TextBlock
        {
            Text = strings.Listening,
            Foreground = Frozen(palette.MusicOverlay.Text),
            HorizontalAlignment = HorizontalAlignment.Center,
            FontSize = 14,
            FontWeight = FontWeights.Medium,
            FontFamily = OverlayFont,
            Margin = new Thickness(0, 10, 0, 0),
            Effect = new DropShadowEffect
            {
                Color = PluginPalette.OpaqueBlack,
                BlurRadius = 10,
                ShadowDepth = 1,
                Opacity = 0.4,
            },
        });

        var sceneRippleLayer = new Canvas { IsHitTestVisible = false };
        var sceneRipples = new SceneRippleHost(sceneRippleLayer);
        var selectionInputSurface = new Grid
        {
            Background = Frozen(PluginPalette.Transparent),
            IsHitTestVisible = true,
        };
        var resultHost = new Grid
        {
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, chipBottomMargin + 60),
        };
        Panel.SetZIndex(resultHost, 1);
        var (debugPanel, debugScenarioButtons) = CreateMusicDebugPanel(palette.MusicOverlay, strings);
        Panel.SetZIndex(debugPanel, 3);

        var root = new Grid();
        root.Children.Add(screenshot);
        root.Children.Add(dim);
        root.Children.Add(dimRect);
        root.Children.Add(sheen);
        root.Children.Add(halo);
        root.Children.Add(accent);
        root.Children.Add(selectionFrame);
        root.Children.Add(selectionInputSurface);
        root.Children.Add(sceneRippleLayer);
        root.Children.Add(listeningLayer);
        root.Children.Add(actionUiRoot);
        root.Children.Add(resultHost);
        root.Children.Add(debugPanel);

        return new OverlayVisual(
            lightTheme,
            root,
            screenshot,
            dim,
            dimRect,
            sheen,
            halo,
            accent,
            selectionFrame,
            selectionInputSurface,
            sceneRippleLayer,
            sceneRipples,
            listeningLayer,
            waveform,
            actionUiRoot,
            tray,
            chip,
            providerButton,
            providerContent,
            providerChevron,
            providerMenu,
            musicButton,
            musicIcon,
            resultHost,
            debugPanel,
            debugScenarioButtons,
            lift);
    }

    private static (Border Panel, StackPanel Buttons) CreateMusicDebugPanel(
        MusicOverlayPalette palette,
        UiStrings strings)
    {
        (MusicDebugScenario Scenario, string Label)[] scenarios =
        [
            (MusicDebugScenario.Live, strings.DebugMusicLive),
            (MusicDebugScenario.Matched, strings.DebugMusicMatched),
            (MusicDebugScenario.NoMatch, strings.DebugMusicNoMatch),
            (MusicDebugScenario.NoAudio, strings.DebugMusicNoAudio),
            (MusicDebugScenario.DeviceError, strings.DebugMusicDeviceError),
            (MusicDebugScenario.ServiceError, strings.DebugMusicServiceError),
            (MusicDebugScenario.RateLimited, strings.DebugMusicRateLimited),
        ];
        var buttons = new StackPanel();
        foreach (var (scenario, label) in scenarios)
        {
            var button = new Button
            {
                Content = label,
                Tag = scenario,
                Foreground = Frozen(palette.Text),
                Background = Frozen(PluginPalette.Transparent),
                BorderBrush = Frozen(PluginPalette.Transparent),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 1, 0, 1),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                FontFamily = OverlayFont,
                FontSize = 12,
                Cursor = Cursors.Hand,
            };
            ApplyButtonTemplate(button, 6, palette.SecondaryContainer, palette.OnSecondaryContainer);
            AutomationProperties.SetName(button, label);
            buttons.Children.Add(button);
        }

        var content = new StackPanel();
        content.Children.Add(new TextBlock
        {
            Text = strings.DebugMusicTitle,
            Foreground = Frozen(palette.Text),
            FontFamily = OverlayFont,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8),
        });
        content.Children.Add(buttons);

        var panel = new Border
        {
            Child = content,
            Visibility = Visibility.Collapsed,
            Width = 240,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(24),
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(10),
            Background = Frozen(palette.Surface),
            BorderBrush = Frozen(palette.Border),
            BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect
            {
                Color = PluginPalette.OpaqueBlack,
                BlurRadius = 16,
                ShadowDepth = 6,
                Direction = -90,
                Opacity = palette.ShadowOpacity,
            },
        };
        SetMusicDebugScenario(buttons, palette, MusicDebugScenario.Live);
        return (panel, buttons);
    }

    internal static void SetMusicDebugScenario(OverlayVisual visual, MusicDebugScenario scenario) =>
        SetMusicDebugScenario(
            visual.DebugScenarioButtons,
            PluginPalette.For(visual.LightTheme).MusicOverlay,
            scenario);

    private static void SetMusicDebugScenario(
        StackPanel buttons,
        MusicOverlayPalette palette,
        MusicDebugScenario scenario)
    {
        foreach (var button in buttons.Children.OfType<Button>())
        {
            var selected = Equals(button.Tag, scenario);
            button.Background = Frozen(selected
                ? palette.PrimaryContainer
                : PluginPalette.Transparent);
            button.Foreground = Frozen(selected
                ? palette.OnPrimaryContainer
                : palette.Text);
        }
    }

    internal static void UpdateProvider(
        OverlayVisual visual,
        IReadOnlyList<SearchProviderDescriptor> providers,
        string selectedProviderId,
        UiStrings strings,
        bool lightTheme)
    {
        if (visual.ProviderContent is null || visual.ProviderButton is null) return;
        var selected = providers.First(provider =>
            string.Equals(provider.Id, selectedProviderId, StringComparison.OrdinalIgnoreCase));
        visual.ProviderContent.Content = ProviderVisualCatalog.Create(selected, strings, lightTheme);
        visual.ProviderButton.ToolTip = strings.SelectSearchProvider(selected.DisplayName);
        AutomationProperties.SetName(visual.ProviderButton, strings.SelectSearchProvider(selected.DisplayName));
        var panel = (StackPanel)((Border)visual.ProviderMenu).Child;
        panel.Children.Clear();
        AddProviderMenuItems(panel, providers, selectedProviderId, lightTheme, strings);
    }

    internal static void SetProviderMenuOpen(OverlayVisual visual, bool open)
    {
        if (!open)
        {
            visual.ProviderMenu.Tag = false;
            AnimateChevron(visual.ProviderChevron, 0);
            if (visual.ProviderMenu.Visibility != Visibility.Visible) return;
            if (!AnimationsEnabled())
            {
                visual.ProviderMenu.Visibility = Visibility.Collapsed;
                return;
            }
            var duration = TimeSpan.FromMilliseconds(150);
            var fade = Animate(1, 0, duration);
            fade.Completed += (_, _) =>
            {
                if (visual.ProviderMenu.Tag is false) visual.ProviderMenu.Visibility = Visibility.Collapsed;
            };
            visual.ProviderMenu.BeginAnimation(UIElement.OpacityProperty, fade);
            var transform = visual.ProviderMenu.RenderTransform as ScaleTransform ?? new ScaleTransform(1, 1);
            visual.ProviderMenu.RenderTransform = transform;
            transform.BeginAnimation(ScaleTransform.ScaleXProperty, Animate(1, 0.95, duration));
            transform.BeginAnimation(ScaleTransform.ScaleYProperty, Animate(1, 0.95, duration));
            return;
        }
        visual.ProviderMenu.Tag = true;
        AnimateChevron(visual.ProviderChevron, 180);
        visual.ProviderMenu.Visibility = Visibility.Visible;
        if (visual.ProviderButton is not null && visual.ProviderButton.IsLoaded)
        {
            visual.ProviderMenu.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var anchor = visual.ProviderButton.TranslatePoint(
                new Point(visual.ProviderButton.ActualWidth / 2, 0),
                visual.ActionUiRoot);
            var left = Math.Clamp(
                anchor.X - visual.ProviderMenu.DesiredSize.Width / 2,
                0,
                Math.Max(0, visual.ActionUiRoot.ActualWidth - visual.ProviderMenu.DesiredSize.Width));
            var top = Math.Clamp(
                anchor.Y - 10 - visual.ProviderMenu.DesiredSize.Height,
                0,
                Math.Max(0, visual.ActionUiRoot.ActualHeight - visual.ProviderMenu.DesiredSize.Height));
            Canvas.SetLeft(visual.ProviderMenu, left);
            Canvas.SetTop(visual.ProviderMenu, top);
        }
        if (!AnimationsEnabled())
        {
            visual.ProviderMenu.Opacity = 1;
            return;
        }
        visual.ProviderMenu.Opacity = 0;
        visual.ProviderMenu.RenderTransform = new ScaleTransform(0.95, 0.95);
        visual.ProviderMenu.BeginAnimation(UIElement.OpacityProperty, Animate(0, 1, EntranceDuration));
        ((ScaleTransform)visual.ProviderMenu.RenderTransform).BeginAnimation(
            ScaleTransform.ScaleXProperty, Animate(0.95, 1, EntranceDuration));
        ((ScaleTransform)visual.ProviderMenu.RenderTransform).BeginAnimation(
            ScaleTransform.ScaleYProperty, Animate(0.95, 1, EntranceDuration));
    }

    private static void AnimateChevron(Path? chevron, double angle)
    {
        if (chevron?.RenderTransform is not RotateTransform rotation) return;
        if (!AnimationsEnabled())
        {
            rotation.Angle = angle;
            return;
        }
        rotation.BeginAnimation(
            RotateTransform.AngleProperty,
            Animate(rotation.Angle, angle, TimeSpan.FromMilliseconds(160)));
    }

    internal static IReadOnlyList<ControlRippleHost> AttachControlRipples(OverlayVisual visual)
        => AttachControlRipples(visual.ActionUiRoot);

    internal static void SetListeningState(OverlayVisual visual, bool listening)
    {
        var theme = PluginPalette.For(visual.LightTheme);
        var accent = SystemAccentColor.Read();
        visual.MusicButton.Background = Frozen(listening
            ? PluginPalette.WithAlpha(accent, 0.16)
            : theme.MusicButton.Surface);
        visual.MusicButton.BorderBrush = Frozen(listening
            ? PluginPalette.WithAlpha(accent, 0.4)
            : theme.MusicButton.Border);
        visual.MusicButton.Foreground = Frozen(listening ? accent : theme.MusicButton.Foreground);
        visual.MusicIcon.Fill = visual.MusicButton.Foreground;

        if (!listening)
        {
            visual.ListeningLayer.BeginAnimation(UIElement.OpacityProperty, null);
            visual.ListeningLayer.Opacity = 0;
            visual.ListeningLayer.Visibility = Visibility.Collapsed;
            visual.MusicButton.Effect = DockShadow(
                visual.LightTheme ? 8 : 6,
                visual.LightTheme ? 0.3 : 0.35);
            return;
        }

        visual.ListeningLayer.Visibility = Visibility.Visible;
        if (!AnimationsEnabled())
        {
            visual.ListeningLayer.Opacity = 1;
            return;
        }
        visual.ListeningLayer.BeginAnimation(
            UIElement.OpacityProperty,
            Animate(0, 1, TimeSpan.FromMilliseconds(220)));
        var halo = new DropShadowEffect
        {
            Color = accent,
            ShadowDepth = 0,
            BlurRadius = 0,
            Opacity = 0.45,
        };
        visual.MusicButton.Effect = halo;
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

    internal static IReadOnlyList<ControlRippleHost> AttachControlRipples(DependencyObject root) =>
        Descendants(root).OfType<Control>().Select(ControlRippleHost.Attach).ToArray();

    internal static FrameworkElement PresentMusicResult(
        OverlayVisual visual,
        MusicRecognitionOutcome outcome,
        UiStrings strings,
        Action<IOverlayCommand> publish,
        Action<string, Button> copy)
    {
        visual.ResultHost.Children.Clear();
        var palette = PluginPalette.For(visual.LightTheme).MusicOverlay;
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
            Background = Frozen(palette.Surface),
            BorderBrush = Frozen(palette.Border),
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
        button.Content = Icon(confirmed ? CheckIconGeometry : CopyIconGeometry, 15, palette.MutedText);
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

        if (Search.SearchCoordinator.IsSafeShazamUrl(recognition.ShazamUrl))
        {
            var open = IconButton(LinkIconGeometry, strings.OpenInShazam, palette);
            open.Click += (_, _) => publish(new OpenMusicResult());
            DockPanel.SetDock(open, Dock.Right);
            row.Children.Add(open);
        }

        var copyButton = IconButton(CopyIconGeometry, strings.CopyTrackInfo, palette);
        copyButton.Click += (_, _) => copy($"{recognition.Title} — {recognition.Artist}", copyButton);
        DockPanel.SetDock(copyButton, Dock.Right);
        row.Children.Add(copyButton);

        var note = Icon(MusicIconGeometry, 16, palette.Primary);
        note.Margin = new Thickness(0, 0, 6, 0);
        DockPanel.SetDock(note, Dock.Left);
        row.Children.Add(note);

        var text = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            FontFamily = OverlayFont,
            FontSize = 13,
            Foreground = Frozen(palette.Text),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 390,
            Margin = new Thickness(0, 0, 4, 0),
        };
        text.Inlines.Add(new System.Windows.Documents.Run(recognition.Title)
        {
            FontWeight = FontWeights.SemiBold,
        });
        text.Inlines.Add(new System.Windows.Documents.Run($" — {recognition.Artist}")
        {
            Foreground = Frozen(palette.MutedText),
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
        var tile = new Border
        {
            Width = 44,
            Height = 44,
            CornerRadius = new CornerRadius(22),
            Background = Frozen(palette.SecondaryContainer),
            Child = Icon(
                status == MusicRecognitionStatus.NoMatch ? MusicOffIconGeometry : NoSoundIconGeometry,
                22,
                palette.OnSecondaryContainer),
        };
        row.Children.Add(tile);
        row.Children.Add(new TextBlock
        {
            Text = message,
            FontFamily = OverlayFont,
            FontSize = 13,
            LineHeight = 19,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Frozen(palette.Text),
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

    private static Button IconButton(
        Geometry geometry,
        string name,
        MusicOverlayPalette palette,
        double iconSize = 15)
    {
        var button = new Button
        {
            Content = Icon(geometry, iconSize, palette.MutedText),
            Width = 30,
            Height = 30,
            Margin = new Thickness(2, 0, 0, 0),
            Padding = new Thickness(7.5),
            Foreground = Frozen(palette.MutedText),
            Background = Frozen(PluginPalette.Transparent),
            BorderBrush = Frozen(PluginPalette.Transparent),
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            ToolTip = name,
        };
        ApplyButtonTemplate(button, 15, palette.SecondaryContainer, palette.OnSecondaryContainer);
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
            FontFamily = OverlayFont,
            FontSize = 12.5,
            FontWeight = FontWeights.Medium,
            Foreground = Frozen(palette.OnPrimaryContainer),
            Background = Frozen(palette.PrimaryContainer),
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
        };
        ApplyButtonTemplate(button, 17, palette.SecondaryContainer, palette.OnSecondaryContainer);
        AutomationProperties.SetName(button, label);
        return button;
    }

    // Even-odd of an oversized monitor rectangle and the lasso polygon: the polygon
    // interior gets its brightness back, everything else stays dimmed. The outer figure
    // bleeds past the window so the blur only rounds the selection boundary.
    public static Geometry BuildRevealGeometry(Size size, IReadOnlyList<Point> polygon)
    {
        var geometry = new StreamGeometry { FillRule = FillRule.EvenOdd };
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(-RevealBleed, -RevealBleed), true, true);
            context.PolyLineTo(
                [
                    new Point(size.Width + RevealBleed, -RevealBleed),
                    new Point(size.Width + RevealBleed, size.Height + RevealBleed),
                    new Point(-RevealBleed, size.Height + RevealBleed),
                ],
                true,
                true);
            if (polygon.Count >= 2)
            {
                var rest = new Point[polygon.Count - 1];
                for (var i = 1; i < polygon.Count; i++) rest[i - 1] = polygon[i];
                context.BeginFigure(polygon[0], true, true);
                context.PolyLineTo(rest, true, true);
            }
        }
        geometry.Freeze();
        return geometry;
    }

    // Closed fill of the lasso polygon; Nonzero keeps self-intersecting loops filled.
    public static Geometry BuildPolygonGeometry(IReadOnlyList<Point> polygon)
    {
        if (polygon.Count < 2) return Geometry.Empty;
        var geometry = new StreamGeometry { FillRule = FillRule.Nonzero };
        using (var context = geometry.Open())
        {
            var rest = new Point[polygon.Count - 1];
            for (var i = 1; i < polygon.Count; i++) rest[i - 1] = polygon[i];
            context.BeginFigure(polygon[0], true, true);
            context.PolyLineTo(rest, true, true);
        }
        geometry.Freeze();
        return geometry;
    }

    public static Geometry BuildSelectionFrameGeometry(Rect rect)
    {
        var geometry = new RectangleGeometry(rect, FrameCornerRadius, FrameCornerRadius);
        geometry.Freeze();
        return geometry;
    }

    // Circle-to-search snap: the lasso layers melt into the rectangle that is actually
    // sent, so the region leaves the screen exactly as it entered the provider.
    public static void BeginSelectionReveal(OverlayVisual visual, Geometry revealGeometry, Geometry frameGeometry)
    {
        visual.DimRect.Data = revealGeometry;
        visual.SelectionFrame.Data = frameGeometry;
        if (!AnimationsEnabled())
        {
            visual.Dim.Opacity = 0;
            visual.Sheen.Opacity = 0;
            visual.Halo.Opacity = 0;
            visual.Accent.Opacity = 0;
            visual.DimRect.Opacity = 1;
            visual.SelectionFrame.Opacity = 1;
            return;
        }

        visual.Dim.BeginAnimation(UIElement.OpacityProperty, Animate(1, 0, RevealDuration));
        visual.Sheen.BeginAnimation(UIElement.OpacityProperty, Animate(1, 0, RevealDuration));
        visual.Halo.BeginAnimation(UIElement.OpacityProperty, Animate(1, 0, RevealDuration));
        visual.Accent.BeginAnimation(UIElement.OpacityProperty, Animate(1, 0, RevealDuration));
        visual.DimRect.BeginAnimation(UIElement.OpacityProperty, Animate(0, 1, RevealDuration));
        visual.SelectionFrame.BeginAnimation(UIElement.OpacityProperty, Animate(0, 1, RevealDuration));
    }

    public static void BeginChipEntrance(OverlayVisual visual)
    {
        if (!AnimationsEnabled())
        {
            visual.ActionTray.Opacity = 1;
            visual.ActionTrayLift.Y = 0;
            return;
        }

        visual.ActionTray.BeginAnimation(UIElement.OpacityProperty, Animate(0, 1, EntranceDuration));
        visual.ActionTrayLift.BeginAnimation(TranslateTransform.YProperty, Animate(ChipEntranceLift, 0, EntranceDuration));
    }

    public static void BeginChipExit(OverlayVisual visual)
    {
        visual.ActionTray.IsHitTestVisible = false;
        if (!AnimationsEnabled())
        {
            visual.ActionTray.Opacity = 0;
            return;
        }

        visual.ActionTray.BeginAnimation(UIElement.OpacityProperty, Animate(1, 0, ExitDuration));
    }

    private static DoubleAnimation Animate(double from, double to, TimeSpan duration) =>
        new(from, to, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };

    internal static bool AnimationsEnabled() =>
        SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast;

    // A software-rendered blur rebuilds a full-screen bitmap on every mouse move.
    internal static bool HardwareEffectsEnabled() => RenderCapability.Tier >> 16 >= 2;

    private static Border CreateChip(SelectionChipPalette palette, UiStrings strings)
    {
        var icon = new Path
        {
            Data = ChipIconGeometry,
            Fill = Frozen(palette.Icon),
            Width = 13,
            Height = 13,
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
        };
        var label = new TextBlock
        {
            Text = strings.SelectionPrompt,
            FontFamily = OverlayFont,
            FontSize = 14,
            FontWeight = FontWeights.Medium,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Frozen(palette.Label),
        };
        var divider = new Rectangle
        {
            Width = 1,
            Height = 18,
            Fill = Frozen(palette.Divider),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 10, 0),
        };
        var keycap = new Border
        {
            Background = Frozen(palette.KeycapBackground),
            BorderBrush = Frozen(palette.KeycapBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(7, 6, 7, 6),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = strings.CancelKeyName,
                FontFamily = OverlayFont,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = Frozen(palette.KeycapText),
            },
        };
        var hint = new TextBlock
        {
            Text = strings.CancelAction,
            FontFamily = OverlayFont,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
            Foreground = Frozen(palette.Hint),
        };

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        row.Children.Add(icon);
        row.Children.Add(label);
        row.Children.Add(divider);
        row.Children.Add(keycap);
        row.Children.Add(hint);

        var chip = new Border
        {
            Child = row,
            MinHeight = 44,
            Padding = new Thickness(18, 8, 16, 8),
            VerticalAlignment = VerticalAlignment.Center,
            Background = Frozen(palette.Surface),
            BorderBrush = ChipOutlineBrush(palette),
            BorderThickness = new Thickness(ChipBorderThicknessDips),
            Effect = new DropShadowEffect
            {
                Color = PluginPalette.OpaqueBlack,
                BlurRadius = 20,
                ShadowDepth = palette.ShadowDepth,
                Direction = -90,
                Opacity = palette.ShadowOpacity,
            },
        };
        // Font metrics vary by locale, so the height is not known until layout; WPF also does
        // not clamp oversized radii into a pill (unlike CSS) — keep it at half the real height.
        chip.CornerRadius = new CornerRadius(22);
        chip.SizeChanged += (_, _) => chip.CornerRadius = new CornerRadius(chip.ActualHeight / 2);
        return chip;
    }

    private static (Button Button, Path Icon) CreateMusicButton(MusicButtonPalette palette, UiStrings strings)
    {
        var icon = new Path
        {
            Data = MusicIconGeometry,
            Fill = Frozen(palette.Foreground),
            Width = 18,
            Height = 18,
            Stretch = Stretch.Uniform,
            IsHitTestVisible = false,
        };
        var button = new Button
        {
            Content = icon,
            Width = 44,
            Height = 44,
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(10),
            VerticalAlignment = VerticalAlignment.Center,
            Background = Frozen(palette.Surface),
            Foreground = Frozen(palette.Foreground),
            BorderBrush = Frozen(palette.Border),
            BorderThickness = new Thickness(1),
            ToolTip = strings.MusicRecognitionAction,
            Focusable = true,
            Cursor = Cursors.Hand,
            Effect = DockShadow(palette.Surface.A == 0xF0 ? 8 : 6, palette.Surface.A == 0xF0 ? 0.3 : 0.35),
        };
        ApplyButtonTemplate(button, 22, palette.Hover, palette.Foreground);
        AutomationProperties.SetName(button, strings.MusicRecognitionAction);
        return (button, icon);
    }

    private static (Button Button, Path Chevron) CreateProviderButton(
        ContentControl content,
        SearchProviderDescriptor selected,
        ProviderPalette palette,
        UiStrings strings)
    {
        var chevron = new Path
        {
            Data = ChevronIconGeometry,
            Width = 14,
            Height = 14,
            Stretch = Stretch.Uniform,
            Fill = Frozen(palette.Hint),
            Margin = new Thickness(7, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new RotateTransform(),
            IsHitTestVisible = false,
        };
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        row.Children.Add(content);
        row.Children.Add(chevron);
        var button = new Button
        {
            Content = row,
            Height = 44,
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(12, 0, 13, 0),
            Background = Frozen(palette.Surface),
            BorderBrush = Frozen(palette.Border),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            ToolTip = strings.SelectSearchProvider(selected.DisplayName),
            Effect = DockShadow(palette.Surface.A == 0xF0 ? 8 : 6, palette.Surface.A == 0xF0 ? 0.3 : 0.35),
        };
        ApplyButtonTemplate(button, 22, palette.Hover, palette.Text);
        AutomationProperties.SetName(button, strings.SelectSearchProvider(selected.DisplayName));
        return (button, chevron);
    }

    private static Border CreateProviderMenu(
        IReadOnlyList<SearchProviderDescriptor> providers,
        string? selectedProviderId,
        ProviderPalette palette,
        bool lightTheme,
        UiStrings strings)
    {
        var panel = new StackPanel();
        AddProviderMenuItems(panel, providers, selectedProviderId, lightTheme, strings);
        return new Border
        {
            Child = panel,
            Visibility = Visibility.Collapsed,
            Padding = new Thickness(6),
            Width = 220,
            CornerRadius = new CornerRadius(16),
            Background = Frozen(palette.MenuSurface),
            BorderBrush = Frozen(palette.MenuBorder),
            BorderThickness = new Thickness(1),
            RenderTransformOrigin = new Point(0.5, 1),
            Effect = new DropShadowEffect
            {
                Color = PluginPalette.OpaqueBlack,
                BlurRadius = 20,
                ShadowDepth = 10,
                Direction = -90,
                Opacity = palette.MenuShadowOpacity,
            },
        };
    }

    private static void AddProviderMenuItems(
        Panel panel,
        IReadOnlyList<SearchProviderDescriptor> providers,
        string? selectedProviderId,
        bool lightTheme,
        UiStrings strings)
    {
        var palette = PluginPalette.For(lightTheme).Provider;
        foreach (var descriptor in providers.Where(provider =>
                     !string.Equals(provider.Id, selectedProviderId, StringComparison.OrdinalIgnoreCase)))
        {
            var item = new Button
            {
                Tag = descriptor.Id,
                Content = ProviderVisualCatalog.Create(descriptor, strings, lightTheme, includeFullName: true),
                Padding = new Thickness(12, 5, 12, 5),
                MinWidth = 206,
                MinHeight = 36,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = Frozen(PluginPalette.Transparent),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                ToolTip = descriptor.DisplayName,
                FontFamily = OverlayFont,
            };
            ApplyButtonTemplate(item, 12, palette.MenuHover, palette.MenuHoverText);
            AutomationProperties.SetName(item, descriptor.DisplayName);
            panel.Children.Add(item);
        }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static Path Icon(Geometry geometry, double size, Color color) => new()
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

    private static DropShadowEffect DockShadow(double depth, double opacity) => new()
    {
        Color = PluginPalette.OpaqueBlack,
        BlurRadius = depth == 8 ? 24 : 20,
        ShadowDepth = depth,
        Direction = -90,
        Opacity = opacity,
    };

    private static void ApplyButtonTemplate(
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
        var focused = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
        focused.Setters.Add(new Setter(Border.BorderBrushProperty, Frozen(SystemAccentColor.Read()), "Chrome"));
        focused.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(2), "Chrome"));
        template.Triggers.Add(focused);
        button.Template = template;
    }

    private static Binding TemplateBinding(DependencyProperty property) => new()
    {
        Path = new PropertyPath(property),
        RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent),
    };

    // High Contrast themes suppress the accent; a neutral outline stays readable there.
    private static Brush ChipOutlineBrush(SelectionChipPalette palette) =>
        SystemParameters.HighContrast
            ? Frozen(palette.NeutralOutline)
            : Frozen(SystemAccentColor.Read());

    // Material Symbols "ink_selection" (Apache-2.0); path data taken verbatim from
    // Images/ink_selection.svg (fill icon, viewBox 0 -960 960 960).
    private static readonly Geometry ChipIconGeometry = CreateChipIconGeometry();
    private static readonly Geometry MusicIconGeometry = CreateMusicIconGeometry();
    private static readonly Geometry ChevronIconGeometry = FrozenGeometry("M7 10l5 5 5-5Z");
    private static readonly Geometry CloseIconGeometry = FrozenGeometry(
        "M19 6.41 17.59 5 12 10.59 6.41 5 5 6.41 10.59 12 5 17.59 6.41 19 12 13.41 17.59 19 19 17.59 13.41 12Z");
    private static readonly Geometry LinkIconGeometry = FrozenGeometry(
        "M3.9 12c0-1.71 1.39-3.1 3.1-3.1h4V7H7c-2.76 0-5 2.24-5 5s2.24 5 5 5h4v-1.9H7c-1.71 0-3.1-1.39-3.1-3.1ZM8 13h8v-2H8v2Zm9-6h-4v1.9h4c1.71 0 3.1 1.39 3.1 3.1s-1.39 3.1-3.1 3.1h-4V17h4c2.76 0 5-2.24 5-5s-2.24-5-5-5Z");
    private static readonly Geometry CopyIconGeometry = FrozenGeometry(
        "M16 1H4c-1.1 0-2 .9-2 2v14h2V3h12V1Zm3 4H8c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h10c1.1 0 2-.9 2-2V7c0-1.1-.9-2-2-2Zm0 16H8V7h10v14Z");
    private static readonly Geometry CheckIconGeometry = FrozenGeometry(
        "M9 16.17 4.83 12l-1.42 1.41L9 19 21 7l-1.41-1.41Z");
    private static readonly Geometry NoSoundIconGeometry = FrozenGeometry(
        "M611-323l-43-43 114-113-114-113 43-43 113 114 113-114 43 43-114 113 114 113-43 43-113-114-113 114ZM120-360v-240h160l200-200v640L280-360H120Zm300-288L307-540H180v120h127l113 109v-337ZM311-481Z");
    private static readonly Geometry MusicOffIconGeometry = FrozenGeometry(
        "M806-56 57-805l43-43L849-99l-43 43ZM546-487l-60-60v-293h234v135H546v218ZM396-120q-63 0-106.5-43.5T246-270q0-63 43.5-106.5T396-420q28 0 50.5 8t39.5 22v-72l60 60v132q0 63-43.5 106.5T396-120Z");
    private static readonly FontFamily OverlayFont = new("Segoe UI Variable Text");

    private static Geometry FrozenGeometry(string data)
    {
        var geometry = Geometry.Parse(data);
        geometry.Freeze();
        return geometry;
    }

    private static Geometry CreateChipIconGeometry()
    {
        var geometry = Geometry.Parse(
            "M439-120v-401h401v60H542l298 298-43 43-298-298v298h-60Z" +
            "m-154 0v-60h60v60h-60Z" +
            "M180-780h-60q0-24.75 17.63-42.38Q155.25-840 180-840v60Z" +
            "m105 0v-60h60v60h-60Z" +
            "m165 0v-60h60v60h-60Z" +
            "m165 0v-60h60v60h-60Z" +
            "m165 0v-60h60v60h-60Z" +
            "m165 0v-60q24.75 0 42.38 17.62Q840-804.75 840-780h-60Z" +
            "M180-180v60q-24.75 0-42.37-17.63Q120-155.25 120-180h60Z" +
            "m-60-105v-60h60v60h-60Z" +
            "m0-165v-60h60v60h-60Z" +
            "m0-165v-60h60v60h-60Z" +
            "m660 0v-60h60v60h-60Z");
        geometry.Freeze();
        return geometry;
    }

    private static Geometry CreateMusicIconGeometry()
    {
        var geometry = Geometry.Parse("M12 3v10.55A4 4 0 1 0 14 17V7h4V3h-6Z");
        geometry.Freeze();
        return geometry;
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}

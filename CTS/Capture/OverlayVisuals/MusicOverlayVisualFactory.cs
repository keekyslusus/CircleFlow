namespace CircleToSearch.Capture;

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using CircleToSearch.MusicRecognition;
using CircleToSearch.Ui;

internal static class MusicOverlayVisualFactory
{
    internal static readonly Geometry MusicIconGeometry = CreateMusicIconGeometry();

    internal static MusicOverlayVisual Create(
        Size size,
        bool lightTheme,
        UiStrings strings)
    {
        var palette = PluginPalette.For(lightTheme);
        var icon = new Path
        {
            Data = MusicIconGeometry,
            Fill = OverlayVisualResources.Frozen(palette.MusicButton.Foreground),
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
            Background = OverlayVisualResources.Frozen(palette.MusicButton.Surface),
            Foreground = OverlayVisualResources.Frozen(palette.MusicButton.Foreground),
            BorderBrush = OverlayVisualResources.Frozen(palette.MusicButton.Border),
            BorderThickness = new Thickness(1),
            ToolTip = strings.MusicRecognitionAction,
            Focusable = true,
            Cursor = Cursors.Hand,
            Effect = OverlayVisualResources.DockShadow(
                palette.MusicButton.Surface.A == 0xF0 ? 8 : 6,
                palette.MusicButton.Surface.A == 0xF0 ? 0.3 : 0.35),
        };
        OverlayVisualResources.ApplyButtonTemplate(
            button, 22, palette.MusicButton.Hover, palette.MusicButton.Foreground);
        AutomationProperties.SetName(button, strings.MusicRecognitionAction);

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
            Foreground = OverlayVisualResources.Frozen(PluginPalette.ListeningText),
            HorizontalAlignment = HorizontalAlignment.Center,
            FontSize = 14,
            FontWeight = FontWeights.Medium,
            FontFamily = OverlayVisualResources.Font,
            Margin = new Thickness(0, 10, 0, 0),
            Effect = new DropShadowEffect
            {
                Color = PluginPalette.OpaqueBlack,
                BlurRadius = 10,
                ShadowDepth = 1,
                Opacity = 0.4,
            },
        });

        var resultHost = new Grid
        {
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        Panel.SetZIndex(resultHost, 1);
        var (debugPanel, debugScenarioButtons, debugToastButtons) =
            CreateDebugPanel(palette.MusicOverlay, strings);
        Panel.SetZIndex(debugPanel, 3);
        return new MusicOverlayVisual(
            button,
            icon,
            listeningLayer,
            waveform,
            resultHost,
            debugPanel,
            debugScenarioButtons,
            debugToastButtons);
    }

    private static (Border Panel, Panel ScenarioButtons, Panel ToastButtons) CreateDebugPanel(
        MusicOverlayPalette palette,
        UiStrings strings)
    {
        (MusicDebugScenario Scenario, string Label)[] scenarios =
        [
            (MusicDebugScenario.Live, strings.DebugMusicLive),
            (MusicDebugScenario.RippleSoft, strings.DebugMusicRippleSoft),
            (MusicDebugScenario.RippleMedium, strings.DebugMusicRippleMedium),
            (MusicDebugScenario.RippleStrong, strings.DebugMusicRippleStrong),
            (MusicDebugScenario.Matched, strings.DebugMusicMatched),
            (MusicDebugScenario.NoMatch, strings.DebugMusicNoMatch),
            (MusicDebugScenario.NoAudio, strings.DebugMusicNoAudio),
            (MusicDebugScenario.DeviceError, strings.DebugMusicDeviceError),
            (MusicDebugScenario.ServiceError, strings.DebugMusicServiceError),
            (MusicDebugScenario.RateLimited, strings.DebugMusicRateLimited),
        ];
        var scenarioButtons = new UniformGrid { Columns = 2 };
        foreach (var (scenario, label) in scenarios)
            scenarioButtons.Children.Add(CreateDebugButton(palette, label, scenario));

        (ToastTone Tone, string Label)[] toasts =
        [
            (ToastTone.Neutral, strings.DebugToastNeutral),
            (ToastTone.Error, strings.DebugToastError),
            (ToastTone.Success, strings.DebugToastSuccess),
        ];
        var toastButtons = new UniformGrid { Columns = 3 };
        foreach (var (tone, label) in toasts)
            toastButtons.Children.Add(CreateDebugButton(palette, label, tone));

        var content = new StackPanel();
        content.Children.Add(new TextBlock
        {
            Text = strings.DebugOverlayTitle,
            Foreground = OverlayVisualResources.Frozen(palette.Text),
            FontFamily = OverlayVisualResources.Font,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 10),
        });
        content.Children.Add(CreateDebugSectionTitle(palette, strings.DebugMusicSection));
        content.Children.Add(scenarioButtons);
        var toastTitle = CreateDebugSectionTitle(palette, strings.DebugToastSection);
        toastTitle.Margin = new Thickness(0, 10, 0, 4);
        content.Children.Add(toastTitle);
        content.Children.Add(toastButtons);

        var panel = new Border
        {
            Child = content,
            Visibility = Visibility.Collapsed,
            Width = 340,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(24),
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(10),
            Background = OverlayVisualResources.Frozen(palette.Surface),
            BorderBrush = OverlayVisualResources.Frozen(palette.Border),
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
        MusicOverlayVisualPresenter.SetDebugScenario(
            scenarioButtons,
            palette,
            MusicDebugScenario.Live);
        return (panel, scenarioButtons, toastButtons);
    }

    private static Button CreateDebugButton(
        MusicOverlayPalette palette,
        string label,
        object tag)
    {
        var button = new Button
        {
            Content = label,
            Tag = tag,
            Foreground = OverlayVisualResources.Frozen(palette.Text),
            Background = OverlayVisualResources.Frozen(PluginPalette.Transparent),
            BorderBrush = OverlayVisualResources.Frozen(PluginPalette.Transparent),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(0, 1, 0, 1),
            HorizontalContentAlignment = HorizontalAlignment.Left,
            FontFamily = OverlayVisualResources.Font,
            FontSize = 12,
            Cursor = Cursors.Hand,
        };
        OverlayVisualResources.ApplyButtonTemplate(
            button, 6, palette.SecondaryContainer, palette.OnSecondaryContainer);
        AutomationProperties.SetName(button, label);
        return button;
    }

    private static TextBlock CreateDebugSectionTitle(
        MusicOverlayPalette palette,
        string text) => new()
        {
            Text = text,
            Foreground = OverlayVisualResources.Frozen(palette.MutedText),
            FontFamily = OverlayVisualResources.Font,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4),
        };

    private static Geometry CreateMusicIconGeometry()
    {
        var geometry = Geometry.Parse("M12 3v10.55A4 4 0 1 0 14 17V7h4V3h-6Z");
        geometry.Freeze();
        return geometry;
    }
}

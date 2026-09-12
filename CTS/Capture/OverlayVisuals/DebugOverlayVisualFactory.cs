namespace CircleToSearch.Capture;

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Effects;
using CircleToSearch.MusicRecognition;
using CircleToSearch.Ui;

internal static class DebugOverlayVisualFactory
{
    internal static DebugOverlayVisual Create(bool lightTheme, UiStrings strings)
    {
        var palette = PluginPalette.For(lightTheme).MusicOverlay;
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
        var translationTitle = CreateDebugSectionTitle(palette, strings.DebugTranslationSection);
        translationTitle.Margin = new Thickness(0, 10, 0, 4);
        content.Children.Add(translationTitle);
        var resetTranslationConsent = CreateDebugButton(palette, strings.DebugResetTranslationConsent, null);
        content.Children.Add(resetTranslationConsent);

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
        Panel.SetZIndex(panel, 3);
        var visual = new DebugOverlayVisual(panel, scenarioButtons, toastButtons, resetTranslationConsent);
        DebugOverlayVisualPresenter.SetMusicScenario(visual, MusicDebugScenario.Live, lightTheme);
        return visual;
    }

    private static Button CreateDebugButton(
        MusicOverlayPalette palette,
        string label,
        object? tag)
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
}

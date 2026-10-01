using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using CircleToSearch.Capture;
using CircleToSearch.MusicRecognition;
using CircleToSearch.Ui;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class DebugOverlayVisualTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Factory_preserves_scenarios_initial_selection_and_layout(bool lightTheme)
    {
        var failure = RunOnSta(() =>
        {
            var visual = DebugOverlayVisualFactory.Create(lightTheme, TestUiStrings.English);
            var palette = PluginPalette.For(lightTheme).MusicOverlay;

            Assert.Equal(Visibility.Collapsed, visual.Panel.Visibility);
            Assert.Equal(
                Enum.GetValues<MusicDebugScenario>(),
                visual.MusicScenarioButtons.Children.OfType<Button>()
                    .Select(button => Assert.IsType<MusicDebugScenario>(button.Tag))
                    .ToArray());
            Assert.Equal(
                Enum.GetValues<ToastTone>(),
                visual.ToastButtons.Children.OfType<Button>()
                    .Select(button => Assert.IsType<ToastTone>(button.Tag))
                    .ToArray());
            Assert.Equal(TestUiStrings.English.DebugResetTranslationConsent,
                visual.ResetTranslationConsentButton.Content);
            Assert.Equal(TestUiStrings.English.DebugResetTranslationConsent,
                System.Windows.Automation.AutomationProperties.GetName(visual.ResetTranslationConsentButton));
            visual.Panel.Visibility = Visibility.Visible;
            var root = new Grid();
            root.Children.Add(visual.Panel);
            root.Measure(new Size(640, 400));
            root.Arrange(new Rect(0, 0, 640, 400));
            var resetBounds = visual.ResetTranslationConsentButton.TransformToAncestor(root)
                .TransformBounds(new Rect(visual.ResetTranslationConsentButton.RenderSize));
            Assert.True(resetBounds.Top >= 0 && resetBounds.Bottom <= 400);

            var live = visual.MusicScenarioButtons.Children.OfType<Button>()
                .Single(button => Equals(button.Tag, MusicDebugScenario.Live));
            var noAudio = visual.MusicScenarioButtons.Children.OfType<Button>()
                .Single(button => Equals(button.Tag, MusicDebugScenario.NoAudio));
            Assert.Equal(palette.PrimaryContainer, Assert.IsType<SolidColorBrush>(live.Background).Color);
            Assert.Equal(palette.OnPrimaryContainer, Assert.IsType<SolidColorBrush>(live.Foreground).Color);
            Assert.Equal(PluginPalette.Transparent, Assert.IsType<SolidColorBrush>(noAudio.Background).Color);
            Assert.Equal(palette.Text, Assert.IsType<SolidColorBrush>(noAudio.Foreground).Color);

            Assert.Equal(HorizontalAlignment.Right, visual.Panel.HorizontalAlignment);
            Assert.Equal(VerticalAlignment.Top, visual.Panel.VerticalAlignment);
            Assert.Equal(3, Panel.GetZIndex(visual.Panel));
            Assert.Equal(palette.Surface, Assert.IsType<SolidColorBrush>(visual.Panel.Background).Color);
            Assert.Equal(palette.Border, Assert.IsType<SolidColorBrush>(visual.Panel.BorderBrush).Color);
            var shadow = Assert.IsType<DropShadowEffect>(visual.Panel.Effect);
            Assert.Equal(palette.ShadowOpacity, shadow.Opacity);
        });

        Assert.Null(failure);
    }

    private static Exception? RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(30));
        Assert.False(thread.IsAlive, "the STA thread did not finish in time");
        return failure;
    }
}

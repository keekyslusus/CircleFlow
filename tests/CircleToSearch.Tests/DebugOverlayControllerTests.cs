using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CircleToSearch.Capture;
using CircleToSearch.Capture.OverlayInteractions;
using CircleToSearch.MusicRecognition;
using CircleToSearch.Ui;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class DebugOverlayControllerTests
{
    [Fact]
    public void SetOpen_tracks_visibility_when_debug_is_enabled()
    {
        var failure = RunOnSta(() =>
        {
            var visual = CreateVisual();
            using var controller = CreateController(visual);

            controller.SetOpen(true);
            Assert.True(controller.IsOpen);
            Assert.Equal(Visibility.Visible, visual.Panel.Visibility);

            controller.SetOpen(false);
            Assert.False(controller.IsOpen);
            Assert.Equal(Visibility.Collapsed, visual.Panel.Visibility);
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Disabled_debug_and_closing_mode_block_open_but_closing_still_allows_close()
    {
        var failure = RunOnSta(() =>
        {
            var disabledVisual = CreateVisual();
            using var disabled = CreateController(disabledVisual, debugEnabled: false);
            disabled.SetOpen(true);
            Assert.False(disabled.IsOpen);
            Assert.Equal(Visibility.Collapsed, disabledVisual.Panel.Visibility);
            Assert.False(disabledVisual.ResetTranslationConsentButton.IsEnabled);

            var mode = OverlayInteractionMode.Selecting;
            var closingVisual = CreateVisual();
            using var closing = CreateController(closingVisual, getMode: () => mode);
            closing.SetOpen(true);
            Assert.True(closing.IsOpen);
            mode = OverlayInteractionMode.Closing;
            closing.SetOpen(false);
            Assert.False(closing.IsOpen);
            Assert.Equal(Visibility.Collapsed, closingVisual.Panel.Visibility);
            closing.SetOpen(true);
            Assert.False(closing.IsOpen);
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Every_music_scenario_updates_selection_and_publishes_once()
    {
        var failure = RunOnSta(() =>
        {
            var visual = CreateVisual();
            var selected = new List<MusicDebugScenario>();
            using var controller = CreateController(visual, selected.Add);
            var palette = PluginPalette.For(lightTheme: false).MusicOverlay;

            foreach (var button in visual.MusicScenarioButtons.Children.OfType<Button>())
            {
                controller.SetOpen(true);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var scenario = Assert.IsType<MusicDebugScenario>(button.Tag);
                Assert.Equal(scenario, selected[^1]);
                Assert.False(controller.IsOpen);
                Assert.Equal(palette.PrimaryContainer, Assert.IsType<SolidColorBrush>(button.Background).Color);
                Assert.Equal(palette.OnPrimaryContainer, Assert.IsType<SolidColorBrush>(button.Foreground).Color);
                Assert.All(
                    visual.MusicScenarioButtons.Children.OfType<Button>().Where(other => !ReferenceEquals(other, button)),
                    other => Assert.Equal(
                        PluginPalette.Transparent,
                        Assert.IsType<SolidColorBrush>(other.Background).Color));
            }

            Assert.Equal(Enum.GetValues<MusicDebugScenario>(), selected);
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Every_toast_button_publishes_its_localized_notification_once()
    {
        var failure = RunOnSta(() =>
        {
            var visual = CreateVisual();
            var notifications = new List<ToastNotification>();
            using var controller = CreateController(visual, showToast: notifications.Add);

            foreach (var button in visual.ToastButtons.Children.OfType<Button>())
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.Equal(Enum.GetValues<ToastTone>(), notifications.Select(notification => notification.Tone));
            Assert.Equal(
                [
                    TestUiStrings.English.DebugToastNeutral,
                    TestUiStrings.English.DebugToastError,
                    TestUiStrings.English.DebugToastSuccess,
                ],
                notifications.Select(notification => notification.Message));
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Reset_translation_consent_saves_and_closes_debug_only_on_success()
    {
        var failure = RunOnSta(() =>
        {
            var visual = CreateVisual();
            var settings = new CircleToSearch.Settings.PluginSettings
            {
                ImageTranslationPrivacyConsentAccepted = true,
            };
            var saves = 0;
            var notifications = new List<ToastNotification>();
            var failSave = true;
            using var controller = CreateController(visual, showToast: notifications.Add,
                resetTranslationConsent: () =>
                    CircleToSearch.CompositionRoot.SaveTranslationConsent(settings, false, () =>
                    {
                        saves++;
                        if (failSave) throw new InvalidOperationException("disk");
                    }));
            controller.SetOpen(true);
            visual.ResetTranslationConsentButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(settings.ImageTranslationPrivacyConsentAccepted);
            Assert.True(controller.IsOpen);
            Assert.Equal(TestUiStrings.English.SavingFailed("disk"), Assert.Single(notifications).Message);
            Assert.Equal(ToastTone.Error, notifications[0].Tone);

            failSave = false;
            visual.ResetTranslationConsentButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(settings.ImageTranslationPrivacyConsentAccepted);
            Assert.Equal(2, saves);
            Assert.False(controller.IsOpen);
            Assert.Equal(TestUiStrings.English.DebugTranslationConsentReset, notifications[1].Message);
            Assert.Equal(ToastTone.Success, notifications[1].Tone);
            visual.ResetTranslationConsentButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(2, saves);
        });
        Assert.Null(failure);
    }

    [Fact]
    public void Closing_and_dispose_prevent_callbacks_and_dispose_is_idempotent()
    {
        var failure = RunOnSta(() =>
        {
            var visual = CreateVisual();
            var scenarios = new List<MusicDebugScenario>();
            var notifications = new List<ToastNotification>();
            var resets = 0;
            var mode = OverlayInteractionMode.Closing;
            var controller = CreateController(
                visual,
                scenarios.Add,
                notifications.Add,
                resetTranslationConsent: () => resets++,
                getMode: () => mode);
            var scenarioButton = Assert.Single(
                visual.MusicScenarioButtons.Children.OfType<Button>(),
                button => Equals(button.Tag, MusicDebugScenario.Live));
            var toastButton = Assert.Single(
                visual.ToastButtons.Children.OfType<Button>(),
                button => Equals(button.Tag, ToastTone.Neutral));

            scenarioButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            toastButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            visual.ResetTranslationConsentButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Empty(scenarios);
            Assert.Empty(notifications);
            Assert.Equal(0, resets);

            mode = OverlayInteractionMode.Selecting;
            controller.Dispose();
            controller.Dispose();
            scenarioButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            toastButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            visual.ResetTranslationConsentButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Empty(scenarios);
            Assert.Empty(notifications);
            Assert.Equal(0, resets);
        });

        Assert.Null(failure);
    }

    private static DebugOverlayVisual CreateVisual() =>
        DebugOverlayVisualFactory.Create(lightTheme: false, TestUiStrings.English);

    private static DebugOverlayController CreateController(
        DebugOverlayVisual visual,
        Action<MusicDebugScenario>? musicScenarioSelected = null,
        Action<ToastNotification>? showToast = null,
        Action? resetTranslationConsent = null,
        bool debugEnabled = true,
        Func<OverlayInteractionMode>? getMode = null) =>
        new(
            visual,
            lightTheme: false,
            debugEnabled,
            getMode ?? (() => OverlayInteractionMode.Selecting),
            musicScenarioSelected ?? (_ => { }),
            showToast ?? (_ => { }),
            resetTranslationConsent,
            TestUiStrings.English);

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

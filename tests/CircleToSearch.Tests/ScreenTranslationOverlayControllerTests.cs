using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CircleToSearch.Capture;
using CircleToSearch.Capture.OverlayInteractions;
using CircleToSearch.TextRecognition;
using CircleToSearch.Translation;
using CircleToSearch.Ui;
using CircleToSearch.Ui.Effects;
using Xunit;
using GdiRectangle = System.Drawing.Rectangle;
using GdiSize = System.Drawing.Size;

namespace CircleToSearch.Tests;

public sealed class ScreenTranslationOverlayControllerTests
{
    [Fact]
    public void Consent_waiting_cancel_and_show_original_follow_state_machine()
    {
        var failure = RunOnSta(() =>
        {
            var visual = OverlayVisualFactory.CreateRoot(null, new System.Windows.Size(640, 400), 20, false, TestUiStrings.English);
            var window = new Window { Width = 640, Height = 400, Content = visual.Root };
            window.Show();
            window.UpdateLayout();
            var state = new OverlayInteractionState();
            var commands = new List<IOverlayCommand>();
            var consent = false;
            using var controller = new ScreenTranslationOverlayController(
                visual.TranslationAction,
                visual.TranslationOverlay,
                visual.Effects,
                visual.Root,
                new OverlayCoordinateMapper(1, false, new GdiSize(640, 400)),
                TestUiStrings.English,
                () => consent,
                () => consent = true,
                () => "es",
                commands.Add,
                target => state.TransitionTo(target),
                _ => { },
                () => false,
                false);

            visual.TranslationAction.Button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(OverlayInteractionMode.TranslationConsent, state.Mode);
            Assert.True(controller.IsConsentOpen);
            Assert.Empty(commands);

            visual.TranslationOverlay.ContinueButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(consent);
            Assert.Equal(OverlayInteractionMode.Translating, state.Mode);
            Assert.True(controller.IsTranslating);
            Assert.Equal(TestUiStrings.English.Translating, visual.TranslationAction.Button.ToolTip);
            Assert.Equal(Visibility.Visible, visual.TranslationAction.LoadingIndicator.Visibility);
            Assert.Equal(1, visual.TranslationAction.LoadingIndicator.Opacity);
            Assert.Equal(0, visual.TranslationAction.Icon.Opacity);
            Assert.Empty(commands);

            controller.SetOcrOutcome(OcrRecognitionOutcome.Success(Document()));
            var request = Assert.IsType<ScreenTranslationRequested>(Assert.Single(commands));
            controller.ShowResult(new ScreenTranslationResult(request.RequestId,
                [new ScreenTranslationLine(0, new GdiRectangle(20, 20, 120, 20), "Hello", "Hola")], false));
            Assert.Equal(OverlayInteractionMode.TranslationShown, state.Mode);
            Assert.True(controller.IsTranslationShown);
            Assert.Equal(TestUiStrings.English.ShowOriginal, visual.TranslationAction.Button.ToolTip);
            Assert.Equal(
                TestUiStrings.English.ShowOriginal,
                AutomationProperties.GetName(visual.TranslationAction.Button));
            Assert.Same(
                TextTranslationVisualFactory.ShowOriginalIconGeometry,
                visual.TranslationAction.Icon.Data);
            Assert.Equal(Visibility.Collapsed, visual.TranslationAction.LoadingIndicator.Visibility);
            Assert.Equal(1, visual.TranslationAction.Icon.Opacity);
            Assert.Equal(0, visual.Effects.SceneRipples.ActiveCount);

            visual.TranslationAction.Button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(OverlayInteractionMode.Selecting, state.Mode);
            Assert.False(controller.IsTranslationShown);
            Assert.Equal(TestUiStrings.English.Translate, visual.TranslationAction.Button.ToolTip);
            Assert.Same(
                TextTranslationVisualFactory.TranslateIconGeometry,
                visual.TranslationAction.Icon.Data);

            visual.Music.Waveform.Dispose();
            visual.Effects.SceneRipples.Dispose();
            window.Close();
        });
        Assert.Null(failure);
    }

    [Fact]
    public void Successful_translation_emits_accent_scene_ripple()
    {
        var failure = RunOnSta(() =>
        {
            var visual = OverlayVisualFactory.CreateRoot(
                null,
                new System.Windows.Size(640, 400),
                20,
                false,
                TestUiStrings.English);
            var rippleLayerIndex = visual.Root.Children.IndexOf(visual.Effects.SceneRippleLayer);
            visual.Effects.SceneRipples.Dispose();
            visual.Root.Children.Remove(visual.Effects.SceneRippleLayer);
            var rippleLayer = new Canvas { IsHitTestVisible = false };
            visual.Root.Children.Insert(rippleLayerIndex, rippleLayer);
            using var ripples = new SceneRippleHost(rippleLayer, animationsEnabled: true);
            var effects = new OverlayEffectsVisual(rippleLayer, ripples);
            var window = new Window { Width = 640, Height = 400, Content = visual.Root };
            window.Show();
            window.UpdateLayout();
            var state = new OverlayInteractionState();
            var commands = new List<IOverlayCommand>();
            using var controller = new ScreenTranslationOverlayController(
                visual.TranslationAction,
                visual.TranslationOverlay,
                effects,
                visual.Root,
                new OverlayCoordinateMapper(1, false, new GdiSize(640, 400)),
                TestUiStrings.English,
                () => true,
                () => { },
                () => "es",
                commands.Add,
                target => state.TransitionTo(target),
                _ => { },
                () => true,
                false);
            controller.SetOcrOutcome(OcrRecognitionOutcome.Success(Document()));

            visual.TranslationAction.Button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var request = Assert.IsType<ScreenTranslationRequested>(Assert.Single(commands));
            controller.ShowResult(new ScreenTranslationResult(request.RequestId,
                [new ScreenTranslationLine(0, new GdiRectangle(20, 20, 120, 20), "Hello", "Hola")], false));
            Assert.True(controller.HasPendingCompletionRipple);

            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);

            Assert.False(controller.HasPendingCompletionRipple);
            Assert.Equal(1, ripples.ActiveCount);
            var effect = Assert.Single(rippleLayer.Children.OfType<Canvas>());
            var wash = Assert.Single(effect.Children.OfType<System.Windows.Shapes.Rectangle>());
            Assert.Equal(
                PluginPalette.WithAlpha(SystemAccentColor.Read(), 0.05),
                Assert.IsType<SolidColorBrush>(wash.Fill).Color);

            visual.Music.Waveform.Dispose();
            window.Close();
        });
        Assert.Null(failure);
    }

    [Fact]
    public void Repeated_translate_click_cancels_only_active_translation()
    {
        var failure = RunOnSta(() =>
        {
            var visual = OverlayVisualFactory.CreateRoot(null, new System.Windows.Size(320, 200), 10, false, TestUiStrings.English);
            var state = new OverlayInteractionState();
            var commands = new List<IOverlayCommand>();
            using var controller = new ScreenTranslationOverlayController(
                visual.TranslationAction,
                visual.TranslationOverlay,
                visual.Effects,
                visual.Root,
                new OverlayCoordinateMapper(1, false, new GdiSize(320, 200)),
                TestUiStrings.English,
                () => true,
                () => { },
                () => "es",
                commands.Add,
                target => state.TransitionTo(target),
                _ => { },
                () => false,
                false);
            controller.SetOcrOutcome(OcrRecognitionOutcome.Success(Document()));

            visual.TranslationAction.Button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var request = Assert.IsType<ScreenTranslationRequested>(Assert.Single(commands));
            visual.TranslationAction.Button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            var cancel = Assert.IsType<CancelScreenTranslation>(commands[1]);
            Assert.Equal(request.RequestId, cancel.RequestId);
            Assert.Equal(OverlayInteractionMode.Selecting, state.Mode);
            Assert.False(controller.IsTranslating);
            visual.Music.Waveform.Dispose();
            visual.Effects.SceneRipples.Dispose();
        });
        Assert.Null(failure);
    }

    private static OcrDocument Document()
    {
        var word = new OcrWord(0, 0, 0, "en", "Hello", new GdiRectangle(20, 20, 120, 20));
        return new OcrDocument("en", new GdiSize(320, 200), [new OcrLine(0, 0, "en", word.BoundsPx, [word])]);
    }

    private static Exception? RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return failure;
    }
}

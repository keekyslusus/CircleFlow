using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CircleToSearch.Capture;
using CircleToSearch.Capture.OverlayInteractions;
using CircleToSearch.Translation;
using CircleToSearch.Ui;
using CircleToSearch.Ui.Effects;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class ScreenTranslationOverlayControllerTests
{
    [Fact]
    public void Consent_fits_a_320_by_200_overlay_and_scrolls_the_full_message()
    {
        var failure = RunOnSta(() =>
        {
            var visual = OverlayVisualFactory.CreateRoot(Source(320, 200), new Size(320, 200), 10, false, TestUiStrings.English);
            var window = new Window { Width = 320, Height = 200, WindowStyle = WindowStyle.None, Content = visual.Root };
            window.Show();
            window.UpdateLayout();
            var state = new OverlayInteractionState();
            using var controller = new ScreenTranslationOverlayController(
                visual.TranslationAction, visual.TranslationOverlay, visual.Bottom, visual.Effects, visual.Root,
                TestUiStrings.English, () => false, () => { }, () => "es", _ => { },
                mode => state.TransitionTo(mode), _ => { }, () => false, false, visual.Selection.Screenshot);
            Click(visual.TranslationAction.Button);
            window.UpdateLayout();

            var card = Card(visual);
            var bounds = card.TransformToAncestor(window).TransformBounds(new Rect(card.RenderSize));
            Assert.True(bounds.Left >= 0, $"Card left edge: {bounds.Left}");
            Assert.True(bounds.Top >= 0, $"Card top edge: {bounds.Top}; card height {card.ActualHeight}; max {card.MaxHeight}; root {visual.Root.ActualHeight}; action {visual.Bottom.ActionSlot.ActualHeight}; stack margin {visual.Bottom.Stack.Margin.Bottom}; result margin {visual.Bottom.ResultSlot.Margin.Bottom}");
            Assert.True(bounds.Right <= window.ActualWidth, $"Card right edge: {bounds.Right}");
            Assert.True(bounds.Bottom <= window.ActualHeight, $"Card bottom edge: {bounds.Bottom}");
            var scroll = Assert.IsType<ScrollViewer>(Row(card).Children[1]);
            Assert.True(scroll.ScrollableHeight > 0);
            var title = Assert.IsType<TextBlock>(Assert.IsType<StackPanel>(scroll.Content).Children[0]);
            Assert.Equal(TestUiStrings.English.TranslationConsentTitle, title.Text);
            Assert.Equal(TestUiStrings.English.TranslationConsentMessage,
                Assert.IsType<TextBlock>(Assert.IsType<StackPanel>(scroll.Content).Children[1]).Text);
            var iconContainer = Assert.IsType<Border>(Row(card).Children[0]);
            var iconTop = iconContainer.TransformToAncestor(card).Transform(new Point()).Y;
            var titleTop = title.TransformToAncestor(card).Transform(new Point()).Y;
            Assert.InRange(Math.Abs(iconTop - titleTop), 0, 4);
            var action = Primary(card);
            var actionBounds = action.TransformToAncestor(window).TransformBounds(new Rect(action.RenderSize));
            Assert.True(actionBounds.Top >= 0 && actionBounds.Bottom <= window.ActualHeight);
            scroll.ScrollToBottom();
            scroll.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            Assert.True(scroll.VerticalOffset > 0);
            visual.Music.Waveform.Dispose();
            visual.Effects.SceneRipples.Dispose();
            window.Close();
        });
        Assert.Null(failure);
    }

    [Fact]
    public void Consent_cancel_escape_and_failed_save_leave_consent_pending()
    {
        var failure = RunOnSta(() =>
        {
            var visual = OverlayVisualFactory.CreateRoot(Source(640, 400), new Size(640, 400), 20, false, TestUiStrings.English);
            var state = new OverlayInteractionState();
            var commands = new List<IOverlayCommand>();
            var toasts = new List<ToastNotification>();
            var accepted = false;
            var saves = 0;
            using var controller = new ScreenTranslationOverlayController(
                visual.TranslationAction, visual.TranslationOverlay, visual.Bottom, visual.Effects, visual.Root,
                TestUiStrings.English, () => accepted, () => { saves++; throw new InvalidOperationException("disk"); },
                () => "es", commands.Add, mode => state.TransitionTo(mode), toasts.Add, () => false,
                false, visual.Selection.Screenshot);

            Click(visual.TranslationAction.Button);
            var card = Card(visual);
            Assert.False(visual.Root.Children.Contains(card));
            Assert.Equal(0, visual.Bottom.Stack.Children.IndexOf(visual.TranslationOverlay.StateHost));
            Assert.Equal(TestUiStrings.English.TranslationConsentTitle, Title(card).Text);
            Assert.Equal(TestUiStrings.English.TranslationConsentMessage, Message(card).Text);
            Assert.Same(TextTranslationVisualFactory.TranslateIconGeometry, Icon(card).Data);
            Assert.Equal(TestUiStrings.English.ConsentCancel, Close(card).ToolTip);
            Assert.Equal(TestUiStrings.English.ConsentCancel, AutomationProperties.GetName(Close(card)));
            Assert.Equal(TestUiStrings.English.Continue, Primary(card).Content);
            Click(Close(card));
            Assert.Equal(OverlayInteractionMode.Selecting, state.Mode);
            Assert.Empty(visual.TranslationOverlay.StateHost.Children);

            Click(visual.TranslationAction.Button);
            Assert.True(controller.HandleEscape());
            Assert.Equal(OverlayInteractionMode.Selecting, state.Mode);
            Click(visual.TranslationAction.Button);
            Click(Primary(Card(visual)));
            Assert.Equal(1, saves);
            Assert.False(accepted);
            Assert.Empty(commands);
            Assert.Equal(OverlayInteractionMode.Selecting, state.Mode);
            Assert.Empty(visual.TranslationOverlay.StateHost.Children);
            Assert.Equal(TestUiStrings.English.SavingFailed("disk"), Assert.Single(toasts).Message);
            Click(visual.TranslationAction.Button);
            Assert.True(controller.IsConsentOpen);
            Assert.Empty(commands);
            visual.Music.Waveform.Dispose();
            visual.Effects.SceneRipples.Dispose();
        });
        Assert.Null(failure);
    }

    [Theory]
    [InlineData(TranslationFailure.Network, "Translation failed because the network is unavailable.", true)]
    [InlineData(TranslationFailure.Timeout, "Translation timed out.", true)]
    [InlineData(TranslationFailure.Service, "The screen could not be translated.", true)]
    [InlineData(TranslationFailure.BadResponse, "The screen could not be translated.", true)]
    [InlineData(TranslationFailure.RateLimited, "The translation service rate limit was reached.", false)]
    [InlineData(TranslationFailure.None, "The screen could not be translated.", true)]
    public void Terminal_failures_show_a_state_card_without_a_toast(
        TranslationFailure kind, string message, bool canRetry)
    {
        var failure = RunOnSta(() =>
        {
            var visual = OverlayVisualFactory.CreateRoot(Source(640, 400), new Size(640, 400), 20, false, TestUiStrings.English);
            var state = new OverlayInteractionState();
            var commands = new List<IOverlayCommand>();
            var toasts = new List<ToastNotification>();
            using var controller = new ScreenTranslationOverlayController(
                visual.TranslationAction, visual.TranslationOverlay, visual.Bottom, visual.Effects, visual.Root,
                TestUiStrings.English, () => true, () => { }, () => "es", commands.Add,
                mode => state.TransitionTo(mode), toasts.Add, () => false, false, visual.Selection.Screenshot);
            Click(visual.TranslationAction.Button);
            var request = Assert.IsType<ScreenTranslationRequested>(Assert.Single(commands));
            controller.ShowFailure(request.RequestId, kind);

            Assert.Equal(OverlayInteractionMode.TranslationResult, state.Mode);
            var card = Card(visual);
            Assert.Equal(message, Message(card).Text);
            Assert.Equal(TestUiStrings.English.TranslationResultTitle, AutomationProperties.GetName(card));
            Assert.Same(TextTranslationVisualFactory.TranslateIconGeometry, Icon(card).Data);
            Assert.Equal(TestUiStrings.English.Close, Close(card).ToolTip);
            Assert.Equal(canRetry, ActionButton(card) is not null);
            Assert.Empty(toasts);
            Assert.Equal(Visibility.Collapsed, visual.TranslationAction.LoadingIndicator.Visibility);
            Assert.True(controller.HandleEscape());
            Assert.Equal(OverlayInteractionMode.Selecting, state.Mode);
            Assert.False(visual.Bottom.Stack.Children.Contains(visual.TranslationOverlay.StateHost));
            Assert.Single(commands);
            visual.Music.Waveform.Dispose();
            visual.Effects.SceneRipples.Dispose();
        });
        Assert.Null(failure);
    }

    [Fact]
    public void Retry_reuses_the_failed_target_and_image_with_a_new_request_id_once()
    {
        var failure = RunOnSta(() =>
        {
            var image = Source(640, 400);
            var visual = OverlayVisualFactory.CreateRoot(image, new Size(640, 400), 20, false, TestUiStrings.English);
            var state = new OverlayInteractionState();
            var commands = new List<IOverlayCommand>();
            var target = "de";
            using var controller = new ScreenTranslationOverlayController(
                visual.TranslationAction, visual.TranslationOverlay, visual.Bottom, visual.Effects, visual.Root,
                TestUiStrings.English, () => true, () => { }, () => target, commands.Add,
                mode => state.TransitionTo(mode), _ => { }, () => false, false, visual.Selection.Screenshot);
            Click(visual.TranslationAction.Button);
            var first = Assert.IsType<ScreenTranslationRequested>(Assert.Single(commands));
            controller.ShowFailure(first.RequestId, TranslationFailure.Network);
            target = "fr";
            var oldCard = Card(visual);
            var retry = Primary(oldCard);
            Click(retry);
            Click(retry);
            var second = Assert.IsType<ScreenTranslationRequested>(commands[1]);
            Assert.Equal(2, commands.Count);
            Assert.NotEqual(first.RequestId, second.RequestId);
            Assert.Same(image, second.Image);
            Assert.Equal("de", second.TargetLanguageTag);
            Assert.Equal(OverlayInteractionMode.Translating, state.Mode);
            Assert.Equal(Visibility.Visible, visual.TranslationAction.LoadingIndicator.Visibility);
            controller.ShowFailure(first.RequestId, TranslationFailure.RateLimited);
            controller.ShowResult(new ScreenTranslationResult(first.RequestId, image));
            Assert.Equal(OverlayInteractionMode.Translating, state.Mode);
            Assert.True(controller.IsTranslating);
            controller.CancelForClosing();
            Assert.False(visual.Bottom.Stack.Children.Contains(visual.TranslationOverlay.StateHost));
            visual.Music.Waveform.Dispose();
            visual.Effects.SceneRipples.Dispose();
        });
        Assert.Null(failure);
    }

    [Fact]
    public void Canceled_failure_stays_silent_and_returns_to_selecting()
    {
        var failure = RunOnSta(() =>
        {
            var visual = OverlayVisualFactory.CreateRoot(Source(320, 200), new Size(320, 200), 10, false, TestUiStrings.English);
            var state = new OverlayInteractionState();
            var commands = new List<IOverlayCommand>();
            var toasts = new List<ToastNotification>();
            using var controller = new ScreenTranslationOverlayController(
                visual.TranslationAction, visual.TranslationOverlay, visual.Bottom, visual.Effects, visual.Root,
                TestUiStrings.English, () => true, () => { }, () => "es", commands.Add,
                mode => state.TransitionTo(mode), toasts.Add, () => false, false, visual.Selection.Screenshot);
            Click(visual.TranslationAction.Button);
            controller.ShowFailure(Assert.IsType<ScreenTranslationRequested>(commands[0]).RequestId, TranslationFailure.Canceled);
            Assert.Equal(OverlayInteractionMode.Selecting, state.Mode);
            Assert.Empty(toasts);
            Assert.Empty(visual.TranslationOverlay.StateHost.Children);
            visual.Music.Waveform.Dispose();
            visual.Effects.SceneRipples.Dispose();
        });
        Assert.Null(failure);
    }

    [Fact]
    public void Closing_during_animated_exit_removes_the_host_and_old_callbacks()
    {
        var failure = RunOnSta(() =>
        {
            var visual = OverlayVisualFactory.CreateRoot(Source(640, 400), new Size(640, 400), 20, false, TestUiStrings.English);
            var window = new Window { Width = 640, Height = 400, Content = visual.Root };
            window.Show();
            var state = new OverlayInteractionState();
            var commands = new List<IOverlayCommand>();
            using (var controller = new ScreenTranslationOverlayController(
                visual.TranslationAction, visual.TranslationOverlay, visual.Bottom, visual.Effects, visual.Root,
                TestUiStrings.English, () => true, () => { }, () => "es", commands.Add,
                mode => state.TransitionTo(mode), _ => { }, () => true, false, visual.Selection.Screenshot))
            {
                Click(visual.TranslationAction.Button);
                var request = Assert.IsType<ScreenTranslationRequested>(commands[0]);
                controller.ShowFailure(request.RequestId, TranslationFailure.Network);
                var oldCard = Card(visual);
                Click(Close(oldCard));
                controller.CancelForClosing();
                Click(Primary(oldCard));
                Assert.Single(commands);
                Assert.False(visual.Bottom.Stack.Children.Contains(visual.TranslationOverlay.StateHost));
                Assert.Empty(visual.TranslationOverlay.StateHost.Children);
            }
            visual.Music.Waveform.Dispose();
            visual.Effects.SceneRipples.Dispose();
            window.Close();
        });
        Assert.Null(failure);
    }

    [Fact]
    public void Replacing_an_exiting_card_keeps_the_new_card_after_the_old_animation_completes()
    {
        var failure = RunOnSta(() =>
        {
            var visual = OverlayVisualFactory.CreateRoot(Source(640, 400), new Size(640, 400), 20, false, TestUiStrings.English);
            var window = new Window { Width = 640, Height = 400, Content = visual.Root };
            window.Show();
            var state = new OverlayInteractionState();
            using var controller = new ScreenTranslationOverlayController(
                visual.TranslationAction, visual.TranslationOverlay, visual.Bottom, visual.Effects, visual.Root,
                TestUiStrings.English, () => false, () => { }, () => "es", _ => { },
                mode => state.TransitionTo(mode), _ => { }, () => true, false, visual.Selection.Screenshot);
            Click(visual.TranslationAction.Button);
            var old = Card(visual);
            Click(Close(old));
            Click(visual.TranslationAction.Button);
            var replacement = Card(visual);
            Assert.NotSame(old, replacement);
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
            timer.Start();
            Dispatcher.PushFrame(frame);
            Assert.Same(replacement, Card(visual));
            Assert.Equal(OverlayInteractionMode.TranslationConsent, state.Mode);
            visual.Music.Waveform.Dispose();
            visual.Effects.SceneRipples.Dispose();
            window.Close();
        });
        Assert.Null(failure);
    }

    [Fact]
    public void Consent_waiting_cancel_and_show_original_follow_state_machine()
    {
        var failure = RunOnSta(() =>
        {
            var visual = OverlayVisualFactory.CreateRoot(Source(640, 400), new System.Windows.Size(640, 400), 20, false, TestUiStrings.English);
            var window = new Window { Width = 640, Height = 400, Content = visual.Root };
            window.Show();
            window.UpdateLayout();
            var state = new OverlayInteractionState();
            var commands = new List<IOverlayCommand>();
            var consent = false;
            using var controller = new ScreenTranslationOverlayController(
                visual.TranslationAction,
                visual.TranslationOverlay,
                visual.Bottom,
                visual.Effects,
                visual.Root,
                TestUiStrings.English,
                () => consent,
                () => consent = true,
                () => "es",
                commands.Add,
                target => state.TransitionTo(target),
                _ => { },
                () => false,
                false,
                visual.Selection.Screenshot);

            visual.TranslationAction.Button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(OverlayInteractionMode.TranslationConsent, state.Mode);
            Assert.True(controller.IsConsentOpen);
            Assert.Empty(commands);

            var consentCard = Assert.IsType<Border>(Assert.Single(visual.TranslationOverlay.StateHost.Children));
            var consentRoot = Assert.IsType<Grid>(consentCard.Child);
            var continueButton = Assert.IsType<Button>(consentRoot.Children[2]);
            continueButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(consent);
            Assert.Equal(OverlayInteractionMode.Translating, state.Mode);
            Assert.True(controller.IsTranslating);
            Assert.Equal(TestUiStrings.English.Translating, visual.TranslationAction.Button.ToolTip);
            Assert.Equal(Visibility.Visible, visual.TranslationAction.LoadingIndicator.Visibility);
            Assert.Equal(1, visual.TranslationAction.LoadingIndicator.Opacity);
            Assert.Equal(0, visual.TranslationAction.Icon.Opacity);
            var request = Assert.IsType<ScreenTranslationRequested>(Assert.Single(commands));
            controller.ShowResult(new ScreenTranslationResult(request.RequestId, Source(640, 400)));
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
                Source(640, 400),
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
                visual.Bottom,
                effects,
                visual.Root,
                TestUiStrings.English,
                () => true,
                () => { },
                () => "es",
                commands.Add,
                target => state.TransitionTo(target),
                _ => { },
                () => true,
                false,
                visual.Selection.Screenshot);

            visual.TranslationAction.Button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var request = Assert.IsType<ScreenTranslationRequested>(Assert.Single(commands));
            controller.ShowResult(new ScreenTranslationResult(request.RequestId, Source(640, 400)));
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
            var visual = OverlayVisualFactory.CreateRoot(Source(320, 200), new System.Windows.Size(320, 200), 10, false, TestUiStrings.English);
            var state = new OverlayInteractionState();
            var commands = new List<IOverlayCommand>();
            using var controller = new ScreenTranslationOverlayController(
                visual.TranslationAction,
                visual.TranslationOverlay,
                visual.Bottom,
                visual.Effects,
                visual.Root,
                TestUiStrings.English,
                () => true,
                () => { },
                () => "es",
                commands.Add,
                target => state.TransitionTo(target),
                _ => { },
                () => false,
                false,
                visual.Selection.Screenshot);

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

    private static BitmapSource Source(int width, int height)
    {
        var stride = width * 4;
        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null,
            new byte[stride * height], stride);
        image.Freeze();
        return image;
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static Border Card(OverlayVisual visual) => Assert.IsType<Border>(Assert.Single(visual.TranslationOverlay.StateHost.Children));
    private static Grid Root(Border card) => Assert.IsType<Grid>(card.Child);
    private static StackPanel Row(Border card) => Assert.IsType<StackPanel>(Root(card).Children[1]);
    private static TextBlock Title(Border card) => Assert.IsType<TextBlock>(Assert.IsType<StackPanel>(Row(card).Children[1]).Children[0]);
    private static TextBlock Message(Border card) => Row(card).Children[1] is TextBlock message
        ? message : Assert.IsType<TextBlock>(Assert.IsType<StackPanel>(Row(card).Children[1]).Children[1]);
    private static System.Windows.Shapes.Path Icon(Border card) =>
        Assert.IsType<System.Windows.Shapes.Path>(Assert.IsType<Border>(Row(card).Children[0]).Child);
    private static Button Close(Border card) => Assert.IsType<Button>(Root(card).Children[0]);
    private static Button? ActionButton(Border card) => Root(card).Children.OfType<Button>().Skip(1).SingleOrDefault();
    private static Button Primary(Border card) => Assert.IsType<Button>(ActionButton(card));

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

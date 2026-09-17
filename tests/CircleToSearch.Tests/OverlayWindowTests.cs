using System.Windows.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using CircleToSearch.Capture;
using CircleToSearch.Capture.OverlayInteractions;
using CircleToSearch.Search;
using CircleToSearch.Translation;
using Xunit;
using GdiBitmap = System.Drawing.Bitmap;
using GdiPoint = System.Drawing.Point;
using GdiRectangle = System.Drawing.Rectangle;

namespace CircleToSearch.Tests;

public sealed class OverlayWindowTests
{
    [Fact]
    public void Debug_reset_makes_the_next_translate_show_privacy_consent()
    {
        var failure = RunOnSta(() =>
        {
            using var frame = new GdiBitmap(640, 400);
            var bounds = new GdiRectangle(0, 0, 640, 400);
            var settings = new CircleToSearch.Settings.AppSettings
            {
                ImageTranslationPrivacyConsentAccepted = true,
            };
            var saves = 0;
            var service = TestSettings.Create(settings, _ => saves++);
            var commands = new List<IOverlayCommand>();
            var overlay = new OverlayWindow(frame, bounds, bounds, 1, new OverlayOptions(8, 12),
                TestUiStrings.English,
                TestOverlayControllers.CreateFactory(Clipboard.SetText, () => false,
                    translationConsentAccepted: () => service.Snapshot.ImageTranslationPrivacyConsentAccepted,
                    resetTranslationConsent: () => service.SetTranslationConsent(false).ThrowIfFailed(TestUiStrings.English.StorageSaveFailed)),
                overscan: false, publishCommand: commands.Add);
            overlay.Show();
            overlay.SetDebugPanelOpen(true);
            var visual = overlay.VisualState;
            visual.Debug.ResetTranslationConsentButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.False(service.Snapshot.ImageTranslationPrivacyConsentAccepted);
            Assert.Equal(1, saves);
            Assert.Equal(Visibility.Collapsed, visual.Debug.Panel.Visibility);
            visual.TranslationAction.Button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(OverlayInteractionMode.TranslationConsent, overlay.Mode);
            Assert.Empty(commands);
            Assert.Single(visual.TranslationOverlay.StateHost.Children);
            overlay.CloseFromSession();
            Dispatcher.Run();
        });
        Assert.Null(failure);
    }

    [Fact]
    public void Translation_cards_block_conflicting_controls_and_restore_them_on_dismiss()
    {
        var failure = RunOnSta(() =>
        {
            using var frame = new GdiBitmap(640, 400);
            var bounds = new GdiRectangle(0, 0, 640, 400);
            var commands = new List<IOverlayCommand>();
            var consent = false;
            var overlay = new OverlayWindow(frame, bounds, bounds, 1, new OverlayOptions(8, 12),
                TestUiStrings.English,
                TestOverlayControllers.CreateFactory(Clipboard.SetText, () => false,
                    translationConsentAccepted: () => consent,
                    acceptTranslationConsent: () => consent = true),
                overscan: false, publishCommand: commands.Add);
            overlay.Show();
            var visual = overlay.VisualState;
            var translate = visual.TranslationAction.Button;
            translate.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(OverlayInteractionMode.TranslationConsent, overlay.Mode);
            Assert.False(translate.IsEnabled);
            Assert.False(visual.Music.Button.IsEnabled);
            Assert.Equal(System.Windows.Input.Cursors.Arrow, overlay.Cursor);
            var consentCard = Assert.IsType<Border>(Assert.Single(visual.TranslationOverlay.StateHost.Children));
            overlay.UpdateLayout();
            Assert.True(consentCard.ActualHeight > 0);
            Assert.True(consentCard.TransformToAncestor(overlay).Transform(new Point()).Y >= 0);
            var continueButton = Assert.IsType<Button>(Assert.IsType<Grid>(consentCard.Child).Children[2]);
            Assert.True(continueButton.IsFocused);
            continueButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(consent);
            Assert.Equal(OverlayInteractionMode.Translating, overlay.Mode);
            Assert.True(translate.IsEnabled);
            var request = Assert.IsType<ScreenTranslationRequested>(Assert.Single(commands));
            overlay.ShowTranslationFailure(request.RequestId, TranslationFailure.Network);
            Assert.Equal(OverlayInteractionMode.TranslationResult, overlay.Mode);
            Assert.False(translate.IsEnabled);
            Assert.False(visual.Music.Button.IsEnabled);
            RaiseEscape(overlay);
            Assert.Equal(OverlayInteractionMode.Selecting, overlay.Mode);
            Assert.True(translate.IsEnabled);
            Assert.True(visual.Music.Button.IsEnabled);
            Assert.False(visual.Bottom.Stack.Children.Contains(visual.TranslationOverlay.StateHost));
            Assert.Single(commands);
            translate.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var second = Assert.IsType<ScreenTranslationRequested>(commands[1]);
            overlay.ShowTranslation(new ScreenTranslationResult(second.RequestId, (BitmapSource)visual.Selection.Screenshot.Source));
            Assert.Equal(OverlayInteractionMode.TranslationShown, overlay.Mode);
            Assert.True(translate.IsEnabled);
            Assert.Equal(TestUiStrings.English.ShowOriginal, translate.ToolTip);
            translate.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(OverlayInteractionMode.Selecting, overlay.Mode);
            overlay.CloseFromSession();
            Dispatcher.Run();
        });
        Assert.Null(failure);
    }

    private static readonly SearchProviderDescriptor[] Providers =
    [
        new(SearchProviderIds.GoogleLens, "Google Lens"),
        new(SearchProviderIds.YandexImages, "Yandex Images"),
    ];

    [Fact]
    public void Visual_factory_places_music_button_beside_not_inside_chip()
    {
        var failure = RunOnSta(() =>
        {
            var visual = OverlayVisualFactory.CreateRoot(
                null,
                new Size(640, 400),
                32,
                lightTheme: false,
                TestUiStrings.English);

            Assert.Same(visual.Actions.Chip, visual.Actions.Tray.Children[0]);
            Assert.Same(visual.TranslationAction.Button, visual.Actions.Tray.Children[1]);
            Assert.Same(visual.Music.Button, visual.Actions.Tray.Children[2]);
            Assert.DoesNotContain(visual.Music.Button, Descendants(visual.Actions.Chip));
            Assert.Equal(TestUiStrings.English.MusicRecognitionAction, visual.Music.Button.ToolTip);
            Assert.Equal(
                TestUiStrings.English.MusicRecognitionAction,
                AutomationProperties.GetName(visual.Music.Button));
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Music_button_finishes_with_music_action_without_selection()
    {
        var failure = RunOnSta(() =>
        {
            using var frame = new GdiBitmap(64, 48);
            var monitor = new GdiRectangle(0, 0, 64, 48);
            var overlay = new OverlayWindow(
                frame,
                monitor,
                monitor,
                1.0,
                new OverlayOptions(8, 12),
                TestUiStrings.English,
                TestOverlayControllers.CreateFactory());
            overlay.Show();
            var musicButton = Assert.Single(
                Descendants((DependencyObject)overlay.Content).OfType<Button>(),
                button => AutomationProperties.GetName(button) == TestUiStrings.English.MusicRecognitionAction);
            musicButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.Run();

            Assert.Equal(OverlayAction.MusicRecognition, overlay.Outcome?.Action);
            Assert.Null(overlay.Outcome?.Selection);
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Action_tray_hit_test_blocks_lasso_even_when_original_source_is_the_window()
    {
        var failure = RunOnSta(() =>
        {
            using var frame = new GdiBitmap(640, 400);
            var monitor = new GdiRectangle(0, 0, 640, 400);
            var overlay = new OverlayWindow(
                frame,
                monitor,
                monitor,
                1.0,
                new OverlayOptions(8, 12),
                TestUiStrings.English,
                TestOverlayControllers.CreateFactory(),
                overscan: false);
            overlay.Show();
            overlay.UpdateLayout();
            var musicButton = Assert.Single(
                Descendants((DependencyObject)overlay.Content).OfType<Button>(),
                button => AutomationProperties.GetName(button) == TestUiStrings.English.MusicRecognitionAction);
            var center = musicButton.TransformToAncestor(overlay).Transform(
                new Point(musicButton.ActualWidth / 2, musicButton.ActualHeight / 2));

            Assert.True(overlay.IsActionTrayInteraction(overlay, center));
            Assert.False(overlay.IsActionTrayInteraction(overlay, new Point(10, 10)));

            overlay.CancelFromCoordinator();
            Dispatcher.Run();
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Entrance_ripple_starts_on_a_rendered_scene_layer()
    {
        var failure = RunOnSta(() =>
        {
            if (!OverlayVisualResources.AnimationsEnabled()) return;
            using var frame = new GdiBitmap(640, 400);
            var monitor = new GdiRectangle(0, 0, 640, 400);
            var overlay = new OverlayWindow(
                frame,
                monitor,
                monitor,
                1,
                new OverlayOptions(8, 12),
                TestUiStrings.English,
                TestOverlayControllers.CreateFactory(),
                overscan: false,
                entranceOrigin: new GdiPoint(320, 200));
            overlay.Show();

            var renderFrame = new DispatcherFrame();
            var timeout = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(160) };
            timeout.Tick += (_, _) =>
            {
                timeout.Stop();
                renderFrame.Continue = false;
            };
            timeout.Start();
            Dispatcher.PushFrame(renderFrame);

            Assert.True(overlay.VisualState.Effects.SceneRippleLayer.ActualWidth > 0);
            Assert.True(overlay.VisualState.Effects.SceneRippleLayer.ActualHeight > 0);
            Assert.Equal(1, overlay.VisualState.Effects.SceneRipples.ActiveCount);
            overlay.CloseFromSession();
            Dispatcher.Run();
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Overlay_opens_and_shuts_down_without_dispatcher_exception()
    {
        var failure = RunOnSta(() =>
        {
            using var frame = new GdiBitmap(64, 48);
            var monitor = new GdiRectangle(0, 0, 64, 48);
            var overlay = new OverlayWindow(
                frame,
                monitor,
                monitor,
                1.0,
                new OverlayOptions(8, 12),
                TestUiStrings.English,
                TestOverlayControllers.CreateFactory());
            overlay.Show();
            PumpUntilShutdown(overlay);
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Overlay_on_negative_coordinates_monitor_opens_without_dispatcher_exception()
    {
        var failure = RunOnSta(() =>
        {
            using var frame = new GdiBitmap(100, 80);
            var monitor = new GdiRectangle(-1920, -80, 100, 80);
            var workArea = new GdiRectangle(-1920, -80, 100, 50);
            var overlay = new OverlayWindow(
                frame,
                monitor,
                workArea,
                1.25,
                new OverlayOptions(8, 12),
                TestUiStrings.English,
                TestOverlayControllers.CreateFactory());
            overlay.Show();
            PumpUntilShutdown(overlay);
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Cancel_from_coordinator_shuts_the_dispatcher_down()
    {
        var failure = RunOnSta(() =>
        {
            using var frame = new GdiBitmap(64, 48);
            var monitor = new GdiRectangle(0, 0, 64, 48);
            var overlay = new OverlayWindow(
                frame,
                monitor,
                monitor,
                1.0,
                new OverlayOptions(8, 12),
                TestUiStrings.English,
                TestOverlayControllers.CreateFactory());
            overlay.Show();
            overlay.CancelFromCoordinator();
            Dispatcher.Run();
            Assert.Null(overlay.Outcome);
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Session_close_forces_shutdown_after_cancel_has_already_started()
    {
        var failure = RunOnSta(() =>
        {
            using var frame = new GdiBitmap(64, 48);
            var monitor = new GdiRectangle(0, 0, 64, 48);
            var overlay = new OverlayWindow(
                frame,
                monitor,
                monitor,
                1.0,
                new OverlayOptions(8, 12),
                TestUiStrings.English,
                TestOverlayControllers.CreateFactory());
            overlay.Show();
            overlay.CancelFromCoordinator();
            overlay.Dispatcher.BeginInvoke(overlay.CloseFromSession, DispatcherPriority.Background);
            Dispatcher.Run();

            Assert.True(overlay.Dispatcher.HasShutdownFinished);
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Escape_closes_debug_then_provider_before_publishing_cancel_once()
    {
        var failure = RunOnSta(() =>
        {
            using var frame = new GdiBitmap(640, 400);
            var monitor = new GdiRectangle(0, 0, 640, 400);
            var commands = new List<IOverlayCommand>();
            var overlay = new OverlayWindow(
                frame,
                monitor,
                monitor,
                1,
                new OverlayLaunchOptions(
                    new OverlayOptions(8, 12),
                    TestUiStrings.English,
                    Providers,
                    SearchProviderIds.GoogleLens),
                commands.Add,
                TestOverlayControllers.CreateFactory(),
                overscan: false);
            overlay.Show();
            overlay.UpdateLayout();
            overlay.SetDebugPanelOpen(true);
            overlay.VisualState.Provider!.Button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            RaiseEscape(overlay);
            Assert.Equal(Visibility.Collapsed, overlay.VisualState.Debug.Panel.Visibility);
            Assert.Empty(commands);

            RaiseEscape(overlay);
            Assert.Empty(commands);

            RaiseEscape(overlay);
            overlay.CancelFromCoordinator();
            Assert.IsType<CancelSession>(Assert.Single(commands));
            Dispatcher.Run();
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Starting_music_recognition_closes_the_debug_panel()
    {
        var failure = RunOnSta(() =>
        {
            using var frame = new GdiBitmap(640, 400);
            var monitor = new GdiRectangle(0, 0, 640, 400);
            var commands = new List<IOverlayCommand>();
            var overlay = new OverlayWindow(
                frame,
                monitor,
                monitor,
                1,
                new OverlayLaunchOptions(
                    new OverlayOptions(8, 12),
                    TestUiStrings.English,
                    Providers,
                    SearchProviderIds.GoogleLens),
                commands.Add,
                TestOverlayControllers.CreateFactory(),
                overscan: false);
            overlay.Show();
            overlay.UpdateLayout();
            overlay.SetDebugPanelOpen(true);

            overlay.VisualState.Music.Button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.Equal(Visibility.Collapsed, overlay.VisualState.Debug.Panel.Visibility);
            Assert.IsType<StartMusicRecognition>(Assert.Single(commands));
            Assert.Equal(OverlayInteractionMode.Listening, overlay.Mode);
            overlay.CloseFromSession();
            Dispatcher.Run();
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Drag_below_minimum_resets_and_shows_toast_without_publishing_a_command()
    {
        var failure = RunOnSta(() =>
        {
            using var frame = new GdiBitmap(640, 400);
            var monitor = new GdiRectangle(0, 0, 640, 400);
            var commands = new List<IOverlayCommand>();
            var pointerPosition = PointerPositions(new Point(100, 100), new Point(111, 100));
            var overlay = new OverlayWindow(
                frame,
                monitor,
                monitor,
                1,
                new OverlayLaunchOptions(
                    new OverlayOptions(8, 12),
                    TestUiStrings.English,
                    Providers,
                    SearchProviderIds.GoogleLens),
                commands.Add,
                TestOverlayControllers.CreateFactory(_ => { }, () => false, pointerPosition),
                overscan: false);
            overlay.Show();
            overlay.UpdateLayout();
            var input = overlay.VisualState.Selection.InputSurface;
            RaisePointerGesture(input);

            Assert.False(overlay.FrameTransferred);
            Assert.Equal(OverlayInteractionMode.Selecting, overlay.Mode);
            Assert.Empty(commands);
            Assert.Empty(overlay.VisualState.Selection.Halo.Points);
            Assert.Empty(overlay.VisualState.Selection.Accent.Points);
            Assert.Same(System.Windows.Media.Geometry.Empty, overlay.VisualState.Selection.Sheen.Data);
            Assert.Same(System.Windows.Media.Geometry.Empty, overlay.VisualState.Selection.DimRect.Data);
            Assert.Same(System.Windows.Media.Geometry.Empty, overlay.VisualState.Selection.SelectionFrame.Data);
            Assert.Equal(1, overlay.VisualState.Selection.Dim.Opacity);
            Assert.Equal(0, overlay.VisualState.Selection.DimRect.Opacity);
            Assert.True(overlay.VisualState.Actions.Tray.IsHitTestVisible);

            var stack = overlay.VisualState.Bottom.Stack.Children.Cast<UIElement>().ToArray();
            Assert.Equal(3, stack.Length);
            var toastSlot = Assert.IsType<Grid>(stack[0]);
            Assert.Same(overlay.VisualState.Bottom.ResultSlot, stack[1]);
            Assert.Same(overlay.VisualState.Bottom.ActionSlot, stack[2]);
            var toastCard = Assert.IsType<Border>(Assert.Single(toastSlot.Children));
            var toastMessage = Assert.IsType<TextBlock>(toastCard.Child);
            Assert.Equal(TestUiStrings.English.SelectionTooSmall, toastMessage.Text);

            RaiseEscape(overlay);
            Assert.IsType<CancelSession>(Assert.Single(commands));
            Dispatcher.Run();
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Exact_click_is_rejected_and_returns_action_tray()
    {
        var failure = RunOnSta(() =>
        {
            using var frame = new GdiBitmap(640, 400);
            var monitor = new GdiRectangle(0, 0, 640, 400);
            var commands = new List<IOverlayCommand>();
            var clipboard = new List<string>();
            var local = new Point(120, 80);
            var overlay = new OverlayWindow(
                frame,
                monitor,
                monitor,
                1,
                new OverlayLaunchOptions(
                    new OverlayOptions(8, 12),
                    TestUiStrings.English,
                    Providers,
                    SearchProviderIds.GoogleLens),
                commands.Add,
                TestOverlayControllers.CreateFactory(clipboard.Add, () => false, PointerPositions(local, local)),
                overscan: false);
            overlay.Show();
            overlay.UpdateLayout();
            RaisePointerGesture(overlay.VisualState.Selection.InputSurface);

            Assert.Empty(clipboard);
            Assert.Empty(commands);
            Assert.Equal(OverlayInteractionMode.Selecting, overlay.Mode);
            Assert.False(overlay.FrameTransferred);
            Assert.False(overlay.VisualState.Selection.InputSurface.IsMouseCaptured);
            Assert.True(overlay.VisualState.Actions.Tray.IsHitTestVisible);
            var toastSlot = Assert.IsType<Grid>(overlay.VisualState.Bottom.Stack.Children[0]);
            var toastMessage = Assert.IsType<TextBlock>(Assert.IsType<Border>(Assert.Single(toastSlot.Children)).Child);
            Assert.Equal(TestUiStrings.English.SelectionTooSmall, toastMessage.Text);

            RaiseEscape(overlay);
            Dispatcher.Run();

            Assert.IsType<CancelSession>(Assert.Single(commands));
            Assert.False(overlay.FrameTransferred);
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Drag_at_minimum_diagonal_completes_visual_selection()
    {
        var failure = RunOnSta(() =>
        {
            using var frame = new GdiBitmap(640, 400);
            var monitor = new GdiRectangle(0, 0, frame.Width, frame.Height);
            var commands = new List<IOverlayCommand>();
            var pointerPosition = PointerPositions(new Point(100, 100), new Point(112, 100));
            var overlay = new OverlayWindow(
                frame,
                monitor,
                monitor,
                1,
                new OverlayLaunchOptions(
                    new OverlayOptions(8, 12),
                    TestUiStrings.English,
                    Providers,
                    SearchProviderIds.GoogleLens),
                commands.Add,
                TestOverlayControllers.CreateFactory(_ => { }, () => false, pointerPosition),
                overscan: false);
            overlay.Show();
            overlay.UpdateLayout();

            RaisePointerGesture(overlay.VisualState.Selection.InputSurface);

            var selection = Assert.IsType<VisualSelection>(Assert.Single(commands));
            Assert.Equal(new GdiRectangle(92, 92, 28, 16), selection.Selection.Bounds);
            Assert.True(overlay.FrameTransferred);
            Dispatcher.Run();
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Normal_lasso_pipeline_still_publishes_visual_selection()
    {
        var failure = RunOnSta(() =>
        {
            using var frame = new GdiBitmap(640, 400);
            var monitor = new GdiRectangle(0, 0, frame.Width, frame.Height);
            var commands = new List<IOverlayCommand>();
            var pointerPosition = PointerPositions(new Point(100, 100), new Point(140, 130));
            var overlay = new OverlayWindow(
                frame,
                monitor,
                monitor,
                1,
                new OverlayLaunchOptions(
                    new OverlayOptions(8, 12),
                    TestUiStrings.English,
                    Providers,
                    SearchProviderIds.GoogleLens),
                commands.Add,
                TestOverlayControllers.CreateFactory(_ => { }, () => false, pointerPosition),
                overscan: false);
            overlay.Show();
            overlay.UpdateLayout();

            RaisePointerGesture(overlay.VisualState.Selection.InputSurface);

            var selection = Assert.IsType<VisualSelection>(Assert.Single(commands));
            Assert.Equal(new GdiRectangle(92, 92, 56, 46), selection.Selection.Bounds);
            Assert.True(overlay.FrameTransferred);
            Dispatcher.Run();
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Debug_toast_buttons_spawn_all_tones_in_the_overlay_stack()
    {
        var failure = RunOnSta(() =>
        {
            using var frame = new GdiBitmap(640, 400);
            var monitor = new GdiRectangle(0, 0, 640, 400);
            var overlay = new OverlayWindow(
                frame,
                monitor,
                monitor,
                1,
                new OverlayLaunchOptions(
                    new OverlayOptions(8, 12),
                    TestUiStrings.English,
                    Providers,
                    SearchProviderIds.GoogleLens),
                _ => { },
                TestOverlayControllers.CreateFactory(_ => { }, () => false),
                overscan: false);
            overlay.Show();
            overlay.UpdateLayout();

            foreach (var button in overlay.VisualState.Debug.ToastButtons.Children.OfType<Button>())
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            var stack = overlay.VisualState.Bottom.Stack.Children.Cast<UIElement>().ToArray();
            Assert.Equal(5, stack.Length);
            Assert.Equal(
                [
                    TestUiStrings.English.DebugToastNeutral,
                    TestUiStrings.English.DebugToastError,
                    TestUiStrings.English.DebugToastSuccess,
                ],
                stack.Take(3)
                    .Select(slot => Assert.IsType<TextBlock>(
                        Assert.IsType<Border>(
                            Assert.Single(Assert.IsType<Grid>(slot).Children)).Child).Text)
                    .ToArray());
            Assert.Same(overlay.VisualState.Bottom.ResultSlot, stack[3]);
            Assert.Same(overlay.VisualState.Bottom.ActionSlot, stack[4]);

            overlay.CloseFromSession();
            Dispatcher.Run();
        });

        Assert.Null(failure);
    }

    private static void PumpUntilShutdown(OverlayWindow overlay)
    {
        overlay.Dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
        Dispatcher.Run();
    }

    private static void RaisePointerGesture(FrameworkElement input)
    {
        input.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(
            System.Windows.Input.Mouse.PrimaryDevice,
            0,
            System.Windows.Input.MouseButton.Left)
        {
            RoutedEvent = UIElement.MouseLeftButtonDownEvent,
            Source = input,
        });
        input.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(
            System.Windows.Input.Mouse.PrimaryDevice,
            0,
            System.Windows.Input.MouseButton.Left)
        {
            RoutedEvent = UIElement.MouseLeftButtonUpEvent,
            Source = input,
        });
    }

    private static Func<System.Windows.Input.MouseEventArgs, Point> PointerPositions(
        Point start,
        Point finish) =>
        e => e.RoutedEvent == UIElement.MouseLeftButtonUpEvent ? finish : start;

    private static void RaiseEscape(OverlayWindow overlay)
    {
        overlay.RaiseEvent(new System.Windows.Input.KeyEventArgs(
            System.Windows.Input.Keyboard.PrimaryDevice,
            PresentationSource.FromVisual(overlay),
            0,
            System.Windows.Input.Key.Escape)
        {
            RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent,
        });
    }

    private static Exception? RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        })
        {
            IsBackground = true,
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(30));
        Assert.True(!thread.IsAlive, "the STA thread did not finish in time");
        return failure;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

}

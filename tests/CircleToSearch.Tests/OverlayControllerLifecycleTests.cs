using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CircleToSearch.Capture;
using CircleToSearch.Capture.OverlayInteractions;
using CircleToSearch.MusicRecognition;
using CircleToSearch.MusicRecognition.Shazam;
using Xunit;
using GdiRectangle = System.Drawing.Rectangle;

namespace CircleToSearch.Tests;

public sealed class OverlayControllerLifecycleTests
{
    [Fact]
    public void Dispose_removes_pending_render_callback_and_releases_window()
    {
        WeakReference? controllerReference = null;
        WeakReference? windowReference = null;
        var failure = RunOnSta(() =>
        {
            CreateAndDisposePendingRenderController(out controllerReference, out windowReference);
            PumpFor(TimeSpan.FromMilliseconds(100));
            ForceCollection();
            Assert.False(controllerReference.IsAlive);
            Assert.False(windowReference.IsAlive);
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Dispose_stops_pending_selection_hold_callback()
    {
        var failure = RunOnSta(() =>
        {
            var visual = CreateVisual();
            var holdCompleted = 0;
            using var controller = CreateSelectionController(visual, () => holdCompleted++);
            controller.ShowSelectionFrame(new GdiRectangle(40, 30, 180, 120));
            Assert.True(controller.HasPendingHold);

            controller.Dispose();
            Assert.False(controller.HasPendingHold);
            PumpFor(TimeSpan.FromMilliseconds(600));

            Assert.Equal(0, holdCompleted);
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Dispose_stops_pending_copy_restore_callback()
    {
        var failure = RunOnSta(() =>
        {
            var visual = CreateVisual();
            var commands = new List<IOverlayCommand>();
            using var controller = CreateMusicController(visual, commands, animationsEnabled: false);
            controller.ShowResult(MatchedOutcome());
            var copy = Descendants(visual.Music.ResultHost).OfType<Button>()
                .Single(button => AutomationProperties.GetName(button) == TestUiStrings.English.CopyTrackInfo);
            copy.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(TestUiStrings.English.Copied, AutomationProperties.GetName(copy));
            Assert.Equal(1, controller.PendingCopyRestoreCount);

            controller.Dispose();
            Assert.Equal(0, controller.PendingCopyRestoreCount);
            PumpFor(TimeSpan.FromMilliseconds(1450));

            Assert.Equal(TestUiStrings.English.Copied, AutomationProperties.GetName(copy));
            Assert.Empty(commands);
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Debug_toast_buttons_request_every_tone_with_their_localized_message()
    {
        var failure = RunOnSta(() =>
        {
            var visual = CreateVisual();
            var notifications = new List<ToastNotification>();
            using var controller = CreateMusicController(
                visual,
                [],
                animationsEnabled: false,
                notifications.Add);

            foreach (var button in visual.Music.DebugToastButtons.Children.OfType<Button>())
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.Collection(
                notifications,
                notification =>
                {
                    Assert.Equal(ToastTone.Neutral, notification.Tone);
                    Assert.Equal(TestUiStrings.English.DebugToastNeutral, notification.Message);
                },
                notification =>
                {
                    Assert.Equal(ToastTone.Error, notification.Tone);
                    Assert.Equal(TestUiStrings.English.DebugToastError, notification.Message);
                },
                notification =>
                {
                    Assert.Equal(ToastTone.Success, notification.Tone);
                    Assert.Equal(TestUiStrings.English.DebugToastSuccess, notification.Message);
                });
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Dispose_aborts_pending_match_dispatcher_operation()
    {
        var failure = RunOnSta(() =>
        {
            var visual = CreateVisual();
            var commands = new List<IOverlayCommand>();
            using var controller = CreateMusicController(visual, commands, animationsEnabled: true);
            controller.ShowResult(MatchedOutcome());
            Assert.True(controller.HasPendingMatchRipple);

            controller.Dispose();
            Assert.False(controller.HasPendingMatchRipple);
            PumpFor(TimeSpan.FromMilliseconds(100));

            Assert.Equal(0, visual.Effects.SceneRipples.ActiveCount);
            Assert.Empty(commands);
            visual.Effects.SceneRipples.Dispose();
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Animated_dismiss_keeps_card_visible_and_noninteractive_until_cleanup()
    {
        var failure = RunOnSta(() =>
        {
            var visual = CreateVisual();
            var window = ShowVisual(visual);
            var commands = new List<IOverlayCommand>();
            using var controller = CreateMusicController(visual, commands, animationsEnabled: true);
            controller.ShowResult(MatchedOutcome());
            var card = Assert.Single(visual.Music.ResultHost.Children.OfType<FrameworkElement>());

            controller.DismissResult();

            Assert.True(controller.HasPendingResultExit);
            Assert.Equal(Visibility.Visible, visual.Music.ResultHost.Visibility);
            Assert.False(visual.Music.ResultHost.IsHitTestVisible);
            Assert.Same(card, Assert.Single(visual.Music.ResultHost.Children));
            PumpFor(TimeSpan.FromMilliseconds(220));

            Assert.False(controller.HasPendingResultExit);
            Assert.Equal(Visibility.Collapsed, visual.Music.ResultHost.Visibility);
            Assert.True(visual.Music.ResultHost.IsHitTestVisible);
            Assert.Empty(visual.Music.ResultHost.Children);
            visual.Effects.SceneRipples.Dispose();
            window.Content = null;
            window.Close();
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Disabled_dismiss_collapses_and_clears_synchronously()
    {
        var failure = RunOnSta(() =>
        {
            var visual = CreateVisual();
            var commands = new List<IOverlayCommand>();
            using var controller = CreateMusicController(visual, commands, animationsEnabled: false);
            controller.ShowResult(MatchedOutcome());

            controller.DismissResult();

            Assert.False(controller.HasPendingResultExit);
            Assert.Equal(Visibility.Collapsed, visual.Music.ResultHost.Visibility);
            Assert.True(visual.Music.ResultHost.IsHitTestVisible);
            Assert.Empty(visual.Music.ResultHost.Children);
            visual.Effects.SceneRipples.Dispose();
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Retry_listening_starts_while_old_card_finishes_noninteractive_exit()
    {
        var failure = RunOnSta(() =>
        {
            var visual = CreateVisual();
            var window = ShowVisual(visual);
            var commands = new List<IOverlayCommand>();
            using var controller = CreateMusicController(visual, commands, animationsEnabled: true);
            controller.ShowResult(MusicRecognitionOutcome.From(MusicRecognitionStatus.NoMatch));

            controller.ShowListening();

            Assert.Equal(Visibility.Visible, visual.Music.ListeningLayer.Visibility);
            Assert.True(visual.Music.Waveform.IsRendering);
            Assert.Equal(Visibility.Visible, visual.Music.ResultHost.Visibility);
            Assert.False(visual.Music.ResultHost.IsHitTestVisible);
            Assert.NotEmpty(visual.Music.ResultHost.Children);
            PumpFor(TimeSpan.FromMilliseconds(220));

            Assert.Equal(Visibility.Collapsed, visual.Music.ResultHost.Visibility);
            Assert.Empty(visual.Music.ResultHost.Children);
            visual.Effects.SceneRipples.Dispose();
            window.Content = null;
            window.Close();
        });

        Assert.Null(failure);
    }

    [Fact]
    public void New_result_cancels_old_exit_and_stale_deadline_cannot_clear_it()
    {
        var failure = RunOnSta(() =>
        {
            var visual = CreateVisual();
            var window = ShowVisual(visual);
            var commands = new List<IOverlayCommand>();
            using var controller = CreateMusicController(visual, commands, animationsEnabled: true);
            controller.ShowResult(MatchedOutcome());
            controller.DismissResult();
            Assert.True(controller.HasPendingResultExit);

            controller.ShowResult(MusicRecognitionOutcome.From(MusicRecognitionStatus.NoAudio));
            var replacement = Assert.Single(visual.Music.ResultHost.Children.OfType<FrameworkElement>());
            PumpFor(TimeSpan.FromMilliseconds(220));

            Assert.False(controller.HasPendingResultExit);
            Assert.Equal(Visibility.Visible, visual.Music.ResultHost.Visibility);
            Assert.True(visual.Music.ResultHost.IsHitTestVisible);
            Assert.Same(replacement, Assert.Single(visual.Music.ResultHost.Children));
            visual.Effects.SceneRipples.Dispose();
            window.Content = null;
            window.Close();
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Production_result_mutations_move_a_persistent_slot_without_replacing_component_transforms()
    {
        var failure = RunOnSta(() =>
        {
            var visual = CreateVisual();
            var toastSlot = new Grid();
            toastSlot.Children.Add(new Border { Width = 180, Height = 32 });
            visual.Bottom.Stack.Children.Insert(0, toastSlot);
            var window = ShowVisual(visual);
            var commands = new List<IOverlayCommand>();
            using var controller = CreateMusicController(visual, commands, animationsEnabled: true);
            var trayLift = visual.Actions.Lift;
            var oldToastY = toastSlot.TranslatePoint(new Point(), visual.Root).Y;

            controller.ShowResult(MatchedOutcome());

            var toastOffset = Assert.IsType<TranslateTransform>(toastSlot.RenderTransform);
            Assert.Equal(oldToastY, toastSlot.TranslatePoint(new Point(), visual.Root).Y, 2);
            Assert.InRange(toastOffset.Y, 63, 65);
            Assert.Same(trayLift, visual.Actions.Tray.RenderTransform);
            var matchedCard = Assert.Single(visual.Music.ResultHost.Children.OfType<FrameworkElement>());
            var matchedTransforms = MusicResultTransitions.GetTransforms(matchedCard);
            Assert.Same(matchedTransforms.Translate,
                Assert.IsType<TransformGroup>(matchedCard.RenderTransform).Children[1]);
            PumpFor(TimeSpan.FromMilliseconds(260));
            var matchedToastY = toastSlot.TranslatePoint(new Point(), visual.Root).Y;

            controller.ShowResult(MusicRecognitionOutcome.From(MusicRecognitionStatus.NoAudio));

            Assert.Equal(matchedToastY, toastSlot.TranslatePoint(new Point(), visual.Root).Y, 2);
            Assert.True(toastOffset.HasAnimatedProperties);
            Assert.Same(trayLift, visual.Actions.Tray.RenderTransform);
            var replacement = Assert.Single(visual.Music.ResultHost.Children.OfType<FrameworkElement>());
            Assert.NotSame(matchedCard, replacement);
            Assert.IsType<TransformGroup>(replacement.RenderTransform);
            PumpFor(TimeSpan.FromMilliseconds(260));
            var tallToastY = toastSlot.TranslatePoint(new Point(), visual.Root).Y;
            Assert.True(tallToastY < matchedToastY);

            controller.DismissResult();
            Assert.Equal(Visibility.Visible, visual.Music.ResultHost.Visibility);
            PumpFor(TimeSpan.FromMilliseconds(190));

            Assert.Equal(Visibility.Collapsed, visual.Music.ResultHost.Visibility);
            Assert.True(toastOffset.HasAnimatedProperties);
            Assert.Same(trayLift, visual.Actions.Tray.RenderTransform);
            PumpFor(TimeSpan.FromMilliseconds(260));
            Assert.Equal(oldToastY, toastSlot.TranslatePoint(new Point(), visual.Root).Y, 2);
            Assert.Equal(0, toastOffset.Y, 3);
            Assert.False(toastOffset.HasAnimatedProperties);

            visual.Effects.SceneRipples.Dispose();
            window.Content = null;
            window.Close();
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Dispose_cancels_pending_result_exit()
    {
        var failure = RunOnSta(() =>
        {
            var visual = CreateVisual();
            var window = ShowVisual(visual);
            var commands = new List<IOverlayCommand>();
            var controller = CreateMusicController(visual, commands, animationsEnabled: true);
            controller.ShowResult(MatchedOutcome());
            controller.DismissResult();
            Assert.True(controller.HasPendingResultExit);

            controller.Dispose();

            Assert.False(controller.HasPendingResultExit);
            Assert.Empty(visual.Music.ResultHost.Children);
            PumpFor(TimeSpan.FromMilliseconds(220));
            Assert.Empty(commands);
            visual.Effects.SceneRipples.Dispose();
            window.Content = null;
            window.Close();
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Dispose_during_result_exit_releases_controller_and_window()
    {
        WeakReference? controllerReference = null;
        WeakReference? windowReference = null;
        var failure = RunOnSta(() =>
        {
            CreateAndDisposePendingResultController(out controllerReference, out windowReference);
            PumpFor(TimeSpan.FromMilliseconds(220));
            ForceCollection();

            Assert.False(controllerReference.IsAlive);
            Assert.False(windowReference.IsAlive);
        });

        Assert.Null(failure);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CreateAndDisposePendingRenderController(
        out WeakReference controllerReference,
        out WeakReference windowReference)
    {
        var visual = CreateVisual();
        var window = new Window
        {
            Width = 640,
            Height = 400,
            Content = visual.Root,
        };
        window.Show();
        window.UpdateLayout();
        var controller = CreateSelectionController(visual, () => { }, window);
        var input = visual.Selection.InputSurface;
        input.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
        {
            RoutedEvent = UIElement.MouseLeftButtonDownEvent,
            Source = input,
        });
        Assert.True(controller.HasPendingRevealUpdate);

        controllerReference = new WeakReference(controller);
        windowReference = new WeakReference(window);
        controller.Dispose();
        Assert.False(controller.HasPendingRevealUpdate);
        Assert.NotSame(input, Mouse.Captured);
        window.Content = null;
        window.Close();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CreateAndDisposePendingResultController(
        out WeakReference controllerReference,
        out WeakReference windowReference)
    {
        var visual = CreateVisual();
        var window = ShowVisual(visual);
        var controller = CreateMusicController(visual, [], animationsEnabled: true);
        controller.ShowResult(MatchedOutcome());
        controller.DismissResult();
        Assert.True(controller.HasPendingResultExit);

        controllerReference = new WeakReference(controller);
        windowReference = new WeakReference(window);
        controller.Dispose();
        visual.Effects.SceneRipples.Dispose();
        window.Content = null;
        window.Close();
    }

    private static SelectionOverlayController CreateSelectionController(
        OverlayVisual visual,
        Action holdCompleted,
        FrameworkElement? coordinateRoot = null) =>
        new(
            visual.Selection,
            coordinateRoot ?? visual.Root,
            new GdiRectangle(0, 0, 640, 400),
            1,
            8,
            12,
            false,
            () => true,
            (_, _) => true,
            () => { },
            _ => { },
            () => { },
            holdCompleted);

    private static MusicOverlayController CreateMusicController(
        OverlayVisual visual,
        List<IOverlayCommand> commands,
        bool animationsEnabled,
        Action<ToastNotification>? showToast = null) =>
        new(
            visual.Music,
            visual.Bottom.LayoutTransitions,
            visual.Effects,
            visual.Root,
            TestUiStrings.English,
            lightTheme: false,
            debugEnabled: true,
            () => OverlayInteractionMode.MusicResult,
            () => { },
            () => { },
            _ => { },
            showToast ?? (_ => { }),
            commands.Add,
            _ => { },
            () => animationsEnabled);

    private static OverlayVisual CreateVisual() => OverlayVisualFactory.CreateRoot(
        null,
        new Size(640, 400),
        32,
        lightTheme: false,
        TestUiStrings.English);

    private static Window ShowVisual(OverlayVisual visual)
    {
        var window = new Window
        {
            Width = 640,
            Height = 400,
            Content = visual.Root,
        };
        window.Show();
        window.UpdateLayout();
        return window;
    }

    private static MusicRecognitionOutcome MatchedOutcome() =>
        MusicRecognitionOutcome.Matched(new ShazamRecognition(
            "Track",
            "Artist",
            null,
            null,
            null,
            null,
            "https://www.shazam.com/track/1"));

    private static void PumpFor(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = duration };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static void ForceCollection()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
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
        thread.Join(TimeSpan.FromSeconds(10));
        Assert.False(thread.IsAlive, "the STA thread did not finish in time");
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

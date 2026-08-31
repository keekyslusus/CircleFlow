using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
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
        bool animationsEnabled) =>
        new(
            visual.Music,
            visual.Effects,
            visual.Root,
            TestUiStrings.English,
            lightTheme: false,
            debugEnabled: true,
            () => OverlayInteractionMode.MusicResult,
            () => { },
            () => { },
            _ => { },
            commands.Add,
            _ => { },
            () => animationsEnabled);

    private static OverlayVisual CreateVisual() => OverlayVisualFactory.CreateRoot(
        null,
        new Size(640, 400),
        32,
        lightTheme: false,
        TestUiStrings.English);

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

using System.Windows.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using CircleToSearch.Capture;
using Xunit;
using GdiBitmap = System.Drawing.Bitmap;
using GdiRectangle = System.Drawing.Rectangle;

namespace CircleToSearch.Tests;

public sealed class OverlayWindowTests
{
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

            Assert.Same(visual.Chip, visual.ActionTray.Children[0]);
            Assert.Same(visual.MusicButton, visual.ActionTray.Children[1]);
            Assert.DoesNotContain(visual.MusicButton, Descendants(visual.Chip));
            Assert.Equal(TestUiStrings.English.MusicRecognitionAction, visual.MusicButton.ToolTip);
            Assert.Equal(
                TestUiStrings.English.MusicRecognitionAction,
                AutomationProperties.GetName(visual.MusicButton));
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
                TestUiStrings.English);
            overlay.Show();
            var musicButton = Assert.Single(Descendants((DependencyObject)overlay.Content).OfType<Button>());
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
                overscan: false);
            overlay.Show();
            overlay.UpdateLayout();
            var musicButton = Assert.Single(Descendants((DependencyObject)overlay.Content).OfType<Button>());
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
                TestUiStrings.English);
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
                TestUiStrings.English);
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
                TestUiStrings.English);
            overlay.Show();
            overlay.CancelFromCoordinator();
            Dispatcher.Run();
            Assert.Null(overlay.Outcome);
        });

        Assert.Null(failure);
    }

    private static void PumpUntilShutdown(OverlayWindow overlay)
    {
        overlay.Dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
        Dispatcher.Run();
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

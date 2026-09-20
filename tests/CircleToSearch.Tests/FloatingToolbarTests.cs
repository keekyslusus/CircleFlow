using System.Windows;
using System.Windows.Threading;
using CircleToSearch.Capture;
using CircleToSearch.Ui;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class FloatingToolbarTests
{
    [Fact]
    public void Hidden_toolbar_stops_input_immediately_and_reopening_cancels_pending_exit()
    {
        RunOnSta(() =>
        {
            var toolbar = new FloatingToolbar(PluginPalette.For(false).FloatingToolbar, () => true);
            var copy = toolbar.AddAction(TestUiStrings.English.TextCopy);
            var window = new Window { Width = 400, Height = 200, Content = toolbar.Layer };
            try
            {
                window.Show();
                toolbar.Show(new Rect(120, 100, 80, 20), new Size(400, 200));
                Pump(280);
                Assert.Equal(1, toolbar.Surface.Opacity, 3);

                toolbar.Hide();
                Assert.False(toolbar.IsOpen);
                Assert.False(copy.IsEnabled);
                Assert.False(toolbar.Surface.IsHitTestVisible);
                Assert.Equal(Visibility.Visible, toolbar.Surface.Visibility);
                Pump(45);
                var opacity = toolbar.Surface.Opacity;
                toolbar.Show(new Rect(120, 100, 80, 20), new Size(400, 200));
                Pump(20);
                Assert.InRange(toolbar.Surface.Opacity, opacity, 0.999);
                Pump(300);

                Assert.True(toolbar.IsOpen);
                Assert.True(copy.IsEnabled);
                Assert.True(toolbar.Surface.IsHitTestVisible);
                Assert.Equal(Visibility.Visible, toolbar.Surface.Visibility);
                Assert.Equal(1, toolbar.Surface.Opacity, 3);
                toolbar.Hide();
                toolbar.Hide();
                Pump(200);
                Assert.Equal(Visibility.Collapsed, toolbar.Surface.Visibility);
                Assert.False(toolbar.Surface.HasAnimatedProperties);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void Disabled_animation_policy_and_immediate_hide_leave_no_animation_clocks()
    {
        RunOnSta(() =>
        {
            var enabled = false;
            var toolbar = new FloatingToolbar(PluginPalette.For(true).FloatingToolbar, () => enabled);
            toolbar.AddAction(TestUiStrings.English.TextCopy);
            toolbar.Show(new Rect(100, 100, 40, 20), new Size(400, 200));
            Assert.Equal(1, toolbar.Surface.Opacity);
            Assert.False(toolbar.Surface.HasAnimatedProperties);
            toolbar.Hide();
            Assert.Equal(Visibility.Collapsed, toolbar.Surface.Visibility);

            enabled = true;
            toolbar.Show(new Rect(100, 100, 40, 20), new Size(400, 200));
            toolbar.Hide(animate: false);
            var transforms = CardTransitions.GetTransforms(toolbar.Surface);
            Assert.False(toolbar.IsOpen);
            Assert.Equal(Visibility.Collapsed, toolbar.Surface.Visibility);
            Assert.False(toolbar.Surface.HasAnimatedProperties);
            Assert.False(transforms.Scale.HasAnimatedProperties);
            Assert.False(transforms.Translate.HasAnimatedProperties);
        });
    }

    [Fact]
    public void Unloading_during_exit_cancels_animations_and_collapses_toolbar()
    {
        RunOnSta(() =>
        {
            var toolbar = new FloatingToolbar(PluginPalette.For(false).FloatingToolbar, () => true);
            toolbar.AddAction(TestUiStrings.English.TextCopy);
            toolbar.Show(new Rect(100, 100, 40, 20), new Size(400, 200));
            toolbar.Hide();
            toolbar.Surface.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            Assert.False(toolbar.IsOpen);
            Assert.Equal(Visibility.Collapsed, toolbar.Surface.Visibility);
            Assert.False(toolbar.Surface.HasAnimatedProperties);
            Pump(240);
            Assert.Equal(Visibility.Collapsed, toolbar.Surface.Visibility);
        });
    }

    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)));
        Assert.Null(failure);
    }
}

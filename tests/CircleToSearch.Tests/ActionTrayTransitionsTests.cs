using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CircleToSearch.Capture;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class ActionTrayTransitionsTests
{
    [Fact]
    public void Return_preserves_current_values_then_restores_interaction()
    {
        Assert.Null(RunOnSta(() =>
        {
            var lift = new TranslateTransform { Y = 7 };
            var tray = new StackPanel
            {
                Opacity = 0.42,
                IsHitTestVisible = false,
                RenderTransform = lift,
            };
            var visual = new ActionTrayVisual(tray, new Border(), lift);
            var window = new Window { Width = 200, Height = 100, Content = tray };
            window.Show();

            ActionTrayTransitions.BeginReturn(visual, animationsEnabled: true);
            PumpFor(TimeSpan.FromMilliseconds(15));

            Assert.True(tray.IsHitTestVisible);
            Assert.InRange(tray.Opacity, 0.42, 1);
            Assert.InRange(lift.Y, 0, 7);
            PumpFor(TimeSpan.FromMilliseconds(230));
            Assert.Equal(1, tray.Opacity, 3);
            Assert.Equal(0, lift.Y, 3);
            window.Close();
        }));
    }

    [Fact]
    public void Disabled_return_is_synchronous_and_installs_no_clocks()
    {
        Assert.Null(RunOnSta(() =>
        {
            var lift = new TranslateTransform { Y = 7 };
            var tray = new StackPanel { Opacity = 0.42, IsHitTestVisible = false };
            var visual = new ActionTrayVisual(tray, new Border(), lift);

            ActionTrayTransitions.BeginReturn(visual, animationsEnabled: false);

            Assert.Equal(1, tray.Opacity);
            Assert.Equal(0, lift.Y);
            Assert.True(tray.IsHitTestVisible);
            Assert.False(tray.HasAnimatedProperties);
            Assert.False(lift.HasAnimatedProperties);
        }));
    }

    [Fact]
    public void Exit_during_return_continues_from_current_opacity_without_a_jump()
    {
        Assert.Null(RunOnSta(() =>
        {
            var lift = new TranslateTransform();
            var tray = new StackPanel { Opacity = 0.35, RenderTransform = lift };
            var visual = new ActionTrayVisual(tray, new Border(), lift);
            var window = new Window { Width = 200, Height = 100, Content = tray };
            window.Show();
            window.UpdateLayout();

            ActionTrayTransitions.BeginReturn(visual, animationsEnabled: true);
            PumpFor(TimeSpan.FromMilliseconds(55));
            var opacityBeforeExit = tray.Opacity;
            Assert.InRange(opacityBeforeExit, 0.35, 0.99);

            ActionTrayTransitions.BeginExit(visual, animationsEnabled: true);
            PumpFor(TimeSpan.FromMilliseconds(15));

            Assert.False(tray.IsHitTestVisible);
            Assert.InRange(tray.Opacity, 0, opacityBeforeExit);
            PumpFor(TimeSpan.FromMilliseconds(190));
            Assert.Equal(0, tray.Opacity, 3);
            window.Close();
        }));
    }

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
        Assert.False(thread.IsAlive);
        return failure;
    }
}

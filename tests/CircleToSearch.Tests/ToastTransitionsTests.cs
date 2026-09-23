using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CircleToSearch.Capture;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class ToastTransitionsTests
{
    [Fact]
    public void Disabled_entrance_and_exit_are_synchronous_without_clocks()
    {
        Assert.Null(RunOnSta(time =>
        {
            var card = new Border();
            ToastTransitions.BeginEntrance(card, animationsEnabled: false);
            var transforms = ToastTransitions.GetTransforms(card);
            Assert.Equal(1, card.Opacity);
            Assert.Equal(1, transforms.Scale.ScaleX);
            Assert.Equal(0, transforms.Translate.Y);
            Assert.False(card.HasAnimatedProperties);
            Assert.False(transforms.Scale.HasAnimatedProperties);
            Assert.False(transforms.Translate.HasAnimatedProperties);

            var completions = 0;
            using var exit = ToastTransitions.BeginExit(card, false, () => completions++);
            Assert.True(exit.IsCompleted);
            Assert.Equal(1, completions);
            Assert.Equal(0, card.Opacity);
            Assert.Equal(0.98, transforms.Scale.ScaleX);
            Assert.Equal(-8, transforms.Translate.Y);
        }));
    }

    [Fact]
    public void Animated_exit_completes_once_and_disposed_exit_never_completes()
    {
        Assert.Null(RunOnSta(time =>
        {
            var card = new Border();
            var host = new Window { Width = 200, Height = 100, Content = card };
            host.Show();
            ToastTransitions.BeginEntrance(card, animationsEnabled: true);
            time.Advance(40);
            var currentOpacity = card.Opacity;
            var transforms = ToastTransitions.GetTransforms(card);
            var currentY = transforms.Translate.Y;

            var completions = 0;
            using (var exit = ToastTransitions.BeginExit(card, true, () => completions++))
            {
                time.Advance(15);
                Assert.InRange(card.Opacity, 0, currentOpacity);
                Assert.InRange(transforms.Translate.Y, -8, currentY);
                time.Advance(220);
                Assert.True(exit.IsCompleted);
                Assert.Equal(1, completions);
            }

            ToastTransitions.BeginEntrance(card, animationsEnabled: true);
            var canceled = ToastTransitions.BeginExit(card, true, () => completions++);
            canceled.Dispose();
            time.Advance(220);
            Assert.Equal(1, completions);
            host.Close();
        }));
    }

    [Fact]
    public void Settle_preserves_effective_values_and_removes_animation_clocks()
    {
        Assert.Null(RunOnSta(time =>
        {
            var card = new Border();
            var host = new Window { Width = 200, Height = 100, Content = card };
            try
            {
                host.Show();
                ToastTransitions.BeginEntrance(card, animationsEnabled: true);
                time.Advance(40);
                var transforms = ToastTransitions.GetTransforms(card);
                var opacity = card.Opacity;
                var scaleX = transforms.Scale.ScaleX;
                var scaleY = transforms.Scale.ScaleY;
                var offset = transforms.Translate.Y;
                Assert.InRange(opacity, 0, 0.999);

                ToastTransitions.Settle(card);

                Assert.Equal(opacity, card.Opacity, 3);
                Assert.Equal(scaleX, transforms.Scale.ScaleX, 3);
                Assert.Equal(scaleY, transforms.Scale.ScaleY, 3);
                Assert.Equal(offset, transforms.Translate.Y, 3);
                Assert.False(card.HasAnimatedProperties);
                Assert.False(transforms.Scale.HasAnimatedProperties);
                Assert.False(transforms.Translate.HasAnimatedProperties);
                time.Advance(220);
                Assert.Equal(opacity, card.Opacity, 3);
                Assert.Equal(offset, transforms.Translate.Y, 3);
            }
            finally
            {
                host.Close();
            }
        }));
    }

    private static Exception? RunOnSta(Action<ManualAnimationClock> action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var time = ManualAnimationClock.Install();
                action(time);
            }
            catch (Exception exception) { failure = exception; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(10));
        Assert.False(thread.IsAlive);
        return failure;
    }
}

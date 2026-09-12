using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using CircleToSearch.Capture;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class StateCardTransitionsTests
{
    [Theory]
    [InlineData(48)]
    [InlineData(112)]
    public void Entrance_owns_centered_render_transforms_and_settles_without_reflow(double cardHeight)
    {
        var failure = RunOnSta(() =>
        {
            var card = new Border { Width = 180, Height = cardHeight };
            var tray = new Border { Width = 240, Height = 44 };
            var stack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
            };
            stack.Children.Add(card);
            stack.Children.Add(tray);
            var window = new Window { Width = 640, Height = 400, Content = stack };
            window.Show();
            window.UpdateLayout();
            var cardSlot = LayoutInformation.GetLayoutSlot(card);
            var trayOrigin = tray.TranslatePoint(new Point(), stack);

            StateCardTransitions.BeginEntrance(card, animationsEnabled: true);
            var transforms = StateCardTransitions.GetTransforms(card);
            var group = Assert.IsType<TransformGroup>(card.RenderTransform);
            PumpFor(TimeSpan.FromMilliseconds(15));

            Assert.Equal(new Point(0.5, 0.5), card.RenderTransformOrigin);
            Assert.Collection(
                group.Children,
                item => Assert.Same(transforms.Scale, item),
                item => Assert.Same(transforms.Translate, item));
            Assert.True(card.HasAnimatedProperties);
            Assert.InRange(card.Opacity, 0, 0.999);
            Assert.InRange(transforms.Scale.ScaleX, 0.96, 0.999);
            Assert.InRange(transforms.Scale.ScaleY, 0.96, 0.999);
            Assert.InRange(transforms.Translate.Y, 0.001, 8);
            Assert.Equal(cardSlot, LayoutInformation.GetLayoutSlot(card));
            Assert.Equal(trayOrigin, tray.TranslatePoint(new Point(), stack));

            PumpFor(TimeSpan.FromMilliseconds(260));

            Assert.Equal(1, card.Opacity, 3);
            Assert.Equal(1, transforms.Scale.ScaleX, 3);
            Assert.Equal(1, transforms.Scale.ScaleY, 3);
            Assert.Equal(0, transforms.Translate.Y, 3);
            Assert.Equal(cardSlot, LayoutInformation.GetLayoutSlot(card));
            Assert.Equal(trayOrigin, tray.TranslatePoint(new Point(), stack));
            window.Close();
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Exit_during_entrance_starts_from_effective_values_and_completes_once()
    {
        var failure = RunOnSta(() =>
        {
            var card = new Border { Width = 180, Height = 72 };
            var window = new Window { Width = 320, Height = 200, Content = card };
            window.Show();
            window.UpdateLayout();
            StateCardTransitions.BeginEntrance(card, animationsEnabled: true);
            PumpFor(TimeSpan.FromMilliseconds(70));
            var transforms = StateCardTransitions.GetTransforms(card);
            var opacityBeforeExit = card.Opacity;
            var scaleBeforeExit = transforms.Scale.ScaleX;
            var offsetBeforeExit = transforms.Translate.Y;
            var completions = 0;

            using var exit = StateCardTransitions.BeginExit(
                card,
                animationsEnabled: true,
                () => completions++);
            PumpFor(TimeSpan.FromMilliseconds(15));

            Assert.InRange(card.Opacity, 0, opacityBeforeExit);
            Assert.InRange(
                transforms.Scale.ScaleX,
                Math.Min(0.98, scaleBeforeExit),
                Math.Max(0.98, scaleBeforeExit));
            Assert.InRange(transforms.Translate.Y, -8, offsetBeforeExit);
            PumpFor(TimeSpan.FromMilliseconds(220));

            Assert.True(exit.IsCompleted);
            Assert.Equal(1, completions);
            Assert.Equal(0, card.Opacity, 3);
            Assert.Equal(0.98, transforms.Scale.ScaleX, 3);
            Assert.Equal(0.98, transforms.Scale.ScaleY, 3);
            Assert.Equal(-8, transforms.Translate.Y, 3);
            window.Close();
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Disabled_entrance_and_exit_settle_synchronously_without_clocks()
    {
        var failure = RunOnSta(() =>
        {
            var card = new Border();

            StateCardTransitions.BeginEntrance(card, animationsEnabled: false);
            var transforms = StateCardTransitions.GetTransforms(card);

            Assert.Equal(1, card.Opacity);
            Assert.Equal(1, transforms.Scale.ScaleX);
            Assert.Equal(1, transforms.Scale.ScaleY);
            Assert.Equal(0, transforms.Translate.Y);
            Assert.False(card.HasAnimatedProperties);
            Assert.False(transforms.Scale.HasAnimatedProperties);
            Assert.False(transforms.Translate.HasAnimatedProperties);
            var completed = false;

            using var exit = StateCardTransitions.BeginExit(
                card,
                animationsEnabled: false,
                () => completed = true);

            Assert.True(completed);
            Assert.True(exit.IsCompleted);
            Assert.Equal(0, card.Opacity);
            Assert.Equal(0.98, transforms.Scale.ScaleX);
            Assert.Equal(0.98, transforms.Scale.ScaleY);
            Assert.Equal(-8, transforms.Translate.Y);
            Assert.False(card.HasAnimatedProperties);
            Assert.False(transforms.Scale.HasAnimatedProperties);
            Assert.False(transforms.Translate.HasAnimatedProperties);
        });

        Assert.Null(failure);
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
        Assert.False(thread.IsAlive, "the STA thread did not finish in time");
        return failure;
    }
}

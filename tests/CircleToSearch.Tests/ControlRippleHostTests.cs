using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CircleToSearch.Capture;
using CircleToSearch.Ui.Effects;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class ControlRippleHostTests
{
    [Fact]
    public void Attached_ripple_reloads_without_duplicates_and_cleans_up_its_previous_layer()
    {
        var failure = RunOnSta(() =>
        {
            var button = new Button { Width = 100, Height = 44 };
            ControlRippleHost.SetIsEnabled(button, true);
            var decorator = new AdornerDecorator { Child = button };
            var window = new Window { Width = 300, Height = 200, Content = decorator };
            try
            {
                window.Show();
                window.UpdateLayout();
                void Press()
                {
                    button.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                    {
                        RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent,
                        Source = button,
                    });
                }
                var layer = AdornerLayer.GetAdornerLayer(button)!;
                Press();
                if (CircleToSearch.Ui.UiAnimationPolicy.Enabled) Assert.Single(layer.GetAdorners(button)!);
                decorator.Child = null;
                PumpUntil(() => !button.IsLoaded);
                Assert.Null(layer.GetAdorners(button));
                decorator.Child = button;
                window.UpdateLayout();
                PumpUntil(() => button.IsLoaded);
                Press();
                Press();
                if (CircleToSearch.Ui.UiAnimationPolicy.Enabled) Assert.Single(layer.GetAdorners(button)!);
                ControlRippleHost.SetIsEnabled(button, false);
                Assert.Null(layer.GetAdorners(button));
                Press();
                Assert.Null(layer.GetAdorners(button));
                ControlRippleHost.SetIsEnabled(button, true);
                Press();
                if (CircleToSearch.Ui.UiAnimationPolicy.Enabled) Assert.Single(layer.GetAdorners(button)!);
            }
            finally { window.Close(); }
        });
        Assert.Null(failure);
    }

    [Fact]
    public void Real_pointer_down_creates_an_adorner_without_crashing_the_dispatcher()
    {
        var failure = RunOnSta(() =>
        {
            var button = new Button { Width = 100, Height = 44 };
            var window = new Window
            {
                Width = 300,
                Height = 200,
                Content = new AdornerDecorator { Child = button },
            };
            window.Show();
            window.UpdateLayout();
            using var ripple = ControlRippleHost.AttachForTest(button);

            button.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            {
                RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent,
                Source = button,
            });

            window.UpdateLayout();
            var layer = AdornerLayer.GetAdornerLayer(button)!;
            var adorner = Assert.Single(layer.GetAdorners(button)!);
            var rippleLayer = Assert.IsType<Canvas>(System.Windows.Media.VisualTreeHelper.GetChild(adorner, 0));
            var rippleEllipse = Assert.Single(rippleLayer.Children.OfType<System.Windows.Shapes.Ellipse>());
            Assert.IsType<System.Windows.Media.RadialGradientBrush>(rippleEllipse.Fill);
            Assert.True(rippleEllipse.Width > button.ActualWidth);
            button.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            {
                RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent,
                Source = button,
            });
            PumpUntil(() => layer.GetAdorners(button) is null);
            window.Close();
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Active_ripple_follows_render_transforms_of_the_control_and_its_scale_host()
    {
        var failure = RunOnSta(() =>
        {
            using var time = ManualAnimationClock.Install();
            var offset = new TranslateTransform();
            var button = new Button { Width = 100, Height = 44, RenderTransform = offset };
            OverlayVisualResources.ApplyButtonTemplate(
                button, 22, Colors.White, Colors.Black, animationsEnabled: false);
            var root = new AdornerDecorator { Child = button };
            var window = new Window { Width = 300, Height = 200, Content = root };
            try
            {
                window.Show();
                window.UpdateLayout();
                using var ripple = ControlRippleHost.AttachForTest(button);
                button.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                {
                    RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent,
                    Source = button,
                });
                window.UpdateLayout();
                var adorner = Assert.Single(AdornerLayer.GetAdornerLayer(button)!.GetAdorners(button)!);
                var scaleHost = Assert.IsAssignableFrom<UIElement>(button.Template.FindName("ScaleHost", button));
                var farCorner = new Point(button.ActualWidth, button.ActualHeight);
                void AssertAligned()
                {
                    Assert.Equal(
                        scaleHost.TransformToVisual(root).Transform(new Point()),
                        adorner.TransformToVisual(root).Transform(new Point()));
                    Assert.Equal(
                        scaleHost.TransformToVisual(root).Transform(farCorner),
                        adorner.TransformToVisual(root).Transform(farCorner));
                }

                offset.BeginAnimation(
                    TranslateTransform.XProperty,
                    new DoubleAnimation(30, 0, TimeSpan.FromMilliseconds(200)));
                time.Advance(96);
                Assert.InRange(offset.X, 1, 29);
                AssertAligned();

                var scale = new ScaleTransform();
                scaleHost.RenderTransform = scale;
                time.Advance(16);
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 0.9, TimeSpan.FromMilliseconds(80)));
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, 0.9, TimeSpan.FromMilliseconds(80)));
                time.Advance(96);
                AssertAligned();
            }
            finally { window.Close(); }
        });

        Assert.Null(failure);
    }

    private static void PumpUntil(Func<bool> condition)
    {
        if (condition()) return;
        var frame = new System.Windows.Threading.DispatcherFrame();
        var timeout = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2),
        };
        timeout.Tick += (_, _) =>
        {
            timeout.Stop();
            frame.Continue = false;
        };
        var poll = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(10),
        };
        poll.Tick += (_, _) =>
        {
            if (!condition()) return;
            poll.Stop();
            timeout.Stop();
            frame.Continue = false;
        };
        timeout.Start();
        poll.Start();
        System.Windows.Threading.Dispatcher.PushFrame(frame);
        poll.Stop();
        Assert.True(condition(), "the control ripple did not finish in time");
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

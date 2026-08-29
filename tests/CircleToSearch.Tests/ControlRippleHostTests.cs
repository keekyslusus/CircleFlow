using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using CircleToSearch.Ui.Effects;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class ControlRippleHostTests
{
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

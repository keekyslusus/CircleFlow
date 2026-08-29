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

            Assert.NotEmpty(AdornerLayer.GetAdornerLayer(button)!.GetAdorners(button)!);
            window.Close();
        });

        Assert.Null(failure);
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

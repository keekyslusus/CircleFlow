using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CircleToSearch.Capture;
using CircleToSearch.Ui;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class OverlayActivityPresenterTests
{
    [Fact]
    public void Replacing_an_exiting_presentation_keeps_the_replacement_visible()
    {
        Assert.Null(RunOnSta(() =>
        {
            var host = Host();
            using var presenter = new OverlayActivityPresenter(host, () => true);
            var firstContent = new Border { Width = 40, Height = 40 };
            using var first = presenter.ShowContent(firstContent, "First");
            var hiding = first.HideAsync();
            var secondContent = new Border { Width = 60, Height = 60 };
            using var second = presenter.ShowContent(secondContent, "Second");

            Assert.True(hiding.IsCompletedSuccessfully);
            Pump(TimeSpan.FromMilliseconds(220));

            Assert.Equal(Visibility.Visible, host.Visibility);
            Assert.Contains(secondContent, Descendants(host));
            Assert.DoesNotContain(firstContent, Descendants(host));
            Assert.Equal("Second", Assert.Single(Descendants(host).OfType<TextBlock>()).Text);
        }));
    }

    [Fact]
    public void Stale_and_repeated_handle_operations_do_not_change_the_current_presentation()
    {
        Assert.Null(RunOnSta(() =>
        {
            var host = Host();
            using var presenter = new OverlayActivityPresenter(host, () => false);
            var first = presenter.ShowContent(new Border(), "First");
            using var second = presenter.ShowContent(new Border(), "Second");

            first.Dispose();
            first.Dispose();
            Assert.True(first.HideAsync().IsCompletedSuccessfully);
            Assert.Equal(Visibility.Visible, host.Visibility);
            Assert.Equal("Second", Assert.Single(Descendants(host).OfType<TextBlock>()).Text);

            Assert.True(second.HideAsync().IsCompletedSuccessfully);
            Assert.True(second.HideAsync().IsCompletedSuccessfully);
            Assert.Equal(Visibility.Collapsed, host.Visibility);
            Assert.Empty(host.Children);
        }));
    }

    [Fact]
    public void Replacement_and_presenter_disposal_complete_pending_hide_tasks()
    {
        Assert.Null(RunOnSta(() =>
        {
            var host = Host();
            var presenter = new OverlayActivityPresenter(host, () => true);
            var first = presenter.ShowContent(new Border(), "First");
            var firstHide = first.HideAsync();
            var second = presenter.ShowContent(new Border(), "Second");
            var secondHide = second.HideAsync();

            Assert.True(firstHide.IsCompletedSuccessfully);
            Assert.False(secondHide.IsCompleted);
            presenter.Dispose();

            Assert.True(secondHide.IsCompletedSuccessfully);
            Assert.Equal(Visibility.Collapsed, host.Visibility);
            Assert.Empty(host.Children);
        }));
    }

    [Fact]
    public void Presenter_stops_its_spinner_but_does_not_stop_external_content()
    {
        Assert.Null(RunOnSta(() =>
        {
            var host = Host();
            using var presenter = new OverlayActivityPresenter(host, () => true);
            using (var loading = presenter.ShowLoading(
                "Loading",
                OverlayVisualResources.Frozen(PluginPalette.For(false).MusicOverlay.Primary)))
            {
                Assert.True(presenter.LoadingIndicator.IsRendering);
                loading.Dispose();
                Assert.False(presenter.LoadingIndicator.IsRendering);
                Assert.Empty(host.Children);
            }

            using var waveform = new AudioWaveformVisual();
            waveform.Start();
            Assert.True(waveform.IsRendering);
            var content = presenter.ShowContent(waveform, "Listening");
            content.Dispose();

            Assert.True(waveform.IsRendering);
            Assert.Empty(host.Children);
        }));
    }

    [Fact]
    public void Disabled_animations_show_a_static_spinner_and_hide_synchronously()
    {
        Assert.Null(RunOnSta(() =>
        {
            var host = Host();
            using var presenter = new OverlayActivityPresenter(host, () => false);
            using var loading = presenter.ShowLoading(
                "Loading",
                OverlayVisualResources.Frozen(PluginPalette.For(false).MusicOverlay.Primary));

            Assert.False(presenter.LoadingIndicator.IsRendering);
            Assert.True(presenter.LoadingIndicator.IsRequestedActive);
            Assert.True(loading.HideAsync().IsCompletedSuccessfully);
            Assert.False(presenter.LoadingIndicator.IsRequestedActive);
            Assert.False(presenter.LoadingIndicator.IsRendering);
            Assert.Equal(Visibility.Collapsed, host.Visibility);
            Assert.Empty(host.Children);
        }));
    }

    [Theory]
    [InlineData(320, 200)]
    [InlineData(640, 400)]
    public void Activity_panel_is_centered_and_long_labels_stay_inside_the_host(double width, double height)
    {
        Assert.Null(RunOnSta(() =>
        {
            var root = new Grid { Width = width, Height = height };
            var host = Host();
            host.Margin = new Thickness(16);
            root.Children.Add(host);
            using var presenter = new OverlayActivityPresenter(host, () => false);
            using var presentation = presenter.ShowContent(
                new Border { Width = 80, Height = 80 },
                new string('W', 120));

            root.Measure(new Size(width, height));
            root.Arrange(new Rect(0, 0, width, height));
            root.UpdateLayout();

            var panel = Assert.Single(host.Children.OfType<Grid>());
            var center = panel.TranslatePoint(
                new Point(panel.ActualWidth / 2, panel.ActualHeight / 2),
                root);
            var label = Assert.Single(Descendants(host).OfType<TextBlock>());
            var bounds = label.TransformToAncestor(root).TransformBounds(new Rect(label.RenderSize));

            Assert.Equal(width / 2, center.X, precision: 6);
            Assert.Equal(height / 2, center.Y, precision: 6);
            Assert.True(bounds.Left >= 16);
            Assert.True(bounds.Right <= width - 16);
            Assert.True(bounds.Top >= 16);
            Assert.True(bounds.Bottom <= height - 16);
            Assert.True(label.ActualHeight > label.FontSize);
        }));
    }

    private static Grid Host() => new()
    {
        Visibility = Visibility.Collapsed,
        IsHitTestVisible = false,
    };

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
        thread.Join(TimeSpan.FromSeconds(30));
        Assert.False(thread.IsAlive, "the STA thread did not finish in time");
        return failure;
    }

    private static void Pump(TimeSpan duration)
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

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}

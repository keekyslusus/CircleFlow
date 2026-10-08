using System.Windows;
using CircleToSearch.Capture;
using CircleToSearch.Capture.OverlayInteractions;
using Xunit;
using GdiRectangle = System.Drawing.Rectangle;

namespace CircleToSearch.Tests;

public sealed class SelectionTraceTests
{
    [Fact]
    public void Lasso_reports_the_distance_drawn_but_not_the_closing_point()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var visual = OverlayVisualFactory.CreateRoot(null, new Size(200, 200), 0, false, TestUiStrings.English);
                var traced = new List<double>();
                var lasso = new SelectionOverlayController(
                    visual.Selection, visual.Root, new GdiRectangle(0, 0, 200, 200), 1, 0, 12,
                    false, () => true, (_, _) => true, () => { }, _ => { }, () => { }, () => { },
                    subscribeInput: false, traced: traced.Add);

                Assert.True(lasso.Begin(new Point(10, 10)));
                lasso.Update(new Point(40, 10));
                lasso.Update(new Point(40, 50));
                lasso.Complete(new Point(10, 50));

                Assert.Equal([30d, 40d], traced);
                lasso.Dispose();
                visual.TranslationAction.LoadingIndicator.Dispose();
                visual.Music.LoadingIndicator.Dispose();
                visual.Music.Waveform.Dispose();
                visual.Bottom.LayoutTransitions.Dispose();
                visual.Effects.SceneRipples.Dispose();
            }
            catch (Exception exception) { failure = exception; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        Assert.Null(failure);
    }
}

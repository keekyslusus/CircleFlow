using System.Windows;
using CircleToSearch.Capture;
using CircleToSearch.Capture.OverlayInteractions;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class ActionTrayOverlayControllerTests
{
    [Fact]
    public void Repeated_entrance_attaches_control_ripples_once_and_dispose_releases_them()
    {
        RunSta(() =>
        {
            var visual = OverlayVisualFactory.CreateRoot(
                null,
                new Size(640, 400),
                32,
                lightTheme: false,
                TestUiStrings.English);
            var controller = new ActionTrayOverlayController(visual.Actions, visual.Bottom.Root);

            controller.ShowEntrance();
            var initialCount = controller.ControlRippleCount;
            controller.ShowEntrance();

            Assert.True(initialCount > 0);
            Assert.Equal(initialCount, controller.ControlRippleCount);
            controller.HideForSelection();
            Assert.False(visual.Actions.Tray.IsHitTestVisible);
            controller.Restore();
            Assert.True(visual.Actions.Tray.IsHitTestVisible);

            controller.Dispose();
            Assert.Equal(0, controller.ControlRippleCount);
            visual.TranslationAction.LoadingIndicator.Dispose();
            visual.Music.LoadingIndicator.Dispose();
            visual.Music.Waveform.Dispose();
            visual.Bottom.LayoutTransitions.Dispose();
            visual.Effects.SceneRipples.Dispose();
        });
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        Assert.Null(failure);
    }
}

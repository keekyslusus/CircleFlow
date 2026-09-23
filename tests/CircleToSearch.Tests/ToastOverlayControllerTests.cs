using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using CircleToSearch.Capture;
using CircleToSearch.Capture.OverlayInteractions;
using CircleToSearch.MusicRecognition;
using Xunit;

namespace CircleToSearch.Tests;

[Trait("Category", "Slow")]
public sealed class ToastOverlayControllerTests
{
    [Fact]
    public void Keyed_language_toast_replaces_only_its_own_message_and_renews_lifetime()
    {
        Assert.Null(RunOnSta(() =>
        {
            var visual = CreateVisual();
            var window = ShowVisual(visual);
            using var controller = new ToastOverlayController(visual.Bottom, false, () => true);
            controller.Show(new ToastNotification("Copied", ToastTone.Success, TimeSpan.FromSeconds(2)));
            controller.ShowOrUpdate("ocr-language", new ToastNotification("English", ToastTone.Neutral,
                TimeSpan.FromMilliseconds(30)));
            PumpFor(TimeSpan.FromMilliseconds(70));
            controller.ShowOrUpdate("ocr-language", new ToastNotification("Russian", ToastTone.Neutral,
                TimeSpan.FromSeconds(1)));
            Assert.Equal(2, controller.ActiveCount);
            Assert.Equal(["Copied", "Russian"], controller.ActiveVisuals.Select(v => v.Message.Text));
            PumpFor(TimeSpan.FromMilliseconds(220));
            Assert.Equal(2, controller.ActiveCount);
            visual.Effects.SceneRipples.Dispose();
            window.Content = null;
            window.Close();
        }));
    }

    [Fact]
    public void Multiple_toasts_keep_creation_order_spacing_and_independent_lifetimes()
    {
        Assert.Null(RunOnSta(() =>
        {
            var visual = CreateVisual();
            var window = ShowVisual(visual);
            using var controller = new ToastOverlayController(
                visual.Bottom,
                lightTheme: false,
                () => false);
            controller.Show(new ToastNotification(
                "First",
                ToastTone.Neutral,
                TimeSpan.FromMilliseconds(40)));
            controller.Show(new ToastNotification(
                "Second",
                ToastTone.Error,
                TimeSpan.FromMilliseconds(140)));

            var active = controller.ActiveVisuals;
            Assert.Equal(2, active.Count);
            Assert.Equal(
                [active[0].Slot, active[1].Slot, visual.Bottom.ResultSlot, visual.Bottom.ActionSlot],
                visual.Bottom.Stack.Children.Cast<UIElement>());
            Assert.Equal(new Thickness(0, 0, 0, 8), active[0].Slot.Margin);
            Assert.Equal(new Thickness(0, 0, 0, 16), active[1].Slot.Margin);
            Assert.IsType<TranslateTransform>(active[0].Slot.RenderTransform);
            Assert.NotSame(active[0].Slot.RenderTransform, active[0].Card.RenderTransform);

            PumpFor(TimeSpan.FromMilliseconds(80));
            Assert.Single(controller.ActiveVisuals);
            Assert.Equal("Second", controller.ActiveVisuals[0].Message.Text);
            Assert.Equal(new Thickness(0, 0, 0, 16), controller.ActiveVisuals[0].Slot.Margin);
            PumpFor(TimeSpan.FromMilliseconds(100));
            Assert.Equal(0, controller.ActiveCount);
            Assert.Equal(
                [visual.Bottom.ResultSlot, visual.Bottom.ActionSlot],
                visual.Bottom.Stack.Children.Cast<UIElement>());

            visual.Effects.SceneRipples.Dispose();
            window.Content = null;
            window.Close();
        }));
    }

    [Fact]
    public void Animated_expiry_keeps_slot_until_exit_finishes_and_dispose_cancels_callbacks()
    {
        Assert.Null(RunOnSta(() =>
        {
            var visual = CreateVisual();
            var window = ShowVisual(visual);
            var controller = new ToastOverlayController(visual.Bottom, false, () => true);
            controller.Show(new ToastNotification(
                "Expiring",
                ToastTone.Success,
                TimeSpan.FromMilliseconds(30)));
            var slot = Assert.Single(controller.ActiveVisuals).Slot;

            PumpFor(TimeSpan.FromMilliseconds(70));
            Assert.Contains(slot, visual.Bottom.Stack.Children.Cast<UIElement>());
            Assert.Equal(1, controller.ActiveCount);
            PumpFor(TimeSpan.FromMilliseconds(180));
            Assert.DoesNotContain(slot, visual.Bottom.Stack.Children.Cast<UIElement>());
            Assert.Equal(0, controller.ActiveCount);

            controller.Show(new ToastNotification("Pending", ToastTone.Error, TimeSpan.FromSeconds(1)));
            controller.SettleForClosing();
            controller.Show(new ToastNotification("Too late", ToastTone.Error));
            Assert.Equal(0, controller.ActiveCount);
            controller.Dispose();
            Assert.Equal(0, controller.ActiveCount);
            Assert.Equal(
                [visual.Bottom.ResultSlot, visual.Bottom.ActionSlot],
                visual.Bottom.Stack.Children.Cast<UIElement>());
            PumpFor(TimeSpan.FromMilliseconds(220));

            visual.Effects.SceneRipples.Dispose();
            window.Content = null;
            window.Close();
        }));
    }

    [Fact]
    public void Visible_toast_uses_flip_when_a_music_result_appears_below_it()
    {
        Assert.Null(RunOnSta(() =>
        {
            var visual = CreateVisual();
            var window = ShowVisual(visual);
            using var controller = new ToastOverlayController(visual.Bottom, false, () => true);
            controller.Show(new ToastNotification("Persistent", ToastTone.Error, TimeSpan.FromSeconds(5)));
            PumpFor(TimeSpan.FromMilliseconds(220));
            var toast = Assert.Single(controller.ActiveVisuals);
            var cardTransform = toast.Card.RenderTransform;
            var oldVisualY = toast.Slot.TransformToAncestor(visual.Root).Transform(new Point()).Y;

            visual.Bottom.LayoutTransitions.Apply(() => MusicOverlayVisualPresenter.PresentResult(
                visual.Music,
                MusicRecognitionOutcome.From(MusicRecognitionStatus.NoAudio),
                TestUiStrings.English,
                lightTheme: false,
                _ => { },
                (_, _) => { }),
                animationsEnabled: true);

            var offset = Assert.IsType<TranslateTransform>(toast.Slot.RenderTransform);
            Assert.Same(cardTransform, toast.Card.RenderTransform);
            Assert.Equal(oldVisualY, toast.Slot.TransformToAncestor(visual.Root).Transform(new Point()).Y, 1);
            Assert.True(offset.HasAnimatedProperties);
            PumpFor(TimeSpan.FromMilliseconds(240));
            Assert.True(toast.Slot.TransformToAncestor(visual.Root).Transform(new Point()).Y < oldVisualY);

            visual.Effects.SceneRipples.Dispose();
            window.Content = null;
            window.Close();
        }));
    }

    [Fact]
    public void Showing_toast_raises_live_region_changed_from_a_real_automation_peer()
    {
        Assert.Null(RunOnSta(() =>
        {
            var visual = CreateVisual();
            var window = ShowVisual(visual);
            using var controller = new ToastOverlayController(visual.Bottom, false, () => false);
            var root = AutomationElement.FromHandle(new WindowInteropHelper(window).Handle);
            var raised = false;
            AutomationEventHandler handler = (_, args) =>
            {
                if (args.EventId == AutomationElementIdentifiers.LiveRegionChangedEvent)
                    Volatile.Write(ref raised, true);
            };
            Automation.AddAutomationEventHandler(
                AutomationElementIdentifiers.LiveRegionChangedEvent,
                root,
                TreeScope.Subtree,
                handler);
            try
            {
                controller.Show(new ToastNotification("Announce me", ToastTone.Neutral));
                PumpFor(TimeSpan.FromMilliseconds(100));
                Assert.True(Volatile.Read(ref raised));
                var message = Assert.Single(controller.ActiveVisuals).Message;
                Assert.NotNull(System.Windows.Automation.Peers.UIElementAutomationPeer.FromElement(message));
            }
            finally
            {
                Automation.RemoveAutomationEventHandler(
                    AutomationElementIdentifiers.LiveRegionChangedEvent,
                    root,
                    handler);
            }

            visual.Effects.SceneRipples.Dispose();
            window.Content = null;
            window.Close();
        }));
    }

    private static OverlayVisual CreateVisual() => OverlayVisualFactory.CreateRoot(
        null,
        new Size(640, 400),
        32,
        lightTheme: false,
        TestUiStrings.English);

    private static Window ShowVisual(OverlayVisual visual)
    {
        var window = new Window { Width = 640, Height = 400, Content = visual.Root };
        window.Show();
        window.UpdateLayout();
        return window;
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

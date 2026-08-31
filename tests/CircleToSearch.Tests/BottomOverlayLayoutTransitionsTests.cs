using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CircleToSearch.Capture;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class BottomOverlayLayoutTransitionsTests
{
    [Fact]
    public void Appearance_applies_final_layout_and_moves_persistent_slot_from_its_old_position()
    {
        var failure = RunOnSta(() =>
        {
            using var fixture = new BottomStackFixture(resultVisible: false, resultHeight: 48);
            var oldToastY = fixture.VisualY(fixture.ToastSlot);
            var oldActionY = fixture.VisualY(fixture.ActionSlot);

            fixture.Transitions.Apply(
                () => fixture.ResultSlot.Visibility = Visibility.Visible,
                animationsEnabled: true);

            Assert.Equal(oldToastY - 48, fixture.LayoutY(fixture.ToastSlot), 3);
            Assert.Equal(oldToastY, fixture.VisualY(fixture.ToastSlot), 2);
            Assert.Equal(oldActionY, fixture.VisualY(fixture.ActionSlot), 3);
            Assert.InRange(fixture.Offset(fixture.ToastSlot).Y, 47, 49);
            Assert.Equal(0, fixture.Offset(fixture.ActionSlot).Y, 3);
            Assert.False(fixture.Offset(fixture.ActionSlot).HasAnimatedProperties);

            PumpFor(TimeSpan.FromMilliseconds(260));

            Assert.Equal(oldToastY - 48, fixture.VisualY(fixture.ToastSlot), 2);
            AssertSettled(fixture.Offset(fixture.ToastSlot));
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Disappearance_moves_persistent_slot_down_and_skips_collapsed_slot()
    {
        var failure = RunOnSta(() =>
        {
            using var fixture = new BottomStackFixture(resultVisible: true, resultHeight: 48);
            var oldToastY = fixture.VisualY(fixture.ToastSlot);

            fixture.Transitions.Apply(
                () => fixture.ResultSlot.Visibility = Visibility.Collapsed,
                animationsEnabled: true);

            Assert.Equal(oldToastY + 48, fixture.LayoutY(fixture.ToastSlot), 3);
            Assert.Equal(oldToastY, fixture.VisualY(fixture.ToastSlot), 2);
            AssertSettled(fixture.Offset(fixture.ResultSlot));

            PumpFor(TimeSpan.FromMilliseconds(260));

            Assert.Equal(oldToastY + 48, fixture.VisualY(fixture.ToastSlot), 2);
            AssertSettled(fixture.Offset(fixture.ToastSlot));
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Newly_visible_removed_and_zero_height_slots_do_not_receive_flip_clocks()
    {
        var failure = RunOnSta(() =>
        {
            using var fixture = new BottomStackFixture(resultVisible: false, resultHeight: 48);
            var zeroSlot = fixture.AddSlot(0, index: 1);
            var removedSlot = fixture.AddSlot(24, index: 2);
            fixture.Window.UpdateLayout();

            fixture.Transitions.Apply(() =>
            {
                fixture.ResultSlot.Visibility = Visibility.Visible;
                fixture.Stack.Children.Remove(removedSlot);
            }, animationsEnabled: true);

            AssertSettled(fixture.Offset(fixture.ResultSlot));
            AssertSettled(fixture.Offset(zeroSlot));
            AssertSettled(fixture.Offset(removedSlot));

            fixture.Transitions.Apply(
                () => fixture.ResultSlot.Visibility = Visibility.Collapsed,
                animationsEnabled: true);

            AssertSettled(fixture.Offset(fixture.ResultSlot));
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Height_change_uses_measured_delta_instead_of_a_fixed_result_height()
    {
        var failure = RunOnSta(() =>
        {
            using var fixture = new BottomStackFixture(resultVisible: true, resultHeight: 48);
            var oldToastY = fixture.VisualY(fixture.ToastSlot);

            fixture.Transitions.Apply(
                () => fixture.ResultContent.Height = 93,
                animationsEnabled: true);

            Assert.Equal(oldToastY - 45, fixture.LayoutY(fixture.ToastSlot), 3);
            Assert.Equal(oldToastY, fixture.VisualY(fixture.ToastSlot), 2);
            Assert.InRange(fixture.Offset(fixture.ToastSlot).Y, 44, 46);
            Assert.InRange(fixture.Offset(fixture.ResultSlot).Y, 44, 46);
            Assert.Equal(0, fixture.Offset(fixture.ActionSlot).Y, 3);
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Interruption_rebases_from_the_current_rendered_position_without_a_jump()
    {
        var failure = RunOnSta(() =>
        {
            using var fixture = new BottomStackFixture(resultVisible: false, resultHeight: 72);
            fixture.Transitions.Apply(
                () => fixture.ResultSlot.Visibility = Visibility.Visible,
                animationsEnabled: true);
            PumpFor(TimeSpan.FromMilliseconds(70));
            var beforeInterruption = fixture.VisualY(fixture.ToastSlot);

            fixture.Transitions.Apply(
                () => fixture.ResultSlot.Visibility = Visibility.Collapsed,
                animationsEnabled: true);

            Assert.InRange(
                Math.Abs(fixture.VisualY(fixture.ToastSlot) - beforeInterruption),
                0,
                1.5);
            PumpFor(TimeSpan.FromMilliseconds(260));

            AssertSettled(fixture.Offset(fixture.ToastSlot));
            Assert.Equal(fixture.LayoutY(fixture.ToastSlot), fixture.VisualY(fixture.ToastSlot), 3);
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Disabled_motion_cancels_active_clocks_and_settles_synchronously()
    {
        var failure = RunOnSta(() =>
        {
            using var fixture = new BottomStackFixture(resultVisible: false, resultHeight: 72);
            fixture.Transitions.Apply(
                () => fixture.ResultSlot.Visibility = Visibility.Visible,
                animationsEnabled: true);
            PumpFor(TimeSpan.FromMilliseconds(50));
            Assert.True(fixture.Offset(fixture.ToastSlot).HasAnimatedProperties);

            fixture.Transitions.Apply(
                () => fixture.ResultSlot.Visibility = Visibility.Collapsed,
                animationsEnabled: false);

            AssertSettled(fixture.Offset(fixture.ToastSlot));
            AssertSettled(fixture.Offset(fixture.ResultSlot));
            AssertSettled(fixture.Offset(fixture.ActionSlot));
            Assert.Equal(fixture.LayoutY(fixture.ToastSlot), fixture.VisualY(fixture.ToastSlot), 3);
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Nested_mutation_joins_outer_capture_and_disposal_removes_pending_clocks()
    {
        var failure = RunOnSta(() =>
        {
            using var fixture = new BottomStackFixture(resultVisible: false, resultHeight: 64);
            var oldToastY = fixture.VisualY(fixture.ToastSlot);

            fixture.Transitions.Apply(
                () => fixture.Transitions.Apply(
                    () => fixture.ResultSlot.Visibility = Visibility.Visible,
                    animationsEnabled: true),
                animationsEnabled: true);

            Assert.Equal(oldToastY, fixture.VisualY(fixture.ToastSlot), 2);
            Assert.True(fixture.Offset(fixture.ToastSlot).HasAnimatedProperties);

            fixture.Transitions.Dispose();

            AssertSettled(fixture.Offset(fixture.ToastSlot));
            AssertSettled(fixture.Offset(fixture.ResultSlot));
            AssertSettled(fixture.Offset(fixture.ActionSlot));
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Disposal_detaches_pending_completion_and_releases_coordinator_and_window()
    {
        WeakReference? coordinatorReference = null;
        WeakReference? windowReference = null;
        var failure = RunOnSta(() =>
        {
            CreateAndDisposeAnimatingFixture(out coordinatorReference, out windowReference);
            PumpFor(TimeSpan.FromMilliseconds(260));
            ForceCollection();

            Assert.False(coordinatorReference.IsAlive);
            Assert.False(windowReference.IsAlive);
        });

        Assert.Null(failure);
    }

    private static void AssertSettled(TranslateTransform offset)
    {
        Assert.Equal(0, offset.Y, 3);
        Assert.False(offset.HasAnimatedProperties);
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

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CreateAndDisposeAnimatingFixture(
        out WeakReference coordinatorReference,
        out WeakReference windowReference)
    {
        var fixture = new BottomStackFixture(resultVisible: false, resultHeight: 64);
        fixture.Transitions.Apply(
            () => fixture.ResultSlot.Visibility = Visibility.Visible,
            animationsEnabled: true);
        Assert.True(fixture.Offset(fixture.ToastSlot).HasAnimatedProperties);
        coordinatorReference = new WeakReference(fixture.Transitions);
        windowReference = new WeakReference(fixture.Window);
        fixture.Dispose();
    }

    private static void ForceCollection()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private sealed class BottomStackFixture : IDisposable
    {
        internal BottomStackFixture(bool resultVisible, double resultHeight)
        {
            ToastSlot = CreateSlot(32, out _);
            ResultSlot = CreateSlot(resultHeight, out var resultContent);
            ResultContent = resultContent;
            ResultSlot.Visibility = resultVisible ? Visibility.Visible : Visibility.Collapsed;
            ActionSlot = CreateSlot(44, out _);
            Stack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 20),
            };
            Stack.Children.Add(ToastSlot);
            Stack.Children.Add(ResultSlot);
            Stack.Children.Add(ActionSlot);
            Root = new Grid();
            Root.Children.Add(Stack);
            Transitions = new BottomOverlayLayoutTransitions(Root, Stack);
            Window = new Window
            {
                Width = 400,
                Height = 320,
                WindowStyle = WindowStyle.None,
                ShowInTaskbar = false,
                Content = Root,
            };
            Window.Show();
            Window.UpdateLayout();
        }

        internal Window Window { get; }
        internal Grid Root { get; }
        internal StackPanel Stack { get; }
        internal Grid ToastSlot { get; }
        internal Grid ResultSlot { get; }
        internal Border ResultContent { get; }
        internal Grid ActionSlot { get; }
        internal BottomOverlayLayoutTransitions Transitions { get; }

        internal Grid AddSlot(double height, int index)
        {
            var slot = CreateSlot(height, out _);
            Stack.Children.Insert(index, slot);
            return slot;
        }

        internal TranslateTransform Offset(FrameworkElement slot) =>
            Assert.IsType<TranslateTransform>(slot.RenderTransform);

        internal double VisualY(FrameworkElement slot) =>
            slot.TranslatePoint(new Point(), Root).Y;

        internal double LayoutY(FrameworkElement slot) => VisualY(slot) - Offset(slot).Y;

        public void Dispose()
        {
            Transitions.Dispose();
            Window.Content = null;
            Window.Close();
        }

        private static Grid CreateSlot(double height, out Border content)
        {
            content = new Border { Width = 120, Height = height };
            var slot = new Grid();
            slot.Children.Add(content);
            return slot;
        }
    }
}

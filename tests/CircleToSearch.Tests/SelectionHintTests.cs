using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using CircleToSearch.Capture;
using CircleToSearch.Capture.OverlayInteractions;
using CircleToSearch.TextRecognition;
using Xunit;
using GdiRectangle = System.Drawing.Rectangle;
using GdiSize = System.Drawing.Size;

namespace CircleToSearch.Tests;

public sealed class SelectionHintTests
{
    [Fact]
    public void Rotation_starts_with_right_drag_and_cycles_through_every_idle_hint()
    {
        var rotation = new SelectionHintRotation();

        Assert.Equal(
            [
                SelectionHint.RightDragActions, SelectionHint.LeftDragSearch, SelectionHint.EscapeCancel,
                SelectionHint.RightDragActions,
            ],
            Enumerable.Range(0, 4).Select(_ => rotation.Next()));
    }

    [Fact]
    public void Text_hover_shows_the_alt_hint_after_a_delay_and_restores_the_idle_hint_after_leaving()
    {
        RunSta(() =>
        {
            var hint = HintVisual();
            var scheduled = new List<(TimeSpan Delay, Action Callback)>();
            var layoutChanges = 0;
            using var controller = new SelectionHintOverlayController(
                hint,
                TestUiStrings.English,
                SelectionHint.RightDragActions,
                change =>
                {
                    layoutChanges++;
                    change();
                },
                () => false,
                Dispatcher.CurrentDispatcher,
                (delay, callback) => scheduled.Add((delay, callback)));

            Assert.Equal(TestUiStrings.English.SelectionHintActions, hint.Action.Text);
            controller.SetTextHovered(true);
            Assert.Equal(SelectionHint.RightDragActions, controller.Shown);
            scheduled[^1].Callback();
            Assert.Equal(SelectionHint.AltLeftDragOverText, controller.Shown);
            Assert.Equal(TestUiStrings.English.SelectionHintSearchOverText, hint.Action.Text);
            Assert.Equal(2, hint.Keys.Children.Count);

            controller.SetTextHovered(false);
            Assert.Equal(SelectionHint.AltLeftDragOverText, controller.Shown);
            Assert.True(scheduled[^1].Delay > scheduled[0].Delay);
            scheduled[^1].Callback();

            Assert.Equal(SelectionHint.RightDragActions, controller.Shown);
            Assert.Single(hint.Keys.Children);
            Assert.Equal(2, layoutChanges);
        });
    }

    [Fact]
    public void Crossing_a_gap_between_words_keeps_the_alt_hint_without_relayout()
    {
        RunSta(() =>
        {
            var hint = HintVisual();
            var scheduled = new List<Action>();
            var layoutChanges = 0;
            using var controller = new SelectionHintOverlayController(
                hint,
                TestUiStrings.English,
                SelectionHint.EscapeCancel,
                change =>
                {
                    layoutChanges++;
                    change();
                },
                () => false,
                Dispatcher.CurrentDispatcher,
                (_, callback) => scheduled.Add(callback));
            controller.SetTextHovered(true);
            scheduled[^1]();

            controller.SetTextHovered(false);
            var pendingHide = scheduled[^1];
            controller.SetTextHovered(true);
            pendingHide();

            Assert.Equal(SelectionHint.AltLeftDragOverText, controller.Shown);
            Assert.Equal(1, layoutChanges);
        });
    }

    [Fact]
    public void Closing_cancels_a_pending_hint_change_and_ignores_later_hover()
    {
        RunSta(() =>
        {
            var hint = HintVisual();
            var scheduled = new List<Action>();
            var layoutChanges = 0;
            using var controller = new SelectionHintOverlayController(
                hint,
                TestUiStrings.English,
                SelectionHint.LeftDragSearch,
                change =>
                {
                    layoutChanges++;
                    change();
                },
                () => false,
                Dispatcher.CurrentDispatcher,
                (_, callback) => scheduled.Add(callback));
            controller.SetTextHovered(true);

            controller.SettleForClosing();
            scheduled[^1]();
            controller.SetTextHovered(false);
            controller.SetTextHovered(true);

            Assert.Single(scheduled);
            Assert.Equal(SelectionHint.LeftDragSearch, controller.Shown);
            Assert.Equal(0, layoutChanges);
        });
    }

    [Fact]
    public void Zoom_shows_the_middle_drag_hint_at_once_and_text_hover_still_takes_over()
    {
        RunSta(() =>
        {
            var hint = HintVisual();
            var scheduled = new List<Action>();
            using var controller = new SelectionHintOverlayController(
                hint,
                TestUiStrings.English,
                SelectionHint.EscapeCancel,
                change => change(),
                () => false,
                Dispatcher.CurrentDispatcher,
                (_, callback) => scheduled.Add(callback));

            controller.SetZoomed(true);
            Assert.Equal(SelectionHint.MiddleDragPan, controller.Shown);
            Assert.Equal(TestUiStrings.English.SelectionHintPan, hint.Action.Text);
            Assert.Equal(TestUiStrings.English.MiddleMouseButton,
                System.Windows.Automation.AutomationProperties.GetName(
                    Assert.IsType<Border>(Assert.Single(hint.Keys.Children))));

            controller.SetTextHovered(true);
            scheduled[^1]();
            Assert.Equal(SelectionHint.AltLeftDragOverText, controller.Shown);
            controller.SetTextHovered(false);
            scheduled[^1]();
            Assert.Equal(SelectionHint.MiddleDragPan, controller.Shown);

            controller.SetZoomed(false);
            Assert.Equal(SelectionHint.EscapeCancel, controller.Shown);
            controller.SettleForClosing();
            controller.SetZoomed(true);
            Assert.Equal(SelectionHint.EscapeCancel, controller.Shown);
        });
    }

    [Fact]
    public void Leaving_the_input_surface_over_text_ends_the_text_hover()
    {
        RunSta(() =>
        {
            var visual = OverlayVisualFactory.CreateRoot(
                null, new Size(100, 40), 0, false, TestUiStrings.English);
            var changes = new List<bool>();
            var lasso = new SelectionOverlayController(
                visual.Selection, visual.Root, new GdiRectangle(0, 0, 100, 40), 1, 0, 12,
                false, () => true, (_, _) => true, () => { }, _ => { }, () => { }, () => { },
                subscribeInput: false);
            var text = new TextSelectionOverlayController(
                visual.TextSelection, visual.Root, visual.Selection.InputSurface,
                new OverlayCoordinateMapper(1, false, new GdiSize(100, 40)),
                new OcrTextHitTester(),
                new ClipboardCopyService(_ => { }, _ => { }, TestUiStrings.English),
                () => "google-lens", _ => { }, TestUiStrings.English, false,
                changes.Add);
            var pointer = new PointerGestureRouter(
                visual.Selection, visual.Root, lasso, text,
                () => true, (_, _) => true, _ => new Point(15, 15));
            try
            {
                var word = new OcrWord(0, 0, 0, "word", new GdiRectangle(10, 10, 20, 10));
                text.SetDocument(new OcrDocument(
                    "en-US", new GdiSize(100, 40),
                    [new OcrLine(0, 0, new GdiRectangle(10, 10, 20, 10), [word])]));

                visual.Selection.InputSurface.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0)
                {
                    RoutedEvent = UIElement.MouseMoveEvent,
                });
                visual.Selection.InputSurface.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0)
                {
                    RoutedEvent = UIElement.MouseLeaveEvent,
                });

                Assert.Equal([true, false], changes);
                Assert.Empty(visual.TextSelection.HighlightLayer.Children);
            }
            finally
            {
                pointer.Dispose();
                text.Dispose();
                lasso.Dispose();
                visual.TranslationAction.LoadingIndicator.Dispose();
                visual.Music.LoadingIndicator.Dispose();
                visual.Music.Waveform.Dispose();
                visual.Bottom.LayoutTransitions.Dispose();
                visual.Effects.SceneRipples.Dispose();
            }
        });
    }

    [Fact]
    public void Text_controller_reports_hover_transitions_and_clears_them_when_the_document_goes_away()
    {
        RunSta(() =>
        {
            var visual = OverlayVisualFactory.CreateRoot(
                null, new Size(100, 40), 0, false, TestUiStrings.English);
            var changes = new List<bool>();
            var text = new TextSelectionOverlayController(
                visual.TextSelection, visual.Root, visual.Selection.InputSurface,
                new OverlayCoordinateMapper(1, false, new GdiSize(100, 40)),
                new OcrTextHitTester(),
                new ClipboardCopyService(_ => { }, _ => { }, TestUiStrings.English),
                () => "google-lens", _ => { }, TestUiStrings.English, false,
                changes.Add);
            try
            {
                var first = new OcrWord(0, 0, 0, "first", new GdiRectangle(10, 10, 20, 10));
                var second = new OcrWord(1, 0, 1, "second", new GdiRectangle(40, 10, 20, 10));
                text.SetDocument(new OcrDocument(
                    "en-US", new GdiSize(100, 40),
                    [new OcrLine(0, 0, new GdiRectangle(10, 10, 50, 10), [first, second])]));

                text.Hover(new Point(80, 30));
                text.Hover(new Point(15, 15));
                text.Hover(new Point(45, 15));
                text.SetDocument(null);

                Assert.Equal([true, false], changes);
            }
            finally
            {
                text.Dispose();
                visual.TranslationAction.LoadingIndicator.Dispose();
                visual.Music.LoadingIndicator.Dispose();
                visual.Music.Waveform.Dispose();
                visual.Bottom.LayoutTransitions.Dispose();
                visual.Effects.SceneRipples.Dispose();
            }
        });
    }

    private static SelectionHintVisual HintVisual()
    {
        var keys = new StackPanel();
        var action = new TextBlock();
        var content = new StackPanel();
        content.Children.Add(keys);
        content.Children.Add(action);
        return new SelectionHintVisual(
            content, keys, action,
            System.Windows.Media.Brushes.Transparent,
            System.Windows.Media.Brushes.Transparent,
            System.Windows.Media.Brushes.Transparent);
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

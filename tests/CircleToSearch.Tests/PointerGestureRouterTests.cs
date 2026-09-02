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

public sealed class PointerGestureRouterTests
{
    [Fact]
    public void Text_hit_routes_to_text_without_starting_legacy_lasso()
    {
        var failure = RunOnSta(() =>
        {
            var harness = new RouterHarness(new Point(15, 15));
            using (harness)
            {
                harness.Text.SetDocument(Document());
                RaiseGesture(harness.Input);

                Assert.True(harness.Text.IsActionMenuOpen);
                Assert.Equal(0, harness.LassoStarts);
                Assert.Equal(ActivePointerGesture.None, harness.Router.ActiveGesture);
            }
        });
        Assert.Null(failure);
    }

    [Fact]
    public void Ocr_completion_after_mouse_down_does_not_promote_lasso_to_text()
    {
        var failure = RunOnSta(() =>
        {
            var harness = new RouterHarness(new Point(15, 15));
            using (harness)
            {
                Raise(harness.Input, UIElement.MouseLeftButtonDownEvent);
                Assert.Equal(ActivePointerGesture.Lasso, harness.Router.ActiveGesture);
                harness.Text.SetDocument(Document());
                Raise(harness.Input, UIElement.MouseLeftButtonUpEvent);

                Assert.Equal(1, harness.LassoStarts);
                Assert.Equal(1, harness.PixelPicks);
                Assert.False(harness.Text.IsActionMenuOpen);
            }
        });
        Assert.Null(failure);
    }

    [Fact]
    public void Alt_on_text_forces_legacy_pipeline()
    {
        var failure = RunOnSta(() =>
        {
            var harness = new RouterHarness(new Point(15, 15), modifiers: () => ModifierKeys.Alt);
            using (harness)
            {
                harness.Text.SetDocument(Document());
                RaiseGesture(harness.Input);

                Assert.Equal(1, harness.LassoStarts);
                Assert.Equal(1, harness.PixelPicks);
                Assert.False(harness.Text.IsActionMenuOpen);
            }
        });
        Assert.Null(failure);
    }

    [Fact]
    public void Overlay_chrome_hit_does_not_start_a_gesture()
    {
        var failure = RunOnSta(() =>
        {
            var harness = new RouterHarness(new Point(15, 15), canStart: (_, _) => false);
            using (harness)
            {
                harness.Text.SetDocument(Document());
                Raise(harness.Input, UIElement.MouseLeftButtonDownEvent);

                Assert.Equal(ActivePointerGesture.None, harness.Router.ActiveGesture);
                Assert.Equal(0, harness.LassoStarts);
            }
        });
        Assert.Null(failure);
    }

    [Fact]
    public void Text_drag_uses_document_snapshot_when_current_document_is_replaced()
    {
        var failure = RunOnSta(() =>
        {
            var copied = new List<string>();
            var harness = new RouterHarness(new Point(15, 15), copied.Add);
            using (harness)
            {
                harness.Text.SetDocument(Document());
                Raise(harness.Input, UIElement.MouseLeftButtonDownEvent);
                harness.Text.SetDocument(new OcrDocument("en", new GdiSize(100, 40),
                    [new OcrLine(0, 0, new GdiRectangle(80, 20, 10, 10),
                        [new OcrWord(9, 0, 0, "replacement", new GdiRectangle(80, 20, 10, 10))]) ]));
                harness.Pointer = new Point(55, 15);
                Raise(harness.Input, UIElement.MouseMoveEvent);
                Raise(harness.Input, UIElement.MouseLeftButtonUpEvent);
                harness.Visual.TextSelection.CopyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                Assert.Equal(["one two"], copied);
                Assert.True(harness.Text.IsActionMenuOpen);
            }
        });
        Assert.Null(failure);
    }

    [Fact]
    public void Search_action_publishes_exactly_one_command()
    {
        var failure = RunOnSta(() =>
        {
            var harness = new RouterHarness(new Point(15, 15));
            using (harness)
            {
                harness.Text.SetDocument(Document());
                RaiseGesture(harness.Input);
                harness.Visual.TextSelection.SearchButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                harness.Visual.TextSelection.SearchButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                var command = Assert.IsType<SearchSelectedText>(Assert.Single(harness.Commands));
                Assert.Equal("one", command.Text);
            }
        });
        Assert.Null(failure);
    }

    [Fact]
    public void Clipboard_failure_keeps_selection_and_shows_localized_toast()
    {
        var failure = RunOnSta(() =>
        {
            var harness = new RouterHarness(new Point(15, 15), _ => throw new InvalidOperationException());
            using (harness)
            {
                harness.Text.SetDocument(Document());
                RaiseGesture(harness.Input);
                harness.Visual.TextSelection.CopyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                Assert.True(harness.Text.IsActionMenuOpen);
                var toast = Assert.Single(harness.Notifications);
                Assert.Equal(TestUiStrings.English.TextCopyFailed, toast.Message);
                Assert.Equal(ToastTone.Error, toast.Tone);
            }
        });
        Assert.Null(failure);
    }

    private static OcrDocument Document()
    {
        var one = new OcrWord(0, 0, 0, "one", new GdiRectangle(10, 10, 20, 10));
        var two = new OcrWord(1, 0, 1, "two", new GdiRectangle(45, 10, 20, 10));
        return new OcrDocument("en", new GdiSize(100, 40),
            [new OcrLine(0, 0, GdiRectangle.Union(one.BoundsPx, two.BoundsPx), [one, two])]);
    }

    private static void RaiseGesture(FrameworkElement input)
    {
        Raise(input, UIElement.MouseLeftButtonDownEvent);
        Raise(input, UIElement.MouseLeftButtonUpEvent);
    }

    private static void Raise(FrameworkElement input, RoutedEvent routedEvent) => input.RaiseEvent(
        routedEvent == UIElement.MouseMoveEvent
            ? new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = routedEvent, Source = input }
            : new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
            {
                RoutedEvent = routedEvent,
                Source = input,
            });

    private sealed class RouterHarness : IDisposable
    {
        private readonly Window _window;
        private readonly SelectionOverlayController _lasso;

        internal RouterHarness(
            Point pointer,
            Action<string>? clipboard = null,
            Func<ModifierKeys>? modifiers = null,
            Func<object?, Point, bool>? canStart = null)
        {
            Pointer = pointer;
            Visual = OverlayVisualFactory.CreateRoot(null, new Size(100, 40), 0, false, TestUiStrings.English);
            _window = new Window { Width = 100, Height = 40, Content = Visual.Root };
            _window.Show();
            _window.UpdateLayout();
            var mapper = new OverlayCoordinateMapper(1, false, new GdiSize(100, 40));
            _lasso = new SelectionOverlayController(
                Visual.Selection,
                Visual.Root,
                new GdiRectangle(0, 0, 100, 40),
                1,
                0,
                12,
                false,
                () => true,
                (_, _) => true,
                () => LassoStarts++,
                _ => PixelPicks++,
                _ => { },
                () => { },
                () => { },
                subscribeInput: false);
            Text = new TextSelectionOverlayController(
                Visual.TextSelection,
                Visual.Root,
                Visual.Selection.InputSurface,
                mapper,
                new OcrTextHitTester(),
                clipboard ?? (_ => { }),
                Notifications.Add,
                () => "google-lens",
                Commands.Add,
                TestUiStrings.English,
                false);
            Router = new PointerGestureRouter(
                Visual.Selection,
                Visual.Root,
                _lasso,
                Text,
                () => true,
                canStart ?? ((_, _) => true),
                _ => Pointer,
                modifiers);
        }

        internal OverlayVisual Visual { get; }
        internal FrameworkElement Input => Visual.Selection.InputSurface;
        internal TextSelectionOverlayController Text { get; }
        internal PointerGestureRouter Router { get; }
        internal int LassoStarts { get; private set; }
        internal int PixelPicks { get; private set; }
        internal Point Pointer { get; set; }
        internal List<IOverlayCommand> Commands { get; } = [];
        internal List<ToastNotification> Notifications { get; } = [];

        public void Dispose()
        {
            Router.Dispose();
            Text.Dispose();
            _lasso.Dispose();
            Visual.Music.Waveform.Dispose();
            Visual.TranslationAction.LoadingIndicator.Dispose();
            Visual.Effects.SceneRipples.Dispose();
            _window.Content = null;
            _window.Close();
        }
    }

    private static Exception? RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return failure;
    }
}

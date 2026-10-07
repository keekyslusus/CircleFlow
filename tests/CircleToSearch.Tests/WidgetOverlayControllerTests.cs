using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CircleToSearch.Capture;
using CircleToSearch.Capture.OverlayInteractions;
using CircleToSearch.Search;
using Xunit;
using GdiBitmap = System.Drawing.Bitmap;
using GdiRectangle = System.Drawing.Rectangle;

namespace CircleToSearch.Tests;

public sealed class WidgetOverlayControllerTests
{
    [Fact]
    public void Legacy_trace_selection_uses_the_terminal_visual_selection_path()
    {
        RunSta(() =>
        {
            using var source = new GdiBitmap(640, 400);
            var bounds = new GdiRectangle(0, 0, 640, 400);
            var start = new Point(100, 100);
            var finish = new Point(180, 160);
            var overlay = new OverlayWindow(
                source,
                bounds,
                bounds,
                1,
                new OverlayOptions(8, 12),
                TestUiStrings.English,
                TestOverlayControllers.CreateFactory(
                    pointerPosition: e => e.RoutedEvent == UIElement.MouseLeftButtonUpEvent ? finish : start),
                overscan: false,
                providers: [new(SearchProviderIds.TraceMoe, "trace.moe")],
                initialProviderId: SearchProviderIds.TraceMoe);
            overlay.Show();
            RaisePointerGesture(overlay.VisualState.Selection.InputSurface);

            Assert.Equal(OverlayInteractionMode.Closing, overlay.Mode);
            Assert.True(overlay.FrameTransferred);
            var outcome = Assert.IsType<OverlayOutcome>(overlay.Outcome);
            Assert.Equal(OverlayAction.VisualSelection, outcome.Action);
            Assert.Same(source, outcome.Selection!.FrozenFrame);

            overlay.CloseFromSession();
            Dispatcher.Run();
        });
    }

    [Fact]
    public void Mouse_back_closes_the_widget_card_while_escape_closes_the_overlay()
    {
        RunSta(() =>
        {
            using var source = new GdiBitmap(640, 400);
            var bounds = new GdiRectangle(0, 0, 640, 400);
            var start = new Point(100, 100);
            var finish = new Point(180, 160);
            var commands = new List<IOverlayCommand>();
            var overlay = new OverlayWindow(
                source,
                bounds,
                bounds,
                1,
                new OverlayOptions(8, 12),
                TestUiStrings.English,
                TestOverlayControllers.CreateFactory(
                    pointerPosition: e => e.RoutedEvent == UIElement.MouseLeftButtonUpEvent ? finish : start),
                overscan: false,
                providers: [new(SearchProviderIds.Pinterest, "Pinterest")],
                initialProviderId: SearchProviderIds.Pinterest,
                publishCommand: commands.Add);
            overlay.Show();
            RaisePointerGesture(overlay.VisualState.Selection.InputSurface);
            Assert.Equal(OverlayInteractionMode.WidgetLoading, overlay.Mode);
            commands.OfType<VisualSelection>().Single().Selection.Dispose();

            RaiseMouseBack(overlay);
            Assert.Equal(OverlayInteractionMode.WidgetLoading, overlay.Mode);
            overlay.ShowWidgetResult(VisualSearchPreparationOutcome.Ready(PreparedVisualSearch.ForPinterest([])));
            Assert.Equal(OverlayInteractionMode.WidgetResult, overlay.Mode);

            RaiseMouseBack(overlay);
            Assert.Equal(OverlayInteractionMode.Selecting, overlay.Mode);
            RaiseMouseBack(overlay);
            Assert.Equal(OverlayInteractionMode.Selecting, overlay.Mode);
            Assert.DoesNotContain(commands, command => command is CancelSession);

            RaisePointerGesture(overlay.VisualState.Selection.InputSurface);
            commands.OfType<VisualSelection>().Last().Selection.Dispose();
            overlay.ShowWidgetResult(VisualSearchPreparationOutcome.Ready(PreparedVisualSearch.ForPinterest([])));
            Assert.Equal(OverlayInteractionMode.WidgetResult, overlay.Mode);
            RaiseEscape(overlay);
            Assert.Equal(OverlayInteractionMode.Closing, overlay.Mode);
            Assert.IsType<CancelSession>(commands[^1]);

            overlay.CloseFromSession();
            Dispatcher.Run();
        });
    }

    [Fact]
    public void Escape_and_mouse_back_collapse_the_pinterest_masonry_before_leaving_the_card()
    {
        RunSta(() =>
        {
            using var source = new GdiBitmap(640, 400);
            var bounds = new GdiRectangle(0, 0, 640, 400);
            var start = new Point(100, 100);
            var finish = new Point(180, 160);
            var commands = new List<IOverlayCommand>();
            var overlay = new OverlayWindow(
                source,
                bounds,
                bounds,
                1,
                new OverlayOptions(8, 12),
                TestUiStrings.English,
                TestOverlayControllers.CreateFactory(
                    pointerPosition: e => e.RoutedEvent == UIElement.MouseLeftButtonUpEvent ? finish : start),
                overscan: false,
                providers: [new(SearchProviderIds.Pinterest, "Pinterest")],
                initialProviderId: SearchProviderIds.Pinterest,
                publishCommand: commands.Add);
            overlay.Show();
            RaisePointerGesture(overlay.VisualState.Selection.InputSurface);
            commands.OfType<VisualSelection>().Single().Selection.Dispose();
            overlay.ShowWidgetResult(VisualSearchPreparationOutcome.Ready(
                PreparedVisualSearch.ForPinterest(PinterestOverlayTests.Pins())));
            overlay.UpdateLayout();
            var back = Named(TestUiStrings.English.PinterestBack);

            Named(TestUiStrings.English.PinterestShowAll).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(Visibility.Visible, back.Visibility);
            RaiseEscape(overlay);
            Assert.Equal(Visibility.Collapsed, back.Visibility);
            Assert.Equal(OverlayInteractionMode.WidgetResult, overlay.Mode);

            overlay.UpdateLayout();
            Named(TestUiStrings.English.PinterestShowAll).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(Visibility.Visible, back.Visibility);
            RaiseMouseBack(overlay);
            Assert.Equal(Visibility.Collapsed, back.Visibility);
            Assert.Equal(OverlayInteractionMode.WidgetResult, overlay.Mode);
            RaiseMouseBack(overlay);
            Assert.Equal(OverlayInteractionMode.Selecting, overlay.Mode);
            Assert.DoesNotContain(commands, command => command is CancelSession);

            overlay.CloseFromSession();
            Dispatcher.Run();

            Button Named(string name) => Descendants(overlay.VisualState.Bottom.Stack).OfType<Button>()
                .Single(button => AutomationProperties.GetName(button) == name);
        });
    }

    [Fact]
    public void TryStart_uses_a_copy_and_keeps_the_source_owned_by_the_overlay()
    {
        RunSta(() =>
        {
            using var source = new GdiBitmap(64, 48);
            var visual = CreateVisual();
            var state = new OverlayInteractionState();
            VisualSelection? published = null;
            using var controller = CreateController(
                visual,
                state,
                command => published = Assert.IsType<VisualSelection>(command),
                bounds => new SelectionOutcome(bounds, (GdiBitmap)source.Clone()));

            Assert.True(controller.TryStart(SearchProviderIds.TraceMoe, new GdiRectangle(4, 5, 20, 18)));

            Assert.Equal(OverlayInteractionMode.WidgetLoading, state.Mode);
            Assert.NotNull(published);
            Assert.NotSame(source, published.Selection.FrozenFrame);
            published.Selection.Dispose();
            Assert.Equal(64, source.Width);
            controller.Dispose();
            DisposeVisual(visual);
        });
    }

    [Fact]
    public void Pinterest_selection_starts_its_widget_and_shows_the_result()
    {
        RunSta(() =>
        {
            using var source = new GdiBitmap(64, 48);
            var visual = CreateVisual();
            var state = new OverlayInteractionState();
            VisualSelection? published = null;
            using var controller = CreateController(
                visual,
                state,
                command => published = Assert.IsType<VisualSelection>(command),
                bounds => new SelectionOutcome(bounds, (GdiBitmap)source.Clone()));

            Assert.True(controller.TryStart(SearchProviderIds.Pinterest, new GdiRectangle(4, 5, 20, 18)));
            Assert.Equal(OverlayInteractionMode.WidgetLoading, state.Mode);
            Assert.Equal(SearchProviderIds.Pinterest, published!.ProviderId);
            published.Selection.Dispose();

            controller.ShowResult(VisualSearchPreparationOutcome.Ready(PreparedVisualSearch.ForPinterest([])));
            Assert.Equal(OverlayInteractionMode.WidgetResult, state.Mode);
            Assert.Contains(Descendants(visual.Bottom.Stack).OfType<TextBlock>(),
                text => text.Text == TestUiStrings.English.PinterestNoMatch);
            controller.Dispose();
            DisposeVisual(visual);
        });
    }

    [Fact]
    public void TryStart_rejects_unsupported_paths_without_creating_a_visual()
    {
        RunSta(() =>
        {
            using var source = new GdiBitmap(64, 48);
            var visual = CreateVisual();
            var state = new OverlayInteractionState();
            var initialChildren = visual.Root.Children.Count;
            using var withoutPublisher = CreateController(
                visual,
                state,
                null,
                bounds => new SelectionOutcome(bounds, (GdiBitmap)source.Clone()));

            Assert.False(withoutPublisher.TryStart(SearchProviderIds.TraceMoe, new GdiRectangle(1, 1, 10, 10)));
            Assert.False(withoutPublisher.TryStart(SearchProviderIds.GoogleLens, new GdiRectangle(1, 1, 10, 10)));
            Assert.Equal(OverlayInteractionMode.Selecting, state.Mode);
            Assert.Equal(initialChildren, visual.Root.Children.Count);
            withoutPublisher.Dispose();
            DisposeVisual(visual);
        });
    }

    [Fact]
    public void Publish_failure_disposes_the_created_selection_copy()
    {
        RunSta(() =>
        {
            using var source = new GdiBitmap(64, 48);
            var visual = CreateVisual();
            var state = new OverlayInteractionState();
            SelectionOutcome? created = null;
            using var controller = CreateController(
                visual,
                state,
                _ => throw new InvalidOperationException("publish failed"),
                bounds => created = new SelectionOutcome(bounds, (GdiBitmap)source.Clone()));

            var failure = Assert.Throws<InvalidOperationException>(() =>
                controller.TryStart(SearchProviderIds.TraceMoe, new GdiRectangle(1, 1, 10, 10)));

            Assert.Equal("publish failed", failure.Message);
            Assert.NotNull(created);
            Assert.Throws<ObjectDisposedException>(() => created.FrozenFrame);
            Assert.Equal(64, source.Width);
            controller.Dispose();
            DisposeVisual(visual);
        });
    }

    [Fact]
    public void Dispose_ignores_late_result_and_removes_loading_visual()
    {
        RunSta(() =>
        {
            using var source = new GdiBitmap(64, 48);
            var visual = CreateVisual();
            var state = new OverlayInteractionState();
            var controller = CreateController(
                visual,
                state,
                command => Assert.IsType<VisualSelection>(command).Selection.Dispose(),
                bounds => new SelectionOutcome(bounds, (GdiBitmap)source.Clone()));
            var initialChildren = visual.Root.Children.Count;
            Assert.True(controller.TryStart(SearchProviderIds.TraceMoe, new GdiRectangle(1, 1, 10, 10)));
            Assert.Equal(initialChildren, visual.Root.Children.Count);
            Assert.Equal(Visibility.Visible, visual.ActivityHost.Visibility);

            controller.Dispose();
            controller.ShowResult(VisualSearchPreparationOutcome.Ready(PreparedVisualSearch.ForTraceMoe(null)));

            Assert.Equal(initialChildren, visual.Root.Children.Count);
            Assert.Equal(Visibility.Collapsed, visual.ActivityHost.Visibility);
            Assert.Equal(OverlayInteractionMode.WidgetLoading, state.Mode);
            DisposeVisual(visual);
        });
    }

    [Fact]
    public void Replacing_a_card_while_video_is_pending_ignores_the_old_ready_callback()
    {
        RunSta(() =>
        {
            using var source = new GdiBitmap(640, 400);
            var visual = CreateVisual();
            var window = new Window
            {
                Content = visual.Root,
                Width = 640,
                Height = 400,
                ShowActivated = false,
                ShowInTaskbar = false,
            };
            var state = new OverlayInteractionState();
            var preview = new DeferredPreview();
            var published = new List<VisualSelection>();
            var controller = CreateController(
                visual,
                state,
                command =>
                {
                    var selection = Assert.IsType<VisualSelection>(command);
                    published.Add(selection);
                    selection.Selection.Dispose();
                },
                bounds => new SelectionOutcome(bounds, (GdiBitmap)source.Clone()),
                _ => preview);
            window.Show();
            window.UpdateLayout();
            try
            {
                var match = TraceMoeProvider.Parse(File.ReadAllText(Path.Combine(
                    TestOutputPaths.RepoDirectory,
                    "tests",
                    "CircleToSearch.Tests",
                    "Fixtures",
                    "trace-moe.json")))! with { Image = null };
                Assert.NotNull(match.Video);
                Assert.True(controller.TryStart(
                    SearchProviderIds.TraceMoe,
                    new GdiRectangle(10, 10, 120, 90)));
                controller.ShowResult(VisualSearchPreparationOutcome.Ready(
                    PreparedVisualSearch.ForTraceMoe(match)));
                Assert.Equal(OverlayInteractionMode.WidgetResult, state.Mode);

                var close = Descendants(visual.Bottom.Stack)
                    .OfType<Button>()
                    .Single(button => AutomationProperties.GetName(button) == TestUiStrings.English.Close);
                close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(OverlayInteractionMode.Selecting, state.Mode);

                Assert.True(controller.TryStart(
                    SearchProviderIds.TraceMoe,
                    new GdiRectangle(30, 30, 140, 100)));
                controller.ShowResult(VisualSearchPreparationOutcome.Ready(
                    PreparedVisualSearch.ForTraceMoe(null)));
                var replacement = Descendants(visual.Bottom.Stack)
                    .OfType<Border>()
                    .Single(card => AutomationProperties.GetName(card) == TestUiStrings.English.TraceMoeProviderName);

                preview.Complete(true);
                Pump(TimeSpan.FromMilliseconds(500));

                Assert.True(preview.Disposed);
                Assert.Equal(2, published.Count);
                Assert.Equal(OverlayInteractionMode.WidgetResult, state.Mode);
                Assert.Same(replacement, Descendants(visual.Bottom.Stack)
                    .OfType<Border>()
                    .Single(card => AutomationProperties.GetName(card) == TestUiStrings.English.TraceMoeProviderName));
                Assert.Contains(
                    Descendants(visual.Bottom.Stack).OfType<TextBlock>(),
                    text => text.Text == TestUiStrings.English.TraceNoMatch);
                Assert.DoesNotContain(
                    Descendants(visual.Bottom.Stack).OfType<TextBlock>(),
                    text => text.Text == match.Title);
            }
            finally
            {
                controller.Dispose();
                DisposeVisual(visual);
                window.Content = null;
                window.Close();
            }
        });
    }

    private static WidgetOverlayController CreateController(
        OverlayVisual visual,
        OverlayInteractionState state,
        Action<IOverlayCommand>? publish,
        Func<GdiRectangle, SelectionOutcome> createSelectionCopy,
        Func<Uri, ITraceVideoPreview>? createVideo = null) =>
        new(
            visual.Root,
            new OverlayActivityPresenter(visual.ActivityHost, OverlayVisualResources.AnimationsEnabled),
            visual.Bottom,
            visual.Effects,
            TestUiStrings.English,
            () => visual.LightTheme,
            new ClipboardCopyService(_ => { }, _ => { }, TestUiStrings.English),
            () => state.Mode,
            mode => state.TransitionTo(mode),
            publish,
            createSelectionCopy,
            CompositionRoot.CreateWidgetVisuals(createVideo));


    private static OverlayVisual CreateVisual() => OverlayVisualFactory.CreateRoot(
        null,
        new Size(640, 400),
        32,
        false,
        TestUiStrings.English,
        [new(SearchProviderIds.TraceMoe, "trace.moe")],
        SearchProviderIds.TraceMoe);

    private static void DisposeVisual(OverlayVisual visual)
    {
        visual.TranslationAction.LoadingIndicator.Dispose();
        visual.Music.LoadingIndicator.Dispose();
        visual.Music.Waveform.Dispose();
        visual.Bottom.LayoutTransitions.Dispose();
        visual.Effects.SceneRipples.Dispose();
    }

    private static void RaisePointerGesture(FrameworkElement input)
    {
        input.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
        {
            RoutedEvent = UIElement.MouseLeftButtonDownEvent,
            Source = input,
        });
        input.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
        {
            RoutedEvent = UIElement.MouseLeftButtonUpEvent,
            Source = input,
        });
    }

    private static void RaiseEscape(OverlayWindow overlay) =>
        overlay.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(overlay), 0, Key.Escape)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent,
        });

    private static void RaiseMouseBack(OverlayWindow overlay) =>
        overlay.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.XButton1)
        {
            RoutedEvent = UIElement.PreviewMouseUpEvent,
        });

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
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

    private sealed class DeferredPreview : ITraceVideoPreview
    {
        private readonly TaskCompletionSource<bool> _ready =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public FrameworkElement Root { get; } = new Grid();
        public Task<bool> Ready => _ready.Task;
        internal bool Disposed { get; private set; }

        internal void Complete(bool success) => _ready.TrySetResult(success);

        public void Dispose() => Disposed = true;
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

using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CircleToSearch.Capture;
using CircleToSearch.Capture.OverlayInteractions;
using CircleToSearch.TextRecognition;
using Xunit;
using GdiRectangle = System.Drawing.Rectangle;
using GdiSize = System.Drawing.Size;

namespace CircleToSearch.Tests;

public sealed class OverlayImageTextCoordinatorTests
{
    [Fact]
    public void Image_change_cancels_active_gesture_before_restarting_ocr()
    {
        var failure = RunOnSta(() =>
        {
            var source = Source(100, 40);
            var translated = Source(100, 40);
            var visual = OverlayVisualFactory.CreateRoot(
                source, new Size(100, 40), 0, false, TestUiStrings.English);
            var lasso = new SelectionOverlayController(
                visual.Selection,
                visual.Root,
                new GdiRectangle(0, 0, 100, 40),
                1,
                0,
                12,
                false,
                () => true,
                (_, _) => true,
                () => { },
                _ => { },
                () => { },
                () => { },
                subscribeInput: false);
            var text = new TextSelectionOverlayController(
                visual.TextSelection,
                visual.Root,
                visual.Selection.InputSurface,
                new OverlayCoordinateMapper(1, false, new GdiSize(100, 40)),
                new OcrTextHitTester(),
                new ClipboardCopyService(_ => { }, _ => { }, TestUiStrings.English),
                () => "google-lens",
                _ => { },
                TestUiStrings.English,
                false);
            var pointer = new PointerGestureRouter(
                visual.Selection,
                visual.Root,
                lasso,
                text,
                () => true,
                (_, _) => true,
                _ => new Point(20, 20));
            var recognizer = new GestureObservingRecognizer(() => pointer.ActiveGesture);
            var ocr = new OcrOverlayController(
                source,
                visual.Root.Dispatcher,
                recognizer,
                "en-US",
                _ => { });

            try
            {
                visual.Selection.InputSurface.RaiseEvent(new MouseButtonEventArgs(
                    Mouse.PrimaryDevice, 0, MouseButton.Left)
                {
                    RoutedEvent = UIElement.MouseLeftButtonDownEvent,
                    Source = visual.Selection.InputSurface,
                });
                Assert.Equal(ActivePointerGesture.Lasso, pointer.ActiveGesture);

                var coordinator = new OverlayImageTextCoordinator(
                    pointer,
                    text,
                    ocr,
                    null,
                    TestUiStrings.English,
                    "en-US");
                coordinator.OnImageChanged(translated, "ru-RU");

                Assert.Equal(ActivePointerGesture.None, pointer.ActiveGesture);
                Assert.Equal(ActivePointerGesture.None, recognizer.GestureAtStart);
                Assert.Same(translated, recognizer.Image);
                Assert.Equal("ru-RU", recognizer.Language);
            }
            finally
            {
                ocr.Dispose();
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

        Assert.Null(failure);
    }

    private static BitmapSource Source(int width, int height)
    {
        var stride = width * 4;
        var source = BitmapSource.Create(
            width, height, 96, 96, PixelFormats.Bgra32, null, new byte[stride * height], stride);
        source.Freeze();
        return source;
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

    private sealed class GestureObservingRecognizer(
        Func<ActivePointerGesture> activeGesture) : IOcrRecognizer
    {
        internal ActivePointerGesture? GestureAtStart { get; private set; }
        internal BitmapSource? Image { get; private set; }
        internal string? Language { get; private set; }

        public Task<OcrRecognitionOutcome> RecognizeAsync(
            BitmapSource source,
            string? requestedLanguageTag,
            CancellationToken cancellationToken)
        {
            GestureAtStart = activeGesture();
            Image = source;
            Language = requestedLanguageTag;
            var completion = new TaskCompletionSource<OcrRecognitionOutcome>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
            return completion.Task;
        }
    }
}

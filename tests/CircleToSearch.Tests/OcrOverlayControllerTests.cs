using System.Drawing;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CircleToSearch.Capture.OverlayInteractions;
using CircleToSearch.TextRecognition;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class OcrOverlayControllerTests
{
    [Fact]
    public void Frozen_bitmap_result_is_delivered_through_overlay_dispatcher_and_status_is_logged()
    {
        var failure = RunOnSta(() =>
        {
            var source = CreateFrozenSource();
            Assert.Null(source.Dispatcher);
            var dispatcher = Dispatcher.CurrentDispatcher;
            var document = Document();
            var recognizer = new AsyncRecognizer(OcrRecognitionOutcome.Success(document));
            var logDirectory = TestOutputPaths.NewTempDirectory(nameof(Frozen_bitmap_result_is_delivered_through_overlay_dispatcher_and_status_is_logged));
            var frame = new DispatcherFrame();
            var timedOut = false;
            OcrRecognitionOutcome? delivered = null;
            var deliveredOnUiThread = false;
            var timeout = StartTimeout(frame, () => timedOut = true);
            using var controller = new OcrOverlayController(
                source,
                dispatcher,
                recognizer,
                "en-US",
                outcome =>
                {
                    delivered = outcome;
                    deliveredOnUiThread = dispatcher.CheckAccess();
                    frame.Continue = false;
                },
                new PluginLog(logDirectory));

            controller.Start();
            Dispatcher.PushFrame(frame);
            timeout.Stop();

            Assert.False(timedOut, "OCR result was not delivered before the timeout.");
            Assert.True(recognizer.ReceivedFrozenSource);
            Assert.True(deliveredOnUiThread);
            Assert.Same(document, Assert.IsType<OcrRecognitionOutcome>(delivered).Document);
            Assert.Contains(
                "OCR completed with status Success.",
                File.ReadAllText(Path.Combine(logDirectory, "plugin.log")));
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Unexpected_recognizer_exception_is_delivered_as_failed_and_logged()
    {
        var failure = RunOnSta(() =>
        {
            var source = CreateFrozenSource();
            var dispatcher = Dispatcher.CurrentDispatcher;
            var logDirectory = TestOutputPaths.NewTempDirectory(nameof(Unexpected_recognizer_exception_is_delivered_as_failed_and_logged));
            var frame = new DispatcherFrame();
            var timedOut = false;
            OcrRecognitionOutcome? delivered = null;
            var timeout = StartTimeout(frame, () => timedOut = true);
            using var controller = new OcrOverlayController(
                source,
                dispatcher,
                new FailingRecognizer(),
                null,
                outcome =>
                {
                    delivered = outcome;
                    frame.Continue = false;
                },
                new PluginLog(logDirectory));

            controller.Start();
            Dispatcher.PushFrame(frame);
            timeout.Stop();

            Assert.False(timedOut, "Failed OCR outcome was not delivered before the timeout.");
            Assert.Equal(OcrRecognitionStatus.Failed, Assert.IsType<OcrRecognitionOutcome>(delivered).Status);
            var log = File.ReadAllText(Path.Combine(logDirectory, "plugin.log"));
            Assert.Contains("OCR recognition failed unexpectedly.", log);
            Assert.Contains(nameof(InvalidOperationException), log);
            Assert.Contains("OCR completed with status Failed.", log);
        });

        Assert.Null(failure);
    }

    private static BitmapSource CreateFrozenSource()
    {
        var source = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[4], 4);
        source.Freeze();
        return source;
    }

    private static OcrDocument Document()
    {
        var bounds = new Rectangle(0, 0, 1, 1);
        var word = new OcrWord(0, 0, 0, "text", bounds);
        return new OcrDocument("en-US", new Size(1, 1), [new OcrLine(0, 0, bounds, [word])]);
    }

    private static DispatcherTimer StartTimeout(DispatcherFrame frame, Action timedOut)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            timedOut();
            frame.Continue = false;
        };
        timer.Start();
        return timer;
    }

    private static Exception? RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(10));
        Assert.False(thread.IsAlive, "the STA thread did not finish in time");
        return failure;
    }

    private sealed class AsyncRecognizer(OcrRecognitionOutcome outcome) : IOcrRecognizer
    {
        internal bool ReceivedFrozenSource { get; private set; }

        public Task<OcrRecognitionOutcome> RecognizeAsync(
            BitmapSource source,
            string? requestedLanguageTag,
            CancellationToken cancellationToken) => Task.Run(() =>
            {
                ReceivedFrozenSource = source.IsFrozen;
                return outcome;
            }, cancellationToken);
    }

    private sealed class FailingRecognizer : IOcrRecognizer
    {
        public async Task<OcrRecognitionOutcome> RecognizeAsync(
            BitmapSource source,
            string? requestedLanguageTag,
            CancellationToken cancellationToken)
        {
            await Task.Yield();
            throw new InvalidOperationException("Synthetic OCR failure.");
        }
    }
}

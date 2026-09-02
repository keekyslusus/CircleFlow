using System.Windows.Threading;
using CircleToSearch.Interop;

namespace CircleToSearch.Capture;

public sealed class OverlaySessionFactory : IOverlaySessionFactory
{
    private readonly PluginLog _log;
    private readonly IPointerMonitorCapture _capture;
    private readonly IOverlayWindowFactory _windowFactory;
    private readonly CircleToSearch.Ocr.IOcrService? _ocrService;

    public OverlaySessionFactory(
        PluginLog log,
        IPointerMonitorCapture capture,
        IOverlayWindowFactory windowFactory,
        CircleToSearch.Ocr.IOcrService? ocrService = null)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        _windowFactory = windowFactory ?? throw new ArgumentNullException(nameof(windowFactory));
        _ocrService = ocrService;
    }

    public Task<IOverlaySession?> OpenAsync(OverlayLaunchOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        var ready = new TaskCompletionSource<IOverlaySession?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => Run(options, cancellationToken, ready))
        {
            IsBackground = true,
            Name = "CircleToSearch overlay session",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return ready.Task;
    }

    private void Run(
        OverlayLaunchOptions options,
        CancellationToken cancellationToken,
        TaskCompletionSource<IOverlaySession?> ready)
    {
        var previousContext = NativeMethods.SetThreadDpiAwarenessContext(NativeMethods.DpiAwarenessPerMonitorV2);
        OverlaySession? session = null;
        Exception? failure = null;
        try
        {
            var capture = _capture.Capture();
            if (capture is null)
            {
                _log.Warn(nameof(OverlaySessionFactory), "capturing the pointer monitor failed; selection canceled");
                ready.TrySetResult(null);
                return;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                capture.Frame.Dispose();
                ready.TrySetResult(null);
                return;
            }

            Task<CircleToSearch.Ocr.OcrScreenSnapshot>? ocrTask = null;
            if (_ocrService is not null && _ocrService.IsAvailable)
            {
                var ocrFrame = (System.Drawing.Bitmap)capture.Frame.Clone();
                ocrTask = Task.Run(async () =>
                {
                    using (ocrFrame)
                    {
                        return await _ocrService.RecognizeAsync(ocrFrame, capture.Scale, cancellationToken).ConfigureAwait(false);
                    }
                }, cancellationToken);
            }

            session = new OverlaySession(_log);
            OverlayWindow? window = null;
            try
            {
                window = _windowFactory.Create(
                    capture.Frame,
                    capture.Monitor,
                    capture.WorkArea,
                    capture.Scale,
                    options,
                    session.Publish,
                    capture.Pointer);
                if (ocrTask is not null) window.SetPendingOcrTask(ocrTask);
                session.Attach(window);
                window.Show();
                using var registration = cancellationToken.Register(
                    () => session.Publish(new CancelSession()));
                ready.TrySetResult(session);
                RunDispatcherLoop(session);
            }
            finally
            {
                if (window?.FrameTransferred != true) capture.Frame.Dispose();
            }
        }
        catch (Exception exception)
        {
            failure = exception;
            _log.Error(nameof(OverlaySessionFactory), "overlay session thread failed", exception);
            ready.TrySetException(exception);
        }
        finally
        {
            session?.Complete(failure);
            if (session is not null)
                _log.Info(nameof(OverlaySessionFactory), "overlay session thread stopped");
            NativeMethods.SetThreadDpiAwarenessContext(previousContext);
        }
    }

    internal static void RunDispatcherLoop(OverlaySession session)
    {
        Exception? failure = null;
        try
        {
            Dispatcher.Run();
        }
        catch (Exception exception)
        {
            failure = exception;
            throw;
        }
        finally
        {
            session.Complete(failure);
        }
    }
}

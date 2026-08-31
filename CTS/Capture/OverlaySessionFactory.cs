using System.Windows.Threading;
using CircleToSearch.Interop;

namespace CircleToSearch.Capture;

public sealed class OverlaySessionFactory : IOverlaySessionFactory
{
    private readonly PluginLog _log;
    private readonly IPointerMonitorCapture _capture;
    private readonly IOverlayWindowFactory _windowFactory;

    public OverlaySessionFactory(
        PluginLog log,
        IPointerMonitorCapture capture,
        IOverlayWindowFactory windowFactory)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        _windowFactory = windowFactory ?? throw new ArgumentNullException(nameof(windowFactory));
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

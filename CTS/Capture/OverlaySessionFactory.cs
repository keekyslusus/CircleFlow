using System.Runtime.InteropServices;
using System.Windows.Threading;
using CircleToSearch.Interop;

namespace CircleToSearch.Capture;

public sealed class OverlaySessionFactory : IOverlaySessionFactory
{
    private readonly PluginLog _log;

    public OverlaySessionFactory(PluginLog log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
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
        try
        {
            if (!OverlayWindow.TryCapturePointerMonitor(
                    out var monitor, out var workArea, out var frame, out var scale, out var pointer))
            {
                _log.Warn(nameof(OverlaySessionFactory), "capturing the pointer monitor failed; selection canceled");
                ready.TrySetResult(null);
                return;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                frame.Dispose();
                ready.TrySetResult(null);
                return;
            }

            var session = new OverlaySession();
            var transferred = false;
            try
            {
                var window = new OverlayWindow(
                    frame, monitor, workArea, scale, options, session.Publish, entranceOrigin: pointer);
                session.Attach(window);
                window.Show();
                using var registration = cancellationToken.Register(
                    () => session.Publish(new CancelSession()));
                ready.TrySetResult(session);
                Dispatcher.Run();
                transferred = window.FrameTransferred;
            }
            finally
            {
                if (!transferred) frame.Dispose();
            }
        }
        catch (Exception exception)
        {
            ready.TrySetException(exception);
        }
        finally
        {
            NativeMethods.SetThreadDpiAwarenessContext(previousContext);
        }
    }
}

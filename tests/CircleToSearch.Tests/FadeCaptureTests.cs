using System.Runtime.InteropServices;
using System.Windows.Threading;
using CircleToSearch.Capture;
using CircleToSearch.Interop;
using Xunit;
using GdiBitmap = System.Drawing.Bitmap;
using GdiColor = System.Drawing.Color;
using GdiRectangle = System.Drawing.Rectangle;
using GdiSize = System.Drawing.Size;

namespace CircleToSearch.Tests;

// Shows the real overlay on the live screen three times, cancels it with a different
// exit strategy each time, and continuously photographs the screen so a human can see
// what each strategy actually renders (black flash vs cross-fade).
// CTS_FADE_CAPTURE=1 dotnet test --filter FadeCaptureTests
// Frames: %TEMP%\cts-fade\NN.png, ~25 fps; phases are ~1.1s apart in capture order.
public sealed class FadeCaptureTests
{
    [Fact]
    public void Captures_exit_fade_frames_for_each_strategy()
    {
        if (Environment.GetEnvironmentVariable("CTS_FADE_CAPTURE") != "1") return;

        var dir = Path.Combine(Path.GetTempPath(), "cts-fade");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Directory.CreateDirectory(dir);

        using var stop = new CancellationTokenSource();
        var capturer = new Thread(() => CaptureLoop(dir, stop.Token)) { IsBackground = true };
        capturer.Start();

        var monitor = GetPointerMonitor(out var workArea, out var scale);
        using var template = new GdiBitmap(monitor.Width, monitor.Height);
        DrawTestFrame(template);

        RunPhase(monitor, workArea, scale, template, "a-window-opacity",
            allowsTransparency: false, exitFade: OverlayExitFade.Window, clickThrough: true);
        RunPhase(monitor, workArea, scale, template, "b-transparent-root",
            allowsTransparency: true, exitFade: OverlayExitFade.Root, clickThrough: true);
        RunPhase(monitor, workArea, scale, template, "c-dim-layers",
            allowsTransparency: false, exitFade: OverlayExitFade.DimLayers, clickThrough: true);
        RunPhase(monitor, workArea, scale, template, "d-window-opacity-no-clickthrough",
            allowsTransparency: false, exitFade: OverlayExitFade.Window, clickThrough: false);

        stop.Cancel();
        capturer.Join(TimeSpan.FromSeconds(5));
    }

    private static void RunPhase(
        GdiRectangle monitor,
        GdiRectangle workArea,
        double scale,
        GdiBitmap template,
        string name,
        bool allowsTransparency,
        OverlayExitFade exitFade,
        bool clickThrough)
    {
        Thread.Sleep(400);

        var failure = RunOnSta(() =>
        {
            var overlay = new OverlayWindow(
                (GdiBitmap)template.Clone(),
                monitor,
                workArea,
                scale,
                new OverlayOptions(8, 12),
                allowsTransparency,
                exitFade,
                clickThrough);
            overlay.Show();

            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                overlay.CancelFromCoordinator();
            };
            timer.Start();
            Dispatcher.Run();
        });
        Assert.Null(failure);
    }

    private static void DrawTestFrame(GdiBitmap frame)
    {
        using var graphics = System.Drawing.Graphics.FromImage(frame);
        graphics.Clear(GdiColor.FromArgb(255, 232, 119, 34));
        using var white = new System.Drawing.SolidBrush(GdiColor.FromArgb(210, 255, 255, 255));
        for (var i = 0; i < 8; i++)
            graphics.FillEllipse(white, i * 160 - 40, (i % 2) * 220 + 60, 220, 220);
    }

    private static void CaptureLoop(string dir, CancellationToken stop)
    {
        var previous = NativeMethods.SetThreadDpiAwarenessContext(NativeMethods.DpiAwarenessPerMonitorV2);
        try
        {
            var index = 0;
            while (!stop.IsCancellationRequested)
            {
                var bounds = GetPointerMonitor(out _, out _);
                using var bitmap = new GdiBitmap(bounds.Width, bounds.Height);
                using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
                    graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, new GdiSize(bounds.Width, bounds.Height));
                bitmap.Save(Path.Combine(dir, $"{index:00}.png"), System.Drawing.Imaging.ImageFormat.Png);
                index++;
                Thread.Sleep(40);
            }
        }
        finally
        {
            NativeMethods.SetThreadDpiAwarenessContext(previous);
        }
    }

    private static GdiRectangle GetPointerMonitor(out GdiRectangle workArea, out double scale)
    {
        if (!NativeMethods.GetCursorPos(out var pointer))
            throw new InvalidOperationException("GetCursorPos failed");
        var handle = NativeMethods.MonitorFromPoint(pointer, 2);
        var info = new MONITORINFO { CbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!NativeMethods.GetMonitorInfoW(handle, ref info))
            throw new InvalidOperationException("GetMonitorInfoW failed");
        if (NativeMethods.GetDpiForMonitor(handle, 0, out var dpiX, out _) != 0 || dpiX == 0) dpiX = 96;
        scale = dpiX / 96.0;

        var bounds = info.Monitor;
        var work = info.Work;
        workArea = new GdiRectangle(work.Left, work.Top, work.Right - work.Left, work.Bottom - work.Top);
        return new GdiRectangle(bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
    }

    private static Exception? RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        })
        {
            IsBackground = true,
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(30));
        Assert.True(!thread.IsAlive, "the STA thread did not finish in time");
        return failure;
    }
}

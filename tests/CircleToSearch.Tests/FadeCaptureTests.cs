using System.Drawing.Imaging;
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

// Opens/closes the real overlay twice (plain window vs 1 DIP overscan) while photographing
// a desktop crop that contains window shadows as fast as GDI allows. log.csv records the
// capture index and tick of every frame plus the show/cancel ticks of both phases, so the
// shadow blink around the open/close transitions can be compared between the two variants.
// CTS_FADE_CAPTURE=1 dotnet test --filter FadeCaptureTests   (the screen will flash twice)
// Frames: tests/temp/cts-fade/, ~60+ fps; phases are ~1.1s apart in capture order.
public sealed class FadeCaptureTests
{
    [Fact]
    public void Captures_open_close_frames_for_overscan_comparison()
    {
        if (Environment.GetEnvironmentVariable("CTS_FADE_CAPTURE") != "1") return;

        var dir = TestOutputPaths.NewTempDirectory("cts-fade");

        var monitor = GetPointerMonitor(out var workArea, out var scale);
        using var template = new GdiBitmap(monitor.Width, monitor.Height);
        DrawTestFrame(template);

        var log = new List<string>();
        var frames = new List<GdiBitmap>();
        var frameTicks = new List<long>();
        using var stop = new CancellationTokenSource();
        var capturer = new Thread(() => CaptureLoop(monitor, stop.Token, frames, frameTicks, log))
        {
            IsBackground = true,
        };
        capturer.Start();
        Thread.Sleep(200);

        RunPhase("a-plain", monitor, workArea, scale, template, oversize: false, log);
        Thread.Sleep(400);
        RunPhase("b-oversize", monitor, workArea, scale, template, oversize: true, log);

        stop.Cancel();
        capturer.Join(TimeSpan.FromSeconds(5));
        for (var i = 0; i < frames.Count; i++)
        {
            var path = Path.Combine(dir, $"{i:000}.jpg");
            SaveJpeg(frames[i], path);
            frames[i].Dispose();
        }
        var lines = new List<string>(log);
        lines.AddRange(frameTicks.Select(tick => $"frame,capture,{tick}"));
        File.WriteAllLines(Path.Combine(dir, "log.csv"), lines);
    }

    private static void RunPhase(
        string name,
        GdiRectangle monitor,
        GdiRectangle workArea,
        double scale,
        GdiBitmap template,
        bool oversize,
        List<string> log)
    {
        var failure = RunOnSta(() =>
        {
            var overlay = new OverlayWindow(
                (GdiBitmap)template.Clone(),
                monitor,
                workArea,
                scale,
                new OverlayOptions(8, 12),
                allowsTransparency: true,
                exitFade: OverlayExitFade.Root,
                clickThroughOnCancel: true,
                overscan: oversize);
            overlay.Show();
            lock (log) log.Add($"{name},show,{Environment.TickCount}");

            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                lock (log) log.Add($"{name},cancel,{Environment.TickCount}");
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

    private static void CaptureLoop(
        GdiRectangle monitor,
        CancellationToken stop,
        List<GdiBitmap> frames,
        List<long> frameTicks,
        List<string> log)
    {
        var previous = NativeMethods.SetThreadDpiAwarenessContext(NativeMethods.DpiAwarenessPerMonitorV2);
        try
        {
            var crop = new GdiRectangle(monitor.Left + 40, monitor.Top + 30, 900, 560);
            while (!stop.IsCancellationRequested)
            {
                var bitmap = new GdiBitmap(crop.Width, crop.Height);
                using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
                    graphics.CopyFromScreen(crop.Left, crop.Top, 0, 0, new GdiSize(crop.Width, crop.Height));
                lock (frames)
                {
                    frames.Add(bitmap);
                    frameTicks.Add(Environment.TickCount);
                }
            }
        }
        finally
        {
            NativeMethods.SetThreadDpiAwarenessContext(previous);
        }
    }

    private static void SaveJpeg(GdiBitmap bitmap, string path)
    {
        var codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(Encoder.Quality, 70L);
        bitmap.Save(path, codec, parameters);
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

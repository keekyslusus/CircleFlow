using System.Runtime.InteropServices;
using CircleToSearch.Interop;
using GdiBitmap = System.Drawing.Bitmap;
using GdiGraphics = System.Drawing.Graphics;
using GdiPoint = System.Drawing.Point;
using GdiRectangle = System.Drawing.Rectangle;
using GdiSize = System.Drawing.Size;

namespace CircleToSearch.Capture;

public interface IPointerMonitorCapture
{
    PointerMonitorCaptureResult? Capture();
}

public sealed record PointerMonitorCaptureResult(
    GdiRectangle Monitor,
    GdiRectangle WorkArea,
    double Scale,
    GdiPoint Pointer,
    GdiBitmap Frame);

public sealed class PointerMonitorCapture : IPointerMonitorCapture
{
    private const uint MonitorDefaultToNearest = 2;
    private const int MonitorEffectiveDpi = 0;

    private readonly Func<PointerMonitorMetadata?> _readMetadata;
    private readonly Func<int, int, GdiBitmap> _createFrame;
    private readonly Action<GdiGraphics, GdiRectangle> _copyFrame;

    public PointerMonitorCapture()
        : this(ReadMetadata, static (width, height) => new GdiBitmap(
                width,
                height,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb),
            static (graphics, bounds) => graphics.CopyFromScreen(
                bounds.Left,
                bounds.Top,
                0,
                0,
                new GdiSize(bounds.Width, bounds.Height)))
    {
    }

    internal PointerMonitorCapture(
        Func<PointerMonitorMetadata?> readMetadata,
        Func<int, int, GdiBitmap> createFrame,
        Action<GdiGraphics, GdiRectangle> copyFrame)
    {
        _readMetadata = readMetadata;
        _createFrame = createFrame;
        _copyFrame = copyFrame;
    }

    public PointerMonitorCaptureResult? Capture()
    {
        var metadata = _readMetadata();
        if (metadata is null) return null;

        var monitor = metadata.Monitor;
        if (monitor.Width <= 0 || monitor.Height <= 0) return null;

        var frame = _createFrame(monitor.Width, monitor.Height);
        try
        {
            using var graphics = GdiGraphics.FromImage(frame);
            _copyFrame(graphics, monitor);
            // The caller owns the frame until ownership is transferred in a SelectionOutcome.
            return new PointerMonitorCaptureResult(
                monitor,
                metadata.WorkArea,
                metadata.Scale,
                metadata.Pointer,
                frame);
        }
        catch
        {
            frame.Dispose();
            throw;
        }
    }

    private static PointerMonitorMetadata? ReadMetadata()
    {
        if (!NativeMethods.GetCursorPos(out var nativePointer)) return null;
        var handle = NativeMethods.MonitorFromPoint(nativePointer, MonitorDefaultToNearest);
        if (handle == IntPtr.Zero) return null;

        var info = new MONITORINFO { CbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!NativeMethods.GetMonitorInfoW(handle, ref info)) return null;

        var bounds = info.Monitor;
        var monitor = GdiRectangle.FromLTRB(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
        var work = info.Work;
        var workArea = GdiRectangle.FromLTRB(work.Left, work.Top, work.Right, work.Bottom);
        if (monitor.Width <= 0 || monitor.Height <= 0) return null;

        if (NativeMethods.GetDpiForMonitor(handle, MonitorEffectiveDpi, out var dpiX, out _) != 0 || dpiX == 0)
            dpiX = 96;
        return new PointerMonitorMetadata(
            monitor,
            workArea,
            dpiX / 96.0,
            new GdiPoint(nativePointer.X, nativePointer.Y));
    }
}

internal sealed record PointerMonitorMetadata(
    GdiRectangle Monitor,
    GdiRectangle WorkArea,
    double Scale,
    GdiPoint Pointer);

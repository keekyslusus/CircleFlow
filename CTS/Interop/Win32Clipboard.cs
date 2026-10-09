using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CircleToSearch.Interop;

// WPF's Clipboard opens the clipboard twice (OleSetClipboard + OleFlushClipboard) and retries each for
// up to a second on the UI thread. With clipboard history on, rapid copies collide with the history
// service reading the previous one, so the overlay stalls and the copy fails. One short open avoids that.
internal static class Win32Clipboard
{
    internal const uint CfUnicodeText = 13;
    internal const uint CfDib = 8;
    private const uint GmemMoveable = 0x0002;
    private const int OpenAttempts = 20;
    private const int OpenRetryDelayMs = 10;
    internal static readonly uint PngFormat = RegisterClipboardFormatW("PNG");

    internal static void SetText(string text) => Write(TextFormats(text));

    internal static void SetImage(BitmapSource image) => Write(ImageFormats(image));

    internal static (uint Format, byte[] Data)[] TextFormats(string text)
    {
        var bytes = new byte[(text.Length + 1) * sizeof(char)];
        System.Text.Encoding.Unicode.GetBytes(text, 0, text.Length, bytes, 0);
        return [(CfUnicodeText, bytes)];
    }

    internal static (uint Format, byte[] Data)[] ImageFormats(BitmapSource image)
    {
        var bgra = image.Format == PixelFormats.Bgra32 ? image : new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var stride = bgra.PixelWidth * 4;
        var pixels = new byte[stride * bgra.PixelHeight];
        bgra.CopyPixels(pixels, stride, 0);
        var dib = (CfDib, Dib(pixels, bgra.PixelWidth, bgra.PixelHeight));
        // Most apps ignore alpha in CF_DIB, so transparency needs PNG; encoding it for every opaque
        // screenshot would stall the UI thread on each copy.
        return HasTransparency(pixels) && PngFormat != 0 ? [dib, (PngFormat, Png(bgra))] : [dib];
    }

    private static bool HasTransparency(byte[] bgraPixels)
    {
        for (var alpha = 3; alpha < bgraPixels.Length; alpha += 4)
            if (bgraPixels[alpha] != byte.MaxValue) return true;
        return false;
    }

    private static void Write((uint Format, byte[] Data)[] entries)
    {
        Open();
        try
        {
            if (!EmptyClipboard()) throw new Win32Exception();
            foreach (var (format, data) in entries)
            {
                var handle = ToGlobal(data);
                if (SetClipboardData(format, handle) == IntPtr.Zero)
                {
                    GlobalFree(handle);
                    throw new Win32Exception();
                }
            }
        }
        finally
        {
            CloseClipboard();
        }
    }

    private static void Open()
    {
        for (var attempt = 1; !OpenClipboard(IntPtr.Zero); attempt++)
        {
            if (attempt >= OpenAttempts) throw new Win32Exception();
            Thread.Sleep(OpenRetryDelayMs);
        }
    }

    private static IntPtr ToGlobal(byte[] data)
    {
        var handle = GlobalAlloc(GmemMoveable, (UIntPtr)data.Length);
        if (handle == IntPtr.Zero) throw new Win32Exception();
        var pointer = GlobalLock(handle);
        if (pointer == IntPtr.Zero)
        {
            GlobalFree(handle);
            throw new Win32Exception();
        }
        Marshal.Copy(data, 0, pointer, data.Length);
        GlobalUnlock(handle);
        return handle;
    }

    private static byte[] Dib(byte[] pixels, int width, int height)
    {
        const int headerSize = 40;
        var stride = width * 4;
        var dib = new byte[headerSize + pixels.Length];
        using (var writer = new BinaryWriter(new MemoryStream(dib)))
        {
            writer.Write(headerSize);
            writer.Write(width);
            writer.Write(height);
            writer.Write((short)1);
            writer.Write((short)32);
            writer.Write(0);
            writer.Write(pixels.Length);
        }
        // Positive height means bottom-up rows, which every CF_DIB consumer understands.
        for (var row = 0; row < height; row++)
            Buffer.BlockCopy(pixels, row * stride, dib, headerSize + (height - 1 - row) * stride, stride);
        return dib;
    }

    private static byte[] Png(BitmapSource image)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(IntPtr owner);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint format, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterClipboardFormatW(string name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr memory);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr memory);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalFree(IntPtr memory);
}

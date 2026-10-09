using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CircleToSearch.Interop;
using Xunit;

namespace CircleToSearch.Tests;

// Checks the payloads rather than the system clipboard, so running tests never touches the user's
// clipboard or Win+V history.
public sealed class Win32ClipboardTests
{
    private static readonly byte[] OpaquePixels = [10, 20, 30, 255, 40, 50, 60, 255, 70, 80, 90, 255, 100, 110, 120, 255];

    [Fact]
    public void Text_is_null_terminated_utf16()
    {
        const string text = "Привет, мир 😀\r\nsecond line";

        var (format, data) = Assert.Single(Win32Clipboard.TextFormats(text));

        Assert.Equal(Win32Clipboard.CfUnicodeText, format);
        Assert.Equal(text + "\0", Encoding.Unicode.GetString(data));
    }

    [Fact]
    public void Opaque_image_is_a_dib_only_with_rows_in_order()
    {
        var (format, dib) = Assert.Single(Win32Clipboard.ImageFormats(Bitmap(OpaquePixels)));

        Assert.Equal(Win32Clipboard.CfDib, format);
        Assert.Equal(OpaquePixels, PixelsOf(DecodeDib(dib)));
    }

    [Fact]
    public void Image_with_transparency_also_carries_an_exact_png()
    {
        byte[] pixels = [10, 20, 30, 255, 40, 50, 60, 0, 70, 80, 90, 255, 100, 110, 120, 128];

        var formats = Win32Clipboard.ImageFormats(Bitmap(pixels));

        Assert.Equal([Win32Clipboard.CfDib, Win32Clipboard.PngFormat], formats.Select(entry => entry.Format));
        var png = BitmapDecoder.Create(new MemoryStream(formats[1].Data), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        Assert.Equal(pixels, PixelsOf(png.Frames[0]));
    }

    private static BitmapSource Bitmap(byte[] pixels) =>
        BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, pixels, 8);

    private static BitmapSource DecodeDib(byte[] dib)
    {
        const int fileHeaderSize = 14;
        const int infoHeaderSize = 40;
        using var bmp = new MemoryStream();
        using (var writer = new BinaryWriter(bmp, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write((byte)'B');
            writer.Write((byte)'M');
            writer.Write(fileHeaderSize + dib.Length);
            writer.Write(0);
            writer.Write(fileHeaderSize + infoHeaderSize);
            writer.Write(dib);
        }
        bmp.Position = 0;
        return BitmapDecoder.Create(bmp, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
    }

    private static byte[] PixelsOf(BitmapSource image)
    {
        var pixels = new byte[image.PixelWidth * image.PixelHeight * 4];
        new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0).CopyPixels(pixels, image.PixelWidth * 4, 0);
        return pixels;
    }
}

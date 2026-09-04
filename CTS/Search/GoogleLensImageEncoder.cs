using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace CircleToSearch.Search;

internal static class GoogleLensImageEncoder
{
    internal const long JpegQuality = 95L;

    public static byte[] EncodeJpeg(byte[] png)
    {
        ArgumentNullException.ThrowIfNull(png);

        using var input = new MemoryStream(png, writable: false);
        using var source = Image.FromStream(input, useEmbeddedColorManagement: false, validateImageData: true);
        using var jpeg = new Bitmap(source.Width, source.Height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(jpeg))
        {
            graphics.Clear(Color.White);
            graphics.DrawImage(source, 0, 0, source.Width, source.Height);
        }

        var codec = ImageCodecInfo.GetImageEncoders()
            .Single(encoder => encoder.FormatID == ImageFormat.Jpeg.Guid);
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(Encoder.Quality, JpegQuality);
        using var output = new MemoryStream();
        jpeg.Save(output, codec, parameters);
        return output.ToArray();
    }
}

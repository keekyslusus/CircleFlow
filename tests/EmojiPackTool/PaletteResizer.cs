using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CircleToSearch.EmojiPackTool;

// WPF only writes 32-bit PNGs, several times larger than Noto's quantized originals,
// so the scaled image is mapped back onto the source palette and written as an indexed PNG.
internal static class PaletteResizer
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(index =>
    {
        var crc = (uint)index;
        for (var bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1;
        return crc;
    }).ToArray();

    public static byte[] Resize(byte[] png, int height)
    {
        var source = new BitmapImage();
        source.BeginInit();
        source.StreamSource = new MemoryStream(png);
        source.DecodePixelHeight = height;
        source.CacheOption = BitmapCacheOption.OnLoad;
        source.EndInit();
        var bitmap = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var width = bitmap.PixelWidth;
        var pixels = new byte[width * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);
        var palette = ReadPalette(png);
        if (palette is not null)
        {
            var indices = Quantize(pixels, palette);
            // Scaling drops most fine shades, and each unused entry costs four bytes in every image.
            var used = indices.Distinct().Order().ToArray();
            var remap = new byte[256];
            for (var index = 0; index < used.Length; index++) remap[used[index]] = (byte)index;
            for (var pixel = 0; pixel < indices.Length; pixel++) indices[pixel] = remap[indices[pixel]];
            return WriteIndexed(width, bitmap.PixelHeight, indices, used.Select(index => palette[index]).ToArray());
        }
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var result = new MemoryStream();
        encoder.Save(result);
        return result.ToArray();
    }

    private static (byte R, byte G, byte B, byte A)[]? ReadPalette(byte[] png)
    {
        byte[]? colors = null;
        byte[] alpha = [];
        for (var offset = 8; offset < png.Length;)
        {
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset));
            var type = Encoding.ASCII.GetString(png, offset + 4, 4);
            var data = png.AsSpan(offset + 8, length).ToArray();
            if (type == "PLTE") colors = data;
            if (type == "tRNS") alpha = data;
            offset += 12 + length;
        }
        if (colors is null) return null;
        return Enumerable.Range(0, colors.Length / 3)
            .Select(index => (colors[index * 3], colors[index * 3 + 1], colors[index * 3 + 2],
                index < alpha.Length ? alpha[index] : (byte)255))
            .ToArray();
    }

    // Distances use premultiplied colors so every fully transparent pixel matches any transparent entry.
    private static byte[] Quantize(byte[] bgra, (byte R, byte G, byte B, byte A)[] palette)
    {
        var indices = new byte[bgra.Length / 4];
        var cache = new Dictionary<int, byte>();
        for (var pixel = 0; pixel < indices.Length; pixel++)
        {
            var (b, g, r, a) = (bgra[pixel * 4], bgra[pixel * 4 + 1], bgra[pixel * 4 + 2], bgra[pixel * 4 + 3]);
            var color = b | g << 8 | r << 16 | a << 24;
            if (!cache.TryGetValue(color, out var best))
            {
                var bestDistance = long.MaxValue;
                for (var index = 0; index < palette.Length; index++)
                {
                    var entry = palette[index];
                    long dr = r * a - entry.R * entry.A, dg = g * a - entry.G * entry.A,
                        db = b * a - entry.B * entry.A, da = (a - entry.A) * 255;
                    var distance = dr * dr + dg * dg + db * db + da * da;
                    if (distance >= bestDistance) continue;
                    bestDistance = distance;
                    best = (byte)index;
                }
                cache[color] = best;
            }
            indices[pixel] = best;
        }
        return indices;
    }

    private static byte[] WriteIndexed(int width, int height, byte[] indices, (byte R, byte G, byte B, byte A)[] palette)
    {
        using var output = new MemoryStream();
        output.Write(Signature);
        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), (uint)height);
        header[8] = 8;
        header[9] = 3;
        WriteChunk(output, "IHDR", header);
        WriteChunk(output, "PLTE", palette.SelectMany(entry => new[] { entry.R, entry.G, entry.B }).ToArray());
        var opaqueTail = palette.Reverse().TakeWhile(entry => entry.A == 255).Count();
        if (opaqueTail < palette.Length)
            WriteChunk(output, "tRNS", palette.Take(palette.Length - opaqueTail).Select(entry => entry.A).ToArray());
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            for (var row = 0; row < height; row++)
            {
                zlib.WriteByte(0);
                zlib.Write(indices, row * width, width);
            }
        }
        WriteChunk(output, "IDAT", compressed.ToArray());
        WriteChunk(output, "IEND", []);
        return output.ToArray();
    }

    private static void WriteChunk(Stream output, string type, byte[] data)
    {
        var buffer = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(buffer, (uint)data.Length);
        output.Write(buffer);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(data);
        var crc = 0xFFFFFFFFu;
        foreach (var value in typeBytes.Concat(data)) crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        BinaryPrimitives.WriteUInt32BigEndian(buffer, crc ^ 0xFFFFFFFFu);
        output.Write(buffer);
    }
}

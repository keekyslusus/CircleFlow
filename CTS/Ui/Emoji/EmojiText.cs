using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CircleToSearch.Ui.Emoji;

// WPF draws color fonts in one color and lacks newer emoji, so emoji in user text are shown as
// Noto Color Emoji images, the set Android uses.
internal sealed class EmojiText(string archivePath)
{
    private const double SizeEm = 1.15;

    private readonly Lock _gate = new();
    private readonly Dictionary<string, BitmapSource?> _images = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _aliases = new(StringComparer.Ordinal);
    private EmojiSequences? _sequences;

    internal void SetText(TextBlock block, string text)
    {
        var typeface = new Typeface(block.FontFamily, block.FontStyle, block.FontWeight, block.FontStretch);
        var glyphs = typeface.TryGetGlyphTypeface(out var font) ? font.CharacterToGlyphMap : null;
        var parts = Sequences().Split(text, codePoint => glyphs?.ContainsKey(codePoint) == true);
        Dictionary<string, BitmapSource?> images;
        lock (_gate) images = Images(parts.Where(part => part.Key is not null).Select(part => part.Key!));
        block.Inlines.Clear();
        // Inline images on lines cut off by MaxHeight are still drawn unless the block clips them.
        block.ClipToBounds = true;
        foreach (var part in parts)
        {
            if (part.Key is null || images[part.Key] is not { } image)
            {
                block.Inlines.Add(new Run(part.Text));
                continue;
            }
            var size = block.FontSize * SizeEm;
            var element = new Image
            {
                Source = image, Height = size, Width = size * image.PixelWidth / image.PixelHeight,
                Stretch = Stretch.Uniform,
            };
            RenderOptions.SetBitmapScalingMode(element, BitmapScalingMode.HighQuality);
            block.Inlines.Add(new InlineUIContainer(element) { BaselineAlignment = BaselineAlignment.Center });
        }
    }

    private EmojiSequences Sequences()
    {
        lock (_gate)
        {
            if (_sequences is not null) return _sequences;
            try
            {
                using var archive = ZipFile.OpenRead(archivePath);
                var keys = archive.Entries.Where(entry => entry.Name.EndsWith(".png", StringComparison.Ordinal))
                    .Select(entry => entry.Name[..^4]).ToList();
                foreach (var line in Lines(archive, "aliases.txt"))
                {
                    var pair = line.Split(' ');
                    _aliases[pair[0]] = pair[1];
                }
                var textDefault = Lines(archive, "text-default.txt").Select(line => Convert.ToInt32(line, 16));
                _sequences = new EmojiSequences(keys.Concat(_aliases.Keys), textDefault);
            }
            // Without the archive emoji keep the system font's rendering instead of breaking the text.
            catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException
                or InvalidDataException)
            {
                _sequences = EmojiSequences.Empty;
            }
            // A briefly locked archive, e.g. while an antivirus scans it, is read again for the next text.
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _aliases.Clear();
                return EmojiSequences.Empty;
            }
            return _sequences;
        }
    }

    // Reading the index takes tens of milliseconds, so it is done off the UI thread while a search runs.
    internal void Preload() => _ = Task.Run(Sequences);

    private Dictionary<string, BitmapSource?> Images(IEnumerable<string> keys)
    {
        var result = new Dictionary<string, BitmapSource?>(StringComparer.Ordinal);
        ZipArchive? archive = null;
        try
        {
            foreach (var key in keys)
            {
                if (result.ContainsKey(key)) continue;
                if (!_images.TryGetValue(key, out var image))
                {
                    archive ??= ZipFile.OpenRead(archivePath);
                    image = Decode(archive, _aliases.GetValueOrDefault(key, key));
                    _images[key] = image;
                }
                result[key] = image;
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException
            or UnauthorizedAccessException)
        {
            foreach (var key in keys) result.TryAdd(key, null);
        }
        finally
        {
            archive?.Dispose();
        }
        return result;
    }

    private static BitmapSource? Decode(ZipArchive archive, string key)
    {
        var entry = archive.GetEntry(key + ".png");
        if (entry is null) return null;
        using var data = new MemoryStream();
        using (var stream = entry.Open()) stream.CopyTo(data);
        data.Position = 0;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.StreamSource = data;
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception exception) when (exception is NotSupportedException or FileFormatException)
        {
            return null;
        }
    }

    private static List<string> Lines(ZipArchive archive, string name)
    {
        var entry = archive.GetEntry(name);
        if (entry is null) return [];
        using var reader = new StreamReader(entry.Open());
        var lines = new List<string>();
        while (reader.ReadLine() is { } line)
            if (line.Length > 0) lines.Add(line);
        return lines;
    }
}

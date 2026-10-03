using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace CircleToSearch.EmojiPackTool;

// Builds Emoji/NotoColorEmoji.zip from NotoColorEmoji.ttf so emoji look as they do on Android.
// The font's own cmap and GSUB ligatures define which sequences exist, including flags and aliases.
public static class Program
{
    private const int VariationSelector16 = 0xFE0F;

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length != 4 || !int.TryParse(args[3], out var height) || height < 0)
        {
            Console.Error.WriteLine("usage: <NotoColorEmoji.ttf> <emoji-data.txt> <output.zip> <height px, 0 keeps the original>");
            return 2;
        }
        var font = new Font(File.ReadAllBytes(args[0]));
        var (emoji, presentation) = ReadEmojiData(args[1]);
        var images = font.Bitmaps();
        var reverse = new Dictionary<int, int>();
        foreach (var (codePoint, glyph) in font.CharacterMap().OrderBy(pair => pair.Key))
            reverse.TryAdd(glyph, codePoint);

        var entries = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        var textDefault = new SortedSet<int>();
        foreach (var (codePoint, glyph) in font.CharacterMap())
        {
            // ASCII keycap bases are only emoji as part of a keycap sequence.
            if (codePoint < 0x80 || !emoji.Contains(codePoint) || !images.TryGetValue(glyph, out var png)) continue;
            entries[Key([codePoint])] = png;
            if (!presentation.Contains(codePoint)) textDefault.Add(codePoint);
        }
        var skipped = 0;
        foreach (var (components, glyph) in font.Ligatures())
        {
            if (!images.TryGetValue(glyph, out var png)) continue;
            var codePoints = new List<int>();
            foreach (var component in components)
            {
                if (!reverse.TryGetValue(component, out var codePoint)) { codePoints.Clear(); break; }
                codePoints.Add(codePoint);
            }
            if (codePoints.Count == 0) { skipped++; continue; }
            entries.TryAdd(Key(codePoints), png);
        }

        var aliases = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var stored = new Dictionary<string, string>(StringComparer.Ordinal);
        using (var output = ZipFile.Open(args[2], ZipArchiveMode.Create))
        {
            foreach (var (key, png) in entries)
            {
                var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(png));
                if (stored.TryGetValue(hash, out var target))
                {
                    aliases[key] = target;
                    continue;
                }
                stored[hash] = key;
                var entry = output.CreateEntry(key + ".png", CompressionLevel.SmallestSize);
                using var stream = entry.Open();
                stream.Write(height == 0 ? png : PaletteResizer.Resize(png, height));
            }
            WriteLines(output, "text-default.txt", textDefault.Select(codePoint => codePoint.ToString("x")));
            WriteLines(output, "aliases.txt", aliases.Select(pair => $"{pair.Key} {pair.Value}"));
        }
        Console.WriteLine($"{stored.Count} images, {aliases.Count} aliases, {textDefault.Count} text-default, " +
            $"{skipped} ligatures skipped, {new FileInfo(args[2]).Length / 1024} KB");
        return 0;
    }

    private static void WriteLines(ZipArchive archive, string name, IEnumerable<string> lines)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name, CompressionLevel.SmallestSize).Open(),
            new UTF8Encoding(false));
        foreach (var line in lines) writer.Write(line + "\n");
    }

    private static string Key(IEnumerable<int> codePoints) =>
        string.Join('_', codePoints.Where(codePoint => codePoint != VariationSelector16).Select(codePoint => codePoint.ToString("x")));

    private static (HashSet<int> Emoji, HashSet<int> Presentation) ReadEmojiData(string path)
    {
        var emoji = new HashSet<int>();
        var presentation = new HashSet<int>();
        foreach (var line in File.ReadLines(path))
        {
            var data = line.Split('#')[0];
            var fields = data.Split(';', StringSplitOptions.TrimEntries);
            if (fields.Length != 2) continue;
            var target = fields[1] switch
            {
                "Emoji" => emoji,
                "Emoji_Presentation" => presentation,
                _ => null,
            };
            if (target is null) continue;
            var range = fields[0].Split("..");
            var first = int.Parse(range[0], NumberStyles.HexNumber);
            var last = range.Length == 2 ? int.Parse(range[1], NumberStyles.HexNumber) : first;
            for (var codePoint = first; codePoint <= last; codePoint++) target.Add(codePoint);
        }
        return (emoji, presentation);
    }
}

internal sealed class Font
{
    private readonly byte[] _data;
    private readonly Dictionary<string, int> _tables = new(StringComparer.Ordinal);

    public Font(byte[] data)
    {
        _data = data;
        var count = U16(4);
        for (var index = 0; index < count; index++)
        {
            var record = 12 + index * 16;
            _tables[Encoding.ASCII.GetString(data, record, 4)] = (int)U32(record + 8);
        }
    }

    public Dictionary<int, int> CharacterMap()
    {
        var cmap = Table("cmap");
        var count = U16(cmap + 2);
        for (var index = 0; index < count; index++)
        {
            var subtable = cmap + (int)U32(cmap + 4 + index * 8 + 4);
            if (U16(subtable) != 12) continue;
            var map = new Dictionary<int, int>();
            var groups = (int)U32(subtable + 12);
            for (var group = 0; group < groups; group++)
            {
                var offset = subtable + 16 + group * 12;
                var first = (int)U32(offset);
                var last = (int)U32(offset + 4);
                var glyph = (int)U32(offset + 8);
                for (var codePoint = first; codePoint <= last; codePoint++) map[codePoint] = glyph + codePoint - first;
            }
            return map;
        }
        throw new InvalidDataException("The font has no format 12 cmap.");
    }

    public List<(int[] Components, int Glyph)> Ligatures()
    {
        var gsub = Table("GSUB");
        var lookups = gsub + U16(gsub + 8);
        var result = new List<(int[], int)>();
        for (var index = 0; index < U16(lookups); index++)
        {
            var lookup = lookups + U16(lookups + 2 + index * 2);
            var type = U16(lookup);
            for (var sub = 0; sub < U16(lookup + 4); sub++)
            {
                var subtable = lookup + U16(lookup + 6 + sub * 2);
                var subtype = type;
                if (type == 7)
                {
                    subtype = U16(subtable + 2);
                    subtable += (int)U32(subtable + 4);
                }
                if (subtype == 4) ReadLigatures(subtable, result);
            }
        }
        return result;
    }

    private void ReadLigatures(int subtable, List<(int[], int)> result)
    {
        var firsts = Coverage(subtable + U16(subtable + 2));
        for (var set = 0; set < U16(subtable + 4); set++)
        {
            var ligatureSet = subtable + U16(subtable + 6 + set * 2);
            for (var index = 0; index < U16(ligatureSet); index++)
            {
                var ligature = ligatureSet + U16(ligatureSet + 2 + index * 2);
                var count = U16(ligature + 2);
                var components = new int[count];
                components[0] = firsts[set];
                for (var component = 1; component < count; component++)
                    components[component] = U16(ligature + 4 + (component - 1) * 2);
                result.Add((components, U16(ligature)));
            }
        }
    }

    private List<int> Coverage(int coverage)
    {
        var glyphs = new List<int>();
        var count = U16(coverage + 2);
        if (U16(coverage) == 1)
        {
            for (var index = 0; index < count; index++) glyphs.Add(U16(coverage + 4 + index * 2));
            return glyphs;
        }
        for (var range = 0; range < count; range++)
        {
            var offset = coverage + 4 + range * 6;
            for (var glyph = U16(offset); glyph <= U16(offset + 2); glyph++) glyphs.Add(glyph);
        }
        return glyphs;
    }

    // Uses the largest strike; Noto ships a single 109 ppem strike of PNG images.
    public Dictionary<int, byte[]> Bitmaps()
    {
        var cblc = Table("CBLC");
        var cbdt = Table("CBDT");
        var strikes = (int)U32(cblc + 4);
        var strike = Enumerable.Range(0, strikes).Select(index => cblc + 8 + index * 48).MaxBy(offset => _data[offset + 44]);
        var array = cblc + (int)U32(strike);
        var images = new Dictionary<int, byte[]>();
        for (var index = 0; index < (int)U32(strike + 8); index++)
        {
            var entry = array + index * 8;
            var first = U16(entry);
            var last = U16(entry + 2);
            var header = array + (int)U32(entry + 4);
            var indexFormat = U16(header);
            var imageFormat = U16(header + 2);
            var dataOffset = cbdt + (int)U32(header + 4);
            for (var glyph = first; glyph <= last; glyph++)
            {
                var position = glyph - first;
                var (start, end) = indexFormat switch
                {
                    1 => ((int)U32(header + 8 + position * 4), (int)U32(header + 12 + position * 4)),
                    3 => (U16(header + 8 + position * 2), U16(header + 10 + position * 2)),
                    _ => throw new InvalidDataException($"Unsupported CBLC index format {indexFormat}."),
                };
                if (end <= start) continue;
                var image = dataOffset + start;
                var png = imageFormat switch
                {
                    17 => image + 5,
                    18 => image + 8,
                    19 => image,
                    _ => throw new InvalidDataException($"Unsupported CBDT image format {imageFormat}."),
                };
                var length = (int)U32(png);
                images[glyph] = _data.AsSpan(png + 4, length).ToArray();
            }
        }
        return images;
    }

    private int Table(string tag) =>
        _tables.TryGetValue(tag, out var offset) ? offset : throw new InvalidDataException($"The font has no {tag} table.");

    private int U16(int offset) => BinaryPrimitives.ReadUInt16BigEndian(_data.AsSpan(offset));

    private uint U32(int offset) => BinaryPrimitives.ReadUInt32BigEndian(_data.AsSpan(offset));
}

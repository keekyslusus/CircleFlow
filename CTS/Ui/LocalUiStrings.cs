using System.Collections.Frozen;
using System.Globalization;
using System.IO;
using System.Xml;
using System.Xml.Linq;

namespace CircleToSearch.Ui;

internal sealed class LocalUiStrings
{
    private readonly FrozenDictionary<string, string> _values;

    private LocalUiStrings(Dictionary<string, string> values, IReadOnlyList<string>? problems = null)
    {
        _values = values.ToFrozenDictionary(StringComparer.Ordinal);
        Problems = problems ?? [];
    }

    // Translation problems that were skipped in favor of English, for the log.
    public IReadOnlyList<string> Problems { get; }

    public string Get(string key) => _values[key];

    public static LocalUiStrings LoadEnglish(string languagesDirectory)
    {
        using var english = File.OpenRead(Path.Combine(languagesDirectory, "en.xaml"));
        var values = ReadValues(english);
        var required = LoadEmbeddedEnglish();
        foreach (var key in required._values.Keys)
            if (!values.ContainsKey(key)) throw new InvalidDataException($"The English language file is missing '{key}'.");
        return new LocalUiStrings(values);
    }

    public static LocalUiStrings LoadEmbeddedEnglish()
    {
        using var stream = typeof(LocalUiStrings).Assembly.GetManifestResourceStream("CircleToSearch.Languages.en.xaml")
            ?? throw new InvalidDataException("The embedded English language resource is missing.");
        return new LocalUiStrings(ReadValues(stream));
    }

    // English is the complete, verified base; a translation must never stop the app from starting,
    // so an unreadable file is skipped and each unusable string keeps its English value.
    public LocalUiStrings Translate(string languagesDirectory, CultureInfo culture)
    {
        var values = new Dictionary<string, string>(_values, StringComparer.Ordinal);
        var problems = new List<string>();
        for (var candidate = culture; !string.IsNullOrEmpty(candidate.Name); candidate = candidate.Parent)
        {
            if (candidate.Name.Equals("en", StringComparison.OrdinalIgnoreCase)) break;
            var file = candidate.Name + ".xaml";
            var path = Path.Combine(languagesDirectory, file);
            if (!File.Exists(path)) continue;
            if (!TryRead(path, out var translated, out var error))
            {
                problems.Add($"{file} was ignored: {error}");
                continue;
            }
            foreach (var (key, value) in translated)
            {
                if (!_values.TryGetValue(key, out var english)) problems.Add($"{file}: unknown key '{key}'.");
                else if (!SameStructure(english, value)) problems.Add($"{file}: '{key}' does not keep the placeholders or '|' separators of the English text.");
                else values[key] = value;
            }
            break;
        }
        return new LocalUiStrings(values, problems.AsReadOnly());
    }

    public static string? ReadError(string path) => TryRead(path, out _, out var error) ? null : error;

    private static bool TryRead(string path, out Dictionary<string, string> values, out string? error)
    {
        try
        {
            using var stream = File.OpenRead(path);
            values = ReadValues(stream);
            error = null;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or XmlException or InvalidDataException)
        {
            values = [];
            error = exception.Message;
            return false;
        }
    }

    // Format items must match the English text verbatim, format specifiers included, because arguments such as
    // dates reject specifiers meant for other types. Pipes separate file dialog filter entries.
    private static bool SameStructure(string english, string translation)
    {
        if (english.Count(c => c == '|') != translation.Count(c => c == '|')) return false;
        // Strings without format items are never formatted, so their braces are literal.
        if (FormatItems(english) is not { Count: > 0 } expected) return true;
        return FormatItems(translation) is { } actual && actual.SetEquals(expected);
    }

    private static HashSet<string>? FormatItems(string template)
    {
        var items = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < template.Length; index++)
        {
            var character = template[index];
            if (character is '{' or '}' && index + 1 < template.Length && template[index + 1] == character) { index++; continue; }
            if (character == '}') return null;
            if (character != '{') continue;
            var end = template.IndexOf('}', index + 1);
            if (end < 0) return null;
            items.Add(template[index..(end + 1)]);
            index = end;
        }
        return items;
    }

    private static Dictionary<string, string> ReadValues(Stream stream)
    {
        var document = XDocument.Load(stream, LoadOptions.PreserveWhitespace);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XNamespace system = "clr-namespace:System;assembly=mscorlib";
        if (document.Root?.Name != presentation + "ResourceDictionary")
            throw new InvalidDataException("The language resource must be a ResourceDictionary.");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var element in document.Root.Elements())
        {
            var key = element.Attribute(xaml + "Key")?.Value;
            if (element.Name != system + "String" || string.IsNullOrWhiteSpace(key) ||
                string.IsNullOrWhiteSpace(element.Value) || !values.TryAdd(key, element.Value))
                throw new InvalidDataException("The language resource contains an invalid or duplicate string.");
        }
        return values;
    }
}

using System.Collections.Frozen;
using System.Globalization;
using System.IO;
using System.Xml.Linq;

namespace CircleToSearch.Ui;

internal sealed class LocalUiStrings
{
    private readonly FrozenDictionary<string, string> _values;

    private LocalUiStrings(Dictionary<string, string> values) => _values = values.ToFrozenDictionary(StringComparer.Ordinal);

    public string Get(string key) => _values[key];

    public static LocalUiStrings Load(string languagesDirectory, CultureInfo culture)
    {
        using var english = File.OpenRead(Path.Combine(languagesDirectory, "en.xaml"));
        var values = ReadValues(english);
        var required = LoadEmbeddedEnglish();
        foreach (var key in required._values.Keys)
            if (!values.ContainsKey(key)) throw new InvalidDataException($"The English language file is missing '{key}'.");

        for (var candidate = culture; !string.IsNullOrEmpty(candidate.Name); candidate = candidate.Parent)
        {
            if (candidate.Name.Equals("en", StringComparison.OrdinalIgnoreCase)) break;
            var path = Path.Combine(languagesDirectory, candidate.Name + ".xaml");
            if (!File.Exists(path)) continue;
            using var localized = File.OpenRead(path);
            foreach (var (key, value) in ReadValues(localized)) values[key] = value;
            break;
        }
        return new LocalUiStrings(values);
    }

    public static LocalUiStrings LoadEmbeddedEnglish()
    {
        using var stream = typeof(LocalUiStrings).Assembly.GetManifestResourceStream("CircleToSearch.Languages.en.xaml")
            ?? throw new InvalidDataException("The embedded English language resource is missing.");
        return new LocalUiStrings(ReadValues(stream));
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

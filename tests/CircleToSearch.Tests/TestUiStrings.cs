using System.Xml.Linq;
using CircleToSearch.Ui;

namespace CircleToSearch.Tests;

internal static class TestUiStrings
{
    public static IReadOnlyDictionary<string, string> EnglishValues { get; } = LoadEnglishValues();

    public static UiStrings English { get; } = LoadEnglish();

    private static UiStrings LoadEnglish()
    {
        var values = EnglishValues;
        return new UiStrings(key => values.TryGetValue(key, out var value)
            ? value
            : throw new KeyNotFoundException($"Translation key '{key}' was not found."));
    }

    private static IReadOnlyDictionary<string, string> LoadEnglishValues()
    {
        var path = Path.Combine(TestOutputPaths.RepoDirectory, "Languages", "en.xaml");
        var document = XDocument.Load(path, LoadOptions.PreserveWhitespace);
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        return document.Root!.Elements().ToDictionary(
            element => element.Attribute(xaml + "Key")?.Value
                       ?? throw new InvalidDataException("A translation is missing x:Key."),
            element => element.Value);
    }
}

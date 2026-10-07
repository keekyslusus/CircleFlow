using System.Xml.Linq;
using CircleToSearch.Ui;

namespace CircleToSearch.Tests;

internal static class TestUiStrings
{
    public static IReadOnlyDictionary<string, string> EnglishValues { get; } = LoadEnglishValues();

    public static UiStrings English { get; } = LoadEnglish();

    private static UiStrings LoadEnglish() =>
        new(LocalUiStrings.LoadEnglish(new AppPaths().LanguagesDirectory).Get);

    private static IReadOnlyDictionary<string, string> LoadEnglishValues()
    {
        var path = Path.Combine(new AppPaths().LanguagesDirectory, "en.xaml");
        var document = XDocument.Load(path, LoadOptions.PreserveWhitespace);
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        return document.Root!.Elements().ToDictionary(
            element => element.Attribute(xaml + "Key")?.Value
                       ?? throw new InvalidDataException("A translation is missing x:Key."),
            element => element.Value);
    }
}

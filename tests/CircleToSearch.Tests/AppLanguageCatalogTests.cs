using System.Globalization;
using CircleToSearch.Ui;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class AppLanguageCatalogTests
{
    [Fact]
    public void Lists_English_first_then_translations_by_native_name_and_skips_unknown_file_names()
    {
        var directory = NewLanguages("en", "ru", "de", "not-a-culture", "backup.en");
        var catalog = new AppLanguageCatalog(directory);
        Assert.Equal(
            [new AppLanguageOption("en", "English"), new("de", "Deutsch"), new("ru", "Русский")],
            catalog.Available);
    }

    [Fact]
    public void Unreadable_translation_is_not_offered()
    {
        var directory = NewLanguages("en", "ru");
        File.WriteAllText(Path.Combine(directory, "de.xaml"), "<ResourceDictionary broken");
        Assert.Equal(["en", "ru"], new AppLanguageCatalog(directory).Available.Select(option => option.Tag));
    }

    [Theory]
    [InlineData("", "fr-FR", "fr-FR")]
    [InlineData("", "ru-RU", "ru")]
    [InlineData("RU", "fr-FR", "ru")]
    [InlineData("ru-RU", "fr-FR", "ru")]
    [InlineData("en", "ru-RU", "en")]
    [InlineData("de", "fr-FR", "fr-FR")]
    [InlineData("not a language", "fr-FR", "fr-FR")]
    public void Saved_language_wins_while_it_or_its_neutral_language_is_delivered(string saved, string system, string expected)
    {
        var catalog = new AppLanguageCatalog(NewLanguages("en", "ru"));
        Assert.Equal(expected, catalog.Resolve(saved, CultureInfo.GetCultureInfo(system)).Name);
    }

    [Fact]
    public void Missing_directory_lists_no_languages()
    {
        var catalog = new AppLanguageCatalog(Path.Combine(TestOutputPaths.TempDirectory, "missing-" + Guid.NewGuid().ToString("N")));
        Assert.Empty(catalog.Available);
        Assert.Equal(string.Empty, catalog.Find("en"));
    }

    private static string NewLanguages(params string[] names)
    {
        var directory = Path.Combine(TestOutputPaths.TempDirectory, "app-languages-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        foreach (var name in names)
            File.WriteAllText(Path.Combine(directory, name + ".xaml"),
                """<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"/>""");
        return directory;
    }
}

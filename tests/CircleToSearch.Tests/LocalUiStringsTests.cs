using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using CircleToSearch.Ui;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class LocalUiStringsTests
{
    [Fact]
    public void Exact_culture_wins_and_missing_keys_fall_back_to_English()
    {
        var directory = NewLanguages();
        WriteLanguage(directory, "fr", ("app_tray_open", "neutral open"), ("app_tray_exit", "neutral exit"));
        WriteLanguage(directory, "fr-CA", ("app_tray_open", "exact open"));
        var source = LocalUiStrings.Load(directory, CultureInfo.GetCultureInfo("fr-CA"));
        Assert.Equal("exact open", source.Get("app_tray_open"));
        Assert.Equal("Exit", source.Get("app_tray_exit"));
    }

    [Theory]
    [InlineData("fr-CA", "fr")]
    [InlineData("zh-Hant-TW", "zh-Hant")]
    public void Neutral_culture_is_used_when_the_exact_file_is_missing(string culture, string neutral)
    {
        var directory = NewLanguages();
        WriteLanguage(directory, neutral, ("app_tray_open", "neutral open"));
        Assert.Equal("neutral open", LocalUiStrings.Load(directory, CultureInfo.GetCultureInfo(culture)).Get("app_tray_open"));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("")]
    [InlineData("en-GB")]
    public void Missing_localization_and_invariant_culture_use_English(string culture)
    {
        var source = LocalUiStrings.Load(NewLanguages(), CultureInfo.GetCultureInfo(culture));
        Assert.Equal("Open", source.Get("app_tray_open"));
        Assert.Equal("CircleFlow", source.Get("plugin_circletosearch_plugin_name"));
    }

    [Fact]
    public async Task Loaded_dictionary_is_stable_on_background_threads_and_preserves_culture()
    {
        var directory = NewLanguages();
        var formatting = CultureInfo.CurrentCulture;
        var ui = CultureInfo.CurrentUICulture;
        var source = LocalUiStrings.Load(directory, CultureInfo.GetCultureInfo("fr-CA"));
        File.WriteAllText(Path.Combine(directory, "en.xaml"), "changed after load");
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            Assert.Equal("Open", source.Get("app_tray_open"));
            Assert.Equal(formatting, CultureInfo.CurrentCulture);
            Assert.Equal(ui, CultureInfo.CurrentUICulture);
        })));
        Assert.Equal(formatting, CultureInfo.CurrentCulture);
        Assert.Equal(ui, CultureInfo.CurrentUICulture);
    }

    [Fact]
    public void Missing_or_corrupt_English_does_not_start_with_an_incomplete_dictionary()
    {
        var directory = NewLanguages();
        var english = Path.Combine(directory, "en.xaml");
        File.Delete(english);
        Assert.Throws<FileNotFoundException>(() => LocalUiStrings.Load(directory, CultureInfo.InvariantCulture));
        File.WriteAllText(english, "not xml");
        Assert.Throws<XmlException>(() => LocalUiStrings.Load(directory, CultureInfo.InvariantCulture));
        WriteLanguage(directory, "en", ("app_tray_open", "Open"));
        Assert.Throws<InvalidDataException>(() => LocalUiStrings.Load(directory, CultureInfo.InvariantCulture));

        var fallback = new UiStrings(LocalUiStrings.LoadEmbeddedEnglish().Get);
        Assert.Equal("CircleFlow", fallback.PluginTitle);
        Assert.Equal(TestUiStrings.English.StartupLanguageFailed, fallback.StartupLanguageFailed);
    }

    [Fact]
    public void Embedded_backup_matches_the_delivered_English_file()
    {
        var embedded = LocalUiStrings.LoadEmbeddedEnglish();
        Assert.All(TestUiStrings.EnglishValues, entry => Assert.Equal(entry.Value, embedded.Get(entry.Key)));
        Assert.NotEmpty(TestUiStrings.English.StartupDataFailed("C:\\CircleFlow\\Data"));
        Assert.NotEmpty(TestUiStrings.English.HotkeyConflict("Ctrl+Alt+Space"));
    }

    [Fact]
    public void Duplicate_or_empty_translations_are_rejected()
    {
        var directory = NewLanguages();
        WriteLanguage(directory, "fr", ("app_tray_open", "one"), ("app_tray_open", "two"));
        Assert.Throws<InvalidDataException>(() => LocalUiStrings.Load(directory, CultureInfo.GetCultureInfo("fr")));
        WriteLanguage(directory, "fr", ("app_tray_open", " "));
        Assert.Throws<InvalidDataException>(() => LocalUiStrings.Load(directory, CultureInfo.GetCultureInfo("fr")));
    }

    private static string NewLanguages()
    {
        var directory = Path.Combine(TestOutputPaths.TempDirectory, "languages-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Languages", "en.xaml"), Path.Combine(directory, "en.xaml"));
        return directory;
    }

    private static void WriteLanguage(string directory, string culture, params (string Key, string Value)[] values)
    {
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XNamespace system = "clr-namespace:System;assembly=mscorlib";
        new XDocument(new XElement(presentation + "ResourceDictionary", values.Select(value =>
            new XElement(system + "String", new XAttribute(xaml + "Key", value.Key), value.Value))))
            .Save(Path.Combine(directory, culture + ".xaml"));
    }
}

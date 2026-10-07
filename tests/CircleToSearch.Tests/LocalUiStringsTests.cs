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
        var source = Load(directory, CultureInfo.GetCultureInfo("fr-CA"));
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
        Assert.Equal("neutral open", Load(directory, CultureInfo.GetCultureInfo(culture)).Get("app_tray_open"));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("")]
    [InlineData("en-GB")]
    public void Missing_localization_and_invariant_culture_use_English(string culture)
    {
        var source = Load(NewLanguages(), CultureInfo.GetCultureInfo(culture));
        Assert.Equal("Open", source.Get("app_tray_open"));
        Assert.Equal("CircleFlow", source.Get("plugin_circletosearch_plugin_name"));
    }

    [Fact]
    public async Task Loaded_dictionary_is_stable_on_background_threads_and_preserves_culture()
    {
        var directory = NewLanguages();
        var formatting = CultureInfo.CurrentCulture;
        var ui = CultureInfo.CurrentUICulture;
        var source = Load(directory, CultureInfo.GetCultureInfo("fr-CA"));
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
        Assert.Throws<FileNotFoundException>(() => Load(directory, CultureInfo.InvariantCulture));
        File.WriteAllText(english, "not xml");
        Assert.Throws<XmlException>(() => Load(directory, CultureInfo.InvariantCulture));
        WriteLanguage(directory, "en", ("app_tray_open", "Open"));
        Assert.Throws<InvalidDataException>(() => Load(directory, CultureInfo.InvariantCulture));

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

    [Theory]
    [InlineData("not xml")]
    [InlineData("<Window/>")]
    [InlineData(null)]
    public void Unreadable_translation_falls_back_to_the_neutral_file_or_English(string? content)
    {
        var directory = NewLanguages();
        var broken = Path.Combine(directory, "fr-CA.xaml");
        if (content is null) WriteLanguage(directory, "fr-CA", ("app_tray_open", "one"), ("app_tray_open", "two"));
        else File.WriteAllText(broken, content);

        var english = Load(directory, CultureInfo.GetCultureInfo("fr-CA"));
        Assert.Equal("Open", english.Get("app_tray_open"));
        Assert.StartsWith("fr-CA.xaml was ignored", Assert.Single(english.Problems));

        WriteLanguage(directory, "fr", ("app_tray_open", "neutral open"));
        Assert.Equal("neutral open", Load(directory, CultureInfo.GetCultureInfo("fr-CA")).Get("app_tray_open"));
    }

    [Fact]
    public void Unknown_keys_and_mismatched_placeholders_keep_English()
    {
        var directory = NewLanguages();
        WriteLanguage(directory, "fr",
            ("app_tray_open_hotkey", "Ouvrir ({1})"),
            ("app_hotkey_conflict", "{0} ({0}) introuvable"),
            ("plugin_circletosearch_music_match_album_genre", "Genre : {1} · Album : {0}"),
            ("app_settings_version_value", "v{0"),
            ("app_tray_exit", "Quitter {curly}"),
            ("app_tray_unknown", "Inconnu"),
            ("plugin_circletosearch_image_file_name", "circleflow_{0:%}"),
            ("plugin_circletosearch_image_save_filter", "Image PNG (*.png)|*.png|JPEG"),
            ("plugin_circletosearch_image_save_title", "Enregistrer | image"));
        var source = Load(directory, CultureInfo.GetCultureInfo("fr"));
        var strings = new UiStrings(source.Get);

        Assert.Equal("Open (Ctrl+K)", strings.TrayOpenWithHotkey("Ctrl+K"));
        Assert.Equal("v1.0", strings.SettingsVersion("1.0"));
        Assert.Equal("Ctrl+K (Ctrl+K) introuvable", strings.HotkeyConflict("Ctrl+K"));
        Assert.Equal("Genre : Pop · Album : A", strings.MusicMatchSubtitle("A", "Pop"));
        Assert.Equal("Quitter {curly}", strings.TrayExit);
        Assert.Equal(TestUiStrings.English.ImageFileName, strings.ImageFileName);
        Assert.Equal(TestUiStrings.English.ImageSaveFilter, strings.ImageSaveFilter);
        Assert.Equal(TestUiStrings.English.ImageSaveTitle, strings.ImageSaveTitle);
        Assert.Equal(6, source.Problems.Count);
        Assert.Contains(source.Problems, problem => problem.Contains("'app_tray_unknown'", StringComparison.Ordinal));
    }

    private static LocalUiStrings Load(string directory, CultureInfo culture) =>
        LocalUiStrings.LoadEnglish(directory).Translate(directory, culture);

    private static string NewLanguages()
    {
        var directory = Path.Combine(TestOutputPaths.TempDirectory, "languages-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.Copy(Path.Combine(new AppPaths().LanguagesDirectory, "en.xaml"), Path.Combine(directory, "en.xaml"));
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

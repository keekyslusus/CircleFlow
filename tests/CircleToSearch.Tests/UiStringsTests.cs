using System.Globalization;
using CircleToSearch.Ui;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class UiStringsTests
{
    [Fact]
    public void English_xaml_drives_typed_ui_strings()
    {
        var strings = TestUiStrings.English;

        Assert.Equal("CircleFlow", strings.PluginTitle);
        Assert.Equal("Select an area", strings.SelectionPrompt);
        Assert.Equal("Translation result", strings.TranslationResultTitle);
        Assert.Equal("Reset privacy consent", strings.DebugResetTranslationConsent);
        Assert.Equal("Copied: sample", strings.CopiedText("sample"));
        Assert.Equal("Could not copy to clipboard.", strings.CopyFailed);
        Assert.Equal("Google Lens", strings.GoogleLensProviderName);
        Assert.Equal("Google AI Mode", strings.GoogleAiModeProviderName);
        Assert.Equal("Searching with Google Lens…", strings.SearchBrowserLoading("Google Lens"));
        Assert.Equal("CircleFlow: Google Lens", strings.SearchBrowserWindowTitle(strings.GoogleLensProviderName));
        Assert.Equal("CircleFlow: Yandex Images", strings.SearchBrowserWindowTitle("Yandex Images"));
        Assert.Equal(
            "Ctrl+Alt+Space active",
            strings.HotkeyStatusActive("Ctrl+Alt+Space"));
        Assert.Equal(
            "The search service answered HTTP 503.",
            strings.SearchUnexpectedStatus(503));
    }

    [Fact]
    public void Delivered_translations_are_named_by_culture_and_match_English_keys_and_placeholders()
    {
        var directory = Path.Combine(TestOutputPaths.RepoDirectory, "Assets", "Languages");
        var english = LocalUiStrings.LoadEnglish(directory);
        var files = Directory.GetFiles(directory, "*.xaml");
        var translations = files.Select(path => Path.GetFileNameWithoutExtension(path)!).Where(name => name != "en").ToArray();

        Assert.NotEmpty(translations);
        Assert.All(translations, name =>
        {
            var culture = CultureInfo.GetCultureInfo(name, predefinedOnly: true);
            Assert.Equal(culture.Name, name);
            Assert.Empty(english.Translate(directory, culture).Problems);
        });
        Assert.Equal(files.Length, new AppLanguageCatalog(directory).Available.Count);
        Assert.All(files, path => Assert.DoesNotContain('—', File.ReadAllText(path)));
        Assert.All(TestUiStrings.EnglishValues.Values, value =>
            Assert.DoesNotMatch("[\\u0400-\\u04FF]", value));
    }

    [Fact]
    public void Language_files_are_copied_to_application_output()
    {
        foreach (var source in Directory.GetFiles(Path.Combine(TestOutputPaths.RepoDirectory, "Assets", "Languages"), "*.xaml"))
        {
            var path = Path.Combine(new AppPaths().LanguagesDirectory, Path.GetFileName(source));
            Assert.True(File.Exists(path), $"Missing language file: {path}");
        }
    }

    [Fact]
    public void Every_typed_accessor_resolves_an_xaml_key()
    {
        var strings = TestUiStrings.English;

        var values = typeof(UiStrings).GetProperties()
            .Where(property => property.PropertyType == typeof(string))
            .Select(property => Assert.IsType<string>(property.GetValue(strings)))
            .ToArray();

        Assert.NotEmpty(values);
        Assert.All(values, Assert.NotEmpty);
        Assert.NotEmpty(strings.VisualSearchQuerySubtitle("status"));
        Assert.NotEmpty(strings.SearchProvider("provider"));
        Assert.NotEmpty(strings.SelectSearchProvider("provider"));
        Assert.NotEmpty(strings.SearchBrowserRuntime(null));
        Assert.NotEmpty(strings.SearchBrowserRuntime("1.0"));
        Assert.NotEmpty(strings.SavingFailed("detail"));
        Assert.NotEmpty(strings.HotkeyStatusActive("gesture"));
        Assert.NotEmpty(strings.HotkeyStatusUnavailable("gesture"));
        Assert.NotEmpty(strings.SearchUnexpectedStatus(500));
        Assert.NotEmpty(strings.BrowserRuntimeRequired("provider"));
        Assert.NotEmpty(strings.BrowserImageAttachmentFailed("provider"));
        Assert.NotEmpty(strings.SearchBrowserWindowTitle("provider"));
        Assert.NotEmpty(strings.SearchBrowserLoading("provider"));
        Assert.NotEmpty(strings.SearchBrowserShowFailed("provider"));
        Assert.NotEmpty(strings.StartingSelectionFailed("detail"));
        Assert.NotEmpty(strings.SearchFailed("detail"));
        Assert.NotEmpty(strings.CopiedText("preview"));
    }
}

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
        Assert.Equal("Searching with Google Lens…", strings.GoogleLensLoading);
        Assert.Equal(
            "Ctrl+Alt+Space active",
            strings.HotkeyStatusActive("Ctrl+Alt+Space"));
        Assert.Equal(
            "The search service answered HTTP 503.",
            strings.SearchUnexpectedStatus(503));
    }

    [Fact]
    public void Language_directory_contains_only_english_xaml()
    {
        var directory = Path.Combine(TestOutputPaths.RepoDirectory, "Languages");
        var languages = Directory.GetFiles(directory, "*.xaml")
            .Select(path => Path.GetFileName(path)!)
            .Order()
            .ToArray();

        Assert.Equal(["en.xaml"], languages);
        Assert.All(TestUiStrings.EnglishValues.Values, value =>
            Assert.DoesNotMatch("[\\u0400-\\u04FF]", value));
    }

    [Fact]
    public void English_xaml_is_copied_to_plugin_output()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Languages", "en.xaml");

        Assert.True(File.Exists(path), $"Missing language file: {path}");
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
        Assert.NotEmpty(strings.GoogleLensRuntime(null));
        Assert.NotEmpty(strings.GoogleLensRuntime("1.0"));
        Assert.NotEmpty(strings.SavingFailed("detail"));
        Assert.NotEmpty(strings.HotkeyStatusActive("gesture"));
        Assert.NotEmpty(strings.HotkeyStatusUnavailable("gesture"));
        Assert.NotEmpty(strings.SearchUnexpectedStatus(500));
        Assert.NotEmpty(strings.BrowserRuntimeRequired("provider"));
        Assert.NotEmpty(strings.BrowserImageAttachmentFailed("provider"));
        Assert.NotEmpty(strings.StartingSelectionFailed("detail"));
        Assert.NotEmpty(strings.SearchFailed("detail"));
    }
}

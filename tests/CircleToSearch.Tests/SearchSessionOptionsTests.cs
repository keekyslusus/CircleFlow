using System.Globalization;
using CircleToSearch.Search;
using CircleToSearch.Settings;
using CircleToSearch.TextRecognition;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class SearchSessionOptionsTests
{
    [Fact]
    public async Task Settings_are_read_once_before_hiding_and_the_next_session_observes_changes()
    {
        var service = TestSettings.Create(new AppSettings
        {
            HideDelayMilliseconds = 0, PaddingPx = 12, LassoMinDiagonalPx = 14, MaxLongSidePx = 1700,
            OcrLanguageTag = "en-US", TranslationTargetLanguageTag = "ru-RU",
        });
        var languages = new OcrLanguageCatalog([new("en-US", "English"), new("ja-JP", "Japanese")]);
        var initial = SearchSessionOptions.From(service.Snapshot, languages, CultureInfo.GetCultureInfo("fr-FR"));
        var reads = 0;
        var workflow = new RecordingWorkflow();
        var directory = Path.Combine(TestOutputPaths.TempDirectory, "session-options-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var coordinator = new SearchCoordinator(workflow,
            () => service.Apply(new SettingsEdits
            {
                HideDelayMilliseconds = 1, PaddingPx = 22, LassoMinDiagonalPx = 24, MaxLongSidePx = 2700,
                OcrLanguageTag = "ja-JP", TranslationTargetLanguageTag = "de-DE",
            }).ThrowIfFailed("test update failed"),
            () => { reads++; return SearchSessionOptions.From(service.Snapshot, languages, CultureInfo.GetCultureInfo("fr-FR")); },
            new TestPluginNotifier(), TestUiStrings.English, new PluginLog(directory));
        await coordinator.StartFromQueryAsync();
        Assert.Equal(initial, Assert.Single(workflow.Options));
        Assert.Equal(1, reads);
        await coordinator.StartFromQueryAsync();
        Assert.Equal(SearchSessionOptions.From(service.Snapshot, languages, CultureInfo.GetCultureInfo("fr-FR")), workflow.Options[1]);
        Assert.Equal(2, reads);
        Assert.NotEqual(workflow.Options[0], workflow.Options[1]);
    }

    [Fact]
    public void Empty_translation_language_uses_system_culture_and_missing_OCR_pack_uses_existing_fallback()
    {
        var languages = new OcrLanguageCatalog([new("en-US", "English")]);
        var settings = new AppSettings { OcrLanguageTag = "ru-RU" };
        var options = SearchSessionOptions.From(settings, languages, CultureInfo.GetCultureInfo("fr-CA"));
        Assert.Null(options.OcrLanguageTag);
        Assert.Equal("fr-CA", options.TranslationTargetLanguageTag);
        Assert.Equal("ru-RU", settings.OcrLanguageTag);
        Assert.Equal("en", SearchSessionOptions.From(settings, languages, CultureInfo.InvariantCulture).TranslationTargetLanguageTag);
    }

    private sealed class RecordingWorkflow : ISearchSessionWorkflow
    {
        public List<SearchSessionOptions> Options { get; } = [];
        public Task RunAsync(SearchSessionOptions options, Action onUploadStarted, CancellationToken cancellationToken)
        {
            Options.Add(options);
            return Task.CompletedTask;
        }
    }
}

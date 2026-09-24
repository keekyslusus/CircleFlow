using System.Globalization;
using CircleToSearch.Search;
using CircleToSearch.Settings;
using CircleToSearch.TextRecognition;
using CircleToSearch.Interop;
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
            OcrLanguageTag = "en-US", TextSearchEngineId = "kagi",
        });
        var languages = new OcrLanguageCatalog([new("en-US", "English"), new("ja-JP", "Japanese")]);
        var inputTag = "en-US";
        var initial = SearchSessionOptions.From(service.Snapshot, languages, CultureInfo.GetCultureInfo("fr-FR"),
            new KeyboardLanguageSnapshot(0, inputTag));
        Assert.Equal("kagi", initial.TextSearchEngineId);
        var reads = 0;
        var workflow = new RecordingWorkflow();
        var directory = Path.Combine(TestOutputPaths.TempDirectory, "session-options-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var coordinator = new SearchCoordinator(workflow,
            () => { inputTag = "ja-JP"; service.Apply(new SettingsEdits
            {
                HideDelayMilliseconds = 1, PaddingPx = 22, LassoMinDiagonalPx = 24, MaxLongSidePx = 2700,
                OcrLanguageTag = "ja-JP",
            }).ThrowIfFailed("test update failed"); return Task.CompletedTask; },
            () => { reads++; return SearchSessionOptions.From(service.Snapshot, languages, CultureInfo.GetCultureInfo("fr-FR"),
                new KeyboardLanguageSnapshot(0, inputTag)); },
            new TestPluginNotifier(), TestUiStrings.English, new PluginLog(directory));
        await coordinator.OpenAsync();
        Assert.Equal(initial, Assert.Single(workflow.Options));
        Assert.Equal("en-US", workflow.Options[0].OcrLanguageTag);
        Assert.Equal(1, reads);
        await coordinator.OpenAsync();
        Assert.Equal(SearchSessionOptions.From(service.Snapshot, languages, CultureInfo.GetCultureInfo("fr-FR"),
            new KeyboardLanguageSnapshot(0, inputTag)), workflow.Options[1]);
        Assert.Equal("ja-JP", workflow.Options[1].OcrLanguageTag);
        Assert.Equal(2, reads);
        Assert.NotEqual(workflow.Options[0], workflow.Options[1]);
    }

    [Fact]
    public void Translation_uses_system_culture_and_missing_ocr_pack_falls_back_to_keyboard()
    {
        var languages = new OcrLanguageCatalog([new("en-US", "English")]);
        var settings = new AppSettings { OcrLanguageTag = "ru-RU" };
        var options = SearchSessionOptions.From(settings, languages, CultureInfo.GetCultureInfo("fr-CA"));
        Assert.Null(options.OcrLanguageTag);
        Assert.Equal("fr-CA", options.TranslationTargetLanguageTag);
        Assert.Equal("en-US", SearchSessionOptions.From(settings, languages, CultureInfo.GetCultureInfo("fr-CA"),
            new KeyboardLanguageSnapshot(0x0409, "en-US")).OcrLanguageTag);
        Assert.Equal("en", SearchSessionOptions.From(settings, languages, CultureInfo.InvariantCulture).TranslationTargetLanguageTag);
    }

    [Fact]
    public void Saved_ocr_language_overrides_captured_input_language()
    {
        var languages = new OcrLanguageCatalog([new("en-US", "English"), new("ru-RU", "Russian")]);
        var settings = new AppSettings { OcrLanguageTag = "en-US" };
        var captured = new KeyboardLanguageSnapshot(0x0419, "ru-RU");
        var options = SearchSessionOptions.From(settings, languages, CultureInfo.GetCultureInfo("en-US"), captured);
        Assert.Equal("en-US", options.OcrLanguageTag);
        Assert.Equal(captured, options.InputLanguage);
        Assert.Equal("ru-RU", SearchSessionOptions.From(settings with { OcrLanguageTag = "" }, languages,
            CultureInfo.GetCultureInfo("en-US"), captured).OcrLanguageTag);
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

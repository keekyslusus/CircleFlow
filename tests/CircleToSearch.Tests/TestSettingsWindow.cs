using System.Globalization;
using CircleToSearch.Capture;
using CircleToSearch.MusicRecognition;
using CircleToSearch.Search;
using CircleToSearch.Settings;
using CircleToSearch.Shell;
using CircleToSearch.Shell.SettingsPreview;
using CircleToSearch.TextRecognition;
using CircleToSearch.Ui;
using CircleToSearch.Updates;

namespace CircleToSearch.Tests;

internal sealed class TestSettingsWindow
{
    public TestSettingsWindow(SettingsService? settings = null, bool openSucceeds = true,
        string? webViewRuntimeVersion = "140.0.3485.54", OcrLanguageCatalog? ocrLanguages = null,
        CultureInfo? culture = null, string? audioOutputName = "Speakers (Test Audio)", bool launchAtStartup = true,
        string? languagesDirectory = null, Func<Task<UpdateCheckOutcome>>? checkForUpdates = null)
    {
        var log = new PluginLog(Path.Combine(TestOutputPaths.TempDirectory, "settings-window-" + Guid.NewGuid().ToString("N")));
        Settings = settings ?? TestSettings.Create();
        var router = new VisualSearchProviderRouter(
            [
                Registration(SearchProviderIds.GoogleLens, TestUiStrings.English.GoogleLensProviderName),
                Registration(SearchProviderIds.YandexImages, TestUiStrings.English.YandexImagesProviderName),
                Registration(SearchProviderIds.TraceMoe, TestUiStrings.English.TraceMoeProviderName),
            ],
            SearchProviderIds.GoogleLens,
            log);
        var providers = new ProviderSelectionStore(router, Settings, Notifier, TestUiStrings.English, log);
        var urlOpening = new UrlOpeningService(
            target => { Opened.Add(target); return openSucceeds; }, Notifier, TestUiStrings.English, log);
        Startup = new TestStartupRegistry(Paths.ExecutablePath, log);
        Startup.Registration.TrySet(launchAtStartup);
        OcrLanguages = ocrLanguages ?? new OcrLanguageCatalog([new("de-DE", "German"), new("en-US", "English")]);
        languagesDirectory ??= Paths.LanguagesDirectory;
        var language = new UiLanguage(LocalUiStrings.LoadEnglish(languagesDirectory),
            new AppLanguageCatalog(languagesDirectory), CultureInfo.GetCultureInfo("en-US"), log);
        language.Apply(Settings.Snapshot.AppLanguageTag);
        Strings = new UiStrings(language.Get);
        HistoryFilePath = Path.Combine(TestOutputPaths.NewTempDirectory("music-history-" + Guid.NewGuid().ToString("N")), "music-history.json");
        History = new MusicHistory(HistoryFilePath,
            () => Settings.Snapshot.SaveMusicHistory, () => Settings.Snapshot.MusicHistoryRetentionDays, Time, log);
        Model = new SettingsWindowModel(Settings, providers, OcrLanguages, History,
            new MusicResultPresenter(urlOpening, Notifier, TestUiStrings.English), language, culture ?? CultureInfo.GetCultureInfo("en-US"),
            new ProjectSupport(urlOpening), urlOpening, Paths, Strings, () => webViewRuntimeVersion,
            () => AudioOutputName, Startup.Registration, () => OnboardingRequests++, () => TestBrowserRequests++,
            () => TestNotificationRequests++,
            checkForUpdates);
        AudioOutputName = audioOutputName;
    }

    public SettingsService Settings { get; }
    public UiStrings Strings { get; }
    public SettingsWindowModel Model { get; }
    public OcrLanguageCatalog OcrLanguages { get; }
    public MusicHistory History { get; }
    public string HistoryFilePath { get; }
    public TestTimeProvider Time { get; } = new(new DateTimeOffset(2026, 9, 29, 16, 40, 0, TimeSpan.Zero));
    public List<string> Clipboard { get; } = [];
    public TestStartupRegistry Startup { get; }
    public string? AudioOutputName { get; set; }
    public AppPaths Paths { get; } = new();
    public TestPluginNotifier Notifier { get; } = new();
    public List<string> Opened { get; } = [];
    public int OnboardingRequests { get; private set; }
    public int TestBrowserRequests { get; private set; }
    public int TestNotificationRequests { get; private set; }

    public SettingsWindowView CreateView(bool light = true) =>
        new(Strings, light, Paths.TrayIconPath, Model,
            feedback => new ClipboardCopyService(Clipboard.Add, feedback, Strings));

    private static VisualSearchProviderRegistration Registration(string id, string name) =>
        new(new SearchProviderDescriptor(id, name), () => throw new InvalidOperationException("Not used by settings."));
}

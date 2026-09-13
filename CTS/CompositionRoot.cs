using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Windows;
using CircleToSearch.Capture;
using CircleToSearch.Capture.OverlayInteractions;
using CircleToSearch.Interop;
using CircleToSearch.MusicRecognition;
using CircleToSearch.MusicRecognition.Audio;
using CircleToSearch.MusicRecognition.Shazam;
using CircleToSearch.Search;
using CircleToSearch.Search.Browser;
using CircleToSearch.Settings;
using CircleToSearch.Shell;
using CircleToSearch.Trigger;
using CircleToSearch.Ui;
using CircleToSearch.TextRecognition;
using CircleToSearch.Translation;
using System.Globalization;

namespace CircleToSearch;

public static class CompositionRoot
{
    internal const string HotkeyThreadName = "CircleToSearch hotkey";
    internal const string SearchBrowserThreadName = "CircleToSearch WebView2";
    internal const bool TranslationMemoryProfilingEnabled = false;

    public static int Run() => Run(new AppPaths(),
        (message, title, icon) => MessageBox.Show(message, title, MessageBoxButton.OK, icon));

    internal static int Run(AppPaths paths, Action<string, string, MessageBoxImage> reportStartupMessage,
        string? instanceName = null)
    {
        var application = CreateApplication();
        UiStrings strings;
        try
        {
            var localStrings = LocalUiStrings.Load(paths.LanguagesDirectory, CultureInfo.CurrentUICulture);
            strings = new UiStrings(localStrings.Get);
        }
        catch
        {
            var fallback = LocalUiStrings.LoadEmbeddedEnglish();
            strings = new UiStrings(fallback.Get);
            reportStartupMessage(strings.StartupLanguageFailed, strings.PluginTitle, MessageBoxImage.Error);
            return 1;
        }

        try
        {
            using var instance = SingleInstanceCoordinator.TryAcquire(instanceName ?? SingleInstanceCoordinator.CurrentSessionName);
            if (instance is null)
            {
                reportStartupMessage(strings.ActivationAlreadyRunning, strings.PluginTitle, MessageBoxImage.Information);
                return 1;
            }
            try { AppDataDirectory.Initialize(paths); }
            catch
            {
                reportStartupMessage(strings.StartupDataFailed(paths.DataDirectory), strings.PluginTitle, MessageBoxImage.Error);
                return 1;
            }
            var log = new PluginLog(paths.LogsDirectory);
            log.Info(nameof(CompositionRoot), "standalone host initialized");
            var store = new SettingsStore(paths);
            SettingsLoadResult loaded;
            try { loaded = store.Load(); }
            catch (Exception exception)
            {
                log.SafeError(nameof(CompositionRoot), "load-settings", exception);
                reportStartupMessage(strings.StorageLoadFailed, strings.PluginTitle, MessageBoxImage.Error);
                return 1;
            }
            if (loaded.ResetFields.Count != 0)
                log.Warn(nameof(CompositionRoot), $"settings reset to defaults: {string.Join(", ", loaded.ResetFields)}");
            if (loaded.Recovered)
                reportStartupMessage(strings.StorageRecovered, strings.PluginTitle, MessageBoxImage.Warning);
            // Runtime startup awaits the shell from migration stage 4.
            var lifetime = new AppLifetime(application, () => Task.CompletedTask);
            return lifetime.Run();
        }
        catch
        {
            reportStartupMessage(strings.StartupFailed, strings.PluginTitle, MessageBoxImage.Error);
            return 1;
        }
    }

    internal static Application CreateApplication() => new()
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown,
    };

    internal static AppRuntime Create(
        AppPaths paths,
        AppSettings initialSettings,
        SettingsStore settingsStore,
        UiStrings strings,
        IPluginNotifier notifier,
        Action hideOwnWindows,
        PluginLog log)
    {
        using var rollback = new ResourceRollbackScope(log);
        var hotkeyDispatcher = rollback.Own(new StaDispatcher(HotkeyThreadName));
        var hotkeyWindow = rollback.Replace(hotkeyDispatcher, new HotkeyWindow(hotkeyDispatcher, log));
        var registrar = new HotkeyRegistrar(hotkeyWindow, strings, log);
        var settings = new SettingsService(initialSettings, settingsStore.Save, registrar.TryApply, log);
        var environments = new WebViewEnvironmentFactory(paths, strings, notifier);
        var searchBrowserDispatcher = rollback.Own(new StaDispatcher(SearchBrowserThreadName));
        var searchBrowserHost = rollback.Replace(searchBrowserDispatcher, new SearchBrowserHost(
            paths.RootDirectory,
            paths.SearchProfileDirectory,
            strings,
            log,
            searchBrowserDispatcher,
            () => environments.CreateAsync(paths.SearchProfileDirectory, enableExtensions: true)));
        var traceHttpClient = rollback.Own(new HttpClient { Timeout = Timeout.InfiniteTimeSpan });
        var providerRouter = rollback.Own(new VisualSearchProviderRouter(
            [
                new VisualSearchProviderRegistration(
                    new SearchProviderDescriptor(SearchProviderIds.GoogleLens, strings.GoogleLensProviderName),
                    () => new GoogleLensProvider(
                        jpeg => new GoogleLensBrowserOperation(jpeg, log))),
                new VisualSearchProviderRegistration(
                    new SearchProviderDescriptor(SearchProviderIds.YandexImages, strings.YandexImagesProviderName),
                    () => new YandexImagesProvider(log)),
                new VisualSearchProviderRegistration(
                    new SearchProviderDescriptor(SearchProviderIds.TraceMoe, strings.TraceMoeProviderName),
                    () => new TraceMoeProvider(traceHttpClient)),
            ],
            SearchProviderIds.GoogleLens,
            log));
        var visualSearchPresenter = new VisualSearchResultPresenter(
            searchBrowserHost,
            OpenResultsUrl,
            notifier,
            strings,
            log);
        var visualSearch = new VisualSearchWorkflow(
            providerRouter,
            ImageCropper.EncodeJpeg,
            visualSearchPresenter,
            notifier,
            strings,
            log);
        var musicClock = new SystemMusicRecognitionClock();
        var musicThrottle = rollback.Own(new ShazamRequestThrottle(musicClock));
        var musicHttpClient = rollback.Own(new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan,
            DefaultRequestVersion = HttpVersion.Version11,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact,
        });
        var shazamClient = new ShazamClient(
            musicHttpClient,
            new PixelUserAgentProvider(),
            new ShazamLocationProvider());
        var shazamLocation = shazamClient.Location;
        var shazamLocationDescription = FormattableString.Invariant(
            $"latitude={shazamLocation.Latitude}, longitude={shazamLocation.Longitude}, altitude={shazamLocation.Altitude} m, timezone={shazamLocation.Timezone}");
        log.Info(nameof(ShazamClient),
            $"using User-Agent: {shazamClient.UserAgent}, location: {shazamLocationDescription}");
        var musicRecognizer = new ProgressiveMusicRecognizer(
            new LoopbackCaptureSessionFactory(log),
            shazamClient,
            musicThrottle,
            musicClock,
            log);
        var musicSimulator = new MusicRecognitionSimulator(strings);
        var musicRecognition = new MusicRecognitionWorkflow(musicRecognizer, musicSimulator, log);
        var musicResultPresenter = new MusicResultPresenter(OpenResultsUrl, notifier, strings);
        var providerSelection = new ProviderSelectionStore(
            providerRouter,
            settings,
            notifier,
            strings,
            log);
        var ocrLanguages = new OcrLanguageCatalog();
        var translationHttpClient = rollback.Own(new HttpClient(new SocketsHttpHandler
        { UseCookies = false, AutomaticDecompression = DecompressionMethods.All })
        {
            Timeout = Timeout.InfiniteTimeSpan,
            DefaultRequestVersion = HttpVersion.Version20,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
        });
        var translationMemory = TranslationMemoryProfilingEnabled ? new TranslationMemoryProfiler(paths.LogsDirectory) : null;
        if (translationMemory is not null) rollback.Own(translationMemory);
        translationMemory?.Mark("runtime_ready");
        var translationDispatcher = rollback.Own(new StaDispatcher("CircleToSearch image translation"));
        var imageTranslationSigner = rollback.Replace(translationDispatcher, new GoogleImageTranslationSigner(
            translationHttpClient,
            translationDispatcher,
            () => environments.CreateAsync(paths.ImageTranslationProfileDirectory),
            translationMemory));
        var screenTranslation = new ScreenTranslationWorkflow(
            new GoogleImageTranslationProvider(translationHttpClient, imageTranslationSigner, translationMemory),
            log);
        var textSearch = new TextSearchWorkflow(
            new TextSearchUrlBuilder(),
            OpenResultsUrl,
            notifier,
            strings,
            log);
        var overlayControllerFactory = new OverlayControllerFactory(
            Clipboard.SetText,
            OverlayVisualResources.AnimationsEnabled,
            ocrRecognizer: new WindowsOcrRecognizer(),
            textHitToleranceDips: 3,
            translationConsentAccepted: () => settings.Snapshot.ImageTranslationPrivacyConsentAccepted,
            acceptTranslationConsent: () => settings.SetTranslationConsent(true).ThrowIfFailed(strings.StorageSaveFailed),
            resetTranslationConsent: () => settings.SetTranslationConsent(false).ThrowIfFailed(strings.StorageSaveFailed),
            log: log,
            memoryProfiler: translationMemory);
        var overlayWindowFactory = new OverlayWindowFactory(overlayControllerFactory,
            video => new TraceVideoPreview(video,
                () => environments.CreateAsync(paths.TraceVideoProfileDirectory), log));
        var workflow = new OverlaySessionWorkflow(
            new OverlaySessionFactory(log, new PointerMonitorCapture(), overlayWindowFactory),
            visualSearch,
            musicRecognition,
            musicResultPresenter,
            providerSelection,
            strings,
            log,
            textSearch,
            screenTranslation,
            OpenResultsUrl);
        var coordinator = new SearchCoordinator(
            workflow,
            hideOwnWindows,
            () => SearchSessionOptions.From(settings.Snapshot, ocrLanguages, CultureInfo.CurrentUICulture),
            notifier,
            strings,
            log);
        var lifetime = new PluginRuntimeLifetime(
            coordinator.StopAsync,
            hotkeyWindow.StopAsync,
            searchBrowserHost.StopAsync,
            imageTranslationSigner.StopAsync,
            providerRouter.StopAsync,
            musicHttpClient,
            musicThrottle,
            translationHttpClient,
            traceHttpClient,
            translationMemory,
            log);
        var stopAdapter = new PluginRuntimeStopAdapter(
            coordinator.RequestStop,
            lifetime.StopAsync,
            log,
            TimeSpan.FromSeconds(2));
        var runtime = new AppRuntime(coordinator, stopAdapter, settings);
        rollback.TransferAllTo(stopAdapter);

        hotkeyWindow.HotkeyPressed += () =>
        {
            try
            {
                // Registration waits on this dispatcher; reading settings here could deadlock its lock.
                _ = Task.Run(coordinator.StartFromHotkeyAsync);
            }
            catch (Exception exception)
            {
                log.Error(nameof(CompositionRoot), "dispatching the hotkey failed", exception);
            }
        };

        if (!settings.InitializeHotkey().Success)
            notifier.ShowError(strings.PluginTitle, strings.HotkeyConflict(settings.Snapshot.HotkeyGesture));
        rollback.Commit();
        return runtime;
    }

    private static bool OpenResultsUrl(string url)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                UseShellExecute = true,
                FileName = url,
            });
            return process is not null;
        }
        catch
        {
            return false;
        }
    }
}

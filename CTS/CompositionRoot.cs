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
        string? instanceName = null, CancellationToken startupCancellation = default)
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
            var launch = SingleInstanceCoordinator.AcquireOrActivate(instanceName ?? SingleInstanceCoordinator.CurrentSessionName);
            using var instance = launch.Instance;
            if (instance is null)
            {
                if (launch.Activated) return 0;
                reportStartupMessage(strings.ActivationFailed, strings.PluginTitle, MessageBoxImage.Error);
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
            using var watchdog = new ShutdownWatchdog(log, () => Environment.Exit(1));
            var lifetime = new AppLifetime(application, log, watchdog);
            lifetime.AddStop("stop-ipc", () => _ = instance.StopListeningAsync());
            lifetime.AddCleanup("stop-ipc", instance.StopListeningAsync);
            lifetime.AddRelease("release-instance", instance.Dispose);
            watchdog.AddEmergencyCleanup(instance.AbortListening);
            var startupMessage = strings.StartupFailed;
            var exitCode = lifetime.Run(cancellation =>
            {
                var activation = new OpenCommandDispatcher(application.Dispatcher, log);
                lifetime.AddStop("stop-activation", activation.Dispose);
                instance.StartListening(activation.TryRequestOpen,
                    exception => log.SafeError(nameof(SingleInstanceCoordinator), "activation-listener", exception));
                var store = new SettingsStore(paths);
                SettingsLoadResult loaded;
                try { loaded = store.Load(); }
                catch { startupMessage = strings.StorageLoadFailed; throw; }
                if (loaded.ResetFields.Count != 0)
                    log.Warn(nameof(CompositionRoot), $"settings reset to defaults: {string.Join(", ", loaded.ResetFields)}");
                if (loaded.Recovered)
                    reportStartupMessage(strings.StorageRecovered, strings.PluginTitle, MessageBoxImage.Warning);
                cancellation.ThrowIfCancellationRequested();
                var settingsWindow = new SettingsWindowController(application.Dispatcher, strings);
                lifetime.AddCleanup("close-settings", () => { settingsWindow.Dispose(); return Task.CompletedTask; });
                var notifications = new NotificationPresenter(application.Dispatcher, strings, log);
                lifetime.AddCleanup("close-notifications", () => { notifications.Dispose(); return Task.CompletedTask; });
                var notifier = new PluginNotifier(notifications.ShowMessage, notifications.ShowMessageWithButton,
                    notifications.ShowError, log);
                TrayIcon? tray = null;
                var rollback = new ResourceRollbackScope(log);
                lifetime.AddCleanup("stop-runtime", () => rollback.DisposeAsync().AsTask());
                var runtime = Create(paths, loaded.Settings, store, strings, notifier,
                    () => application.Dispatcher.InvokeAsync(() =>
                    {
                        tray?.CloseMenu();
                        settingsWindow.Hide();
                        notifications.CloseAll();
                    }).Task, log, rollback);
                lifetime.AddStop("stop-runtime-triggers", () => _ = runtime.StopAsync());
                cancellation.ThrowIfCancellationRequested();
                var support = new ProjectSupport(OpenResultsUrl, notifier, strings);
                tray = new TrayIcon(paths.TrayIconPath, strings, application.Dispatcher, log,
                    () => { activation.TryRequestOpen(); return Task.CompletedTask; },
                    () => { settingsWindow.Show(); return Task.CompletedTask; },
                    () => { support.Open(); return Task.CompletedTask; },
                    lifetime.RequestExitAsync);
                lifetime.AddStop("remove-tray", tray.RemoveForShutdown);
                lifetime.AddCleanup("close-tray", () => { tray.Dispose(); return Task.CompletedTask; });
                watchdog.AddEmergencyCleanup(tray.RemoveForShutdown);
                cancellation.ThrowIfCancellationRequested();
                activation.SetReady(runtime.OpenAsync);
            }, startupCancellation);
            if (lifetime.StartupFailure is not null)
                reportStartupMessage(startupMessage, strings.PluginTitle, MessageBoxImage.Error);
            return exitCode;
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
        Func<Task> hideOwnWindows,
        PluginLog log,
        ResourceRollbackScope rollback)
    {
        var hotkeyDispatcher = new StaDispatcher(HotkeyThreadName);
        rollback.Own(hotkeyDispatcher, hotkeyDispatcher.StopAsync);
        var hotkeyWindow = rollback.Replace(hotkeyDispatcher, new HotkeyWindow(hotkeyDispatcher, log));
        var registrar = new HotkeyRegistrar(hotkeyWindow, strings, log);
        var settings = new SettingsService(initialSettings, settingsStore.Save, registrar.TryApply, log);
        var environments = new WebViewEnvironmentFactory(paths, strings, notifier);
        var searchBrowserDispatcher = new StaDispatcher(SearchBrowserThreadName);
        rollback.Own(searchBrowserDispatcher, searchBrowserDispatcher.StopAsync);
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
        var translationDispatcher = new StaDispatcher("CircleToSearch image translation");
        rollback.Own(translationDispatcher, translationDispatcher.StopAsync);
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
        var runtime = rollback.TransferAllTo(new AppRuntime(coordinator, lifetime, settings));

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

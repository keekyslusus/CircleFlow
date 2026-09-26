using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
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
using CircleToSearch.Shell.Onboarding;
using CircleToSearch.Shell.SettingsPreview;
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

    internal sealed record OverlayControllerDependencies(
        Action<string> SetClipboard,
        Func<bool> AnimationsEnabled,
        Func<MouseEventArgs, Point>? PointerPosition,
        IOcrRecognizer OcrRecognizer,
        double TextHitToleranceDips,
        Func<bool> TranslationConsentAccepted,
        Action AcceptTranslationConsent,
        Action? ResetTranslationConsent,
        PluginLog? Log,
        TranslationMemoryProfiler? MemoryProfiler,
        Func<Uri, ITraceVideoPreview>? CreateTraceVideo,
        Func<bool> TraceTheme,
        OcrLanguageCatalog? OcrLanguages = null,
        Action<BitmapSource>? SetImageClipboard = null,
        Func<SelectionHint>? NextSelectionHint = null);

    public static int Run(string[] args) => Run(new AppPaths(),
        (message, title, icon) => MessageBox.Show(message, title, MessageBoxButton.OK, icon),
        autostart: args.Contains(WindowsStartupRegistration.AutostartArgument, StringComparer.OrdinalIgnoreCase));

    internal static int Run(AppPaths paths, Action<string, string, MessageBoxImage> reportStartupMessage,
        string? instanceName = null, CancellationToken startupCancellation = default, TimeSpan? activationTimeout = null,
        bool autostart = false)
    {
        var application = CreateApplication();
        // The log only writes once Data exists; creating it early lets the language report problems then.
        var log = new PluginLog(paths.LogsDirectory);
        UiLanguage language;
        UiStrings strings;
        try
        {
            language = new UiLanguage(LocalUiStrings.LoadEnglish(paths.LanguagesDirectory),
                new AppLanguageCatalog(paths.LanguagesDirectory), CultureInfo.CurrentUICulture, log);
            strings = new UiStrings(language.Get);
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
            instanceName ??= SingleInstanceCoordinator.CurrentSessionName;
            // A sign-in launch must not open a capture in an instance the user has already started.
            var launch = autostart
                ? new InstanceLaunchResult(SingleInstanceCoordinator.TryAcquire(instanceName), Activated: false)
                : SingleInstanceCoordinator.AcquireOrActivate(instanceName, activationTimeout);
            using var instance = launch.Instance;
            if (instance is null)
            {
                if (launch.Activated || autostart) return 0;
                reportStartupMessage(strings.ActivationFailed, strings.PluginTitle, MessageBoxImage.Error);
                return 1;
            }
            try { AppDataDirectory.Initialize(paths); }
            catch
            {
                reportStartupMessage(strings.StartupDataFailed(paths.DataDirectory), strings.PluginTitle, MessageBoxImage.Error);
                return 1;
            }
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
                // Messages before this point can only use the Windows display language.
                language.Apply(loaded.Settings.AppLanguageTag);
                startupMessage = strings.StartupFailed;
                if (loaded.ResetFields.Count != 0)
                    log.Warn(nameof(CompositionRoot), $"settings reset to defaults: {string.Join(", ", loaded.ResetFields)}");
                if (loaded.Recovered)
                    reportStartupMessage(strings.StorageRecovered, strings.PluginTitle, MessageBoxImage.Warning);
                _ = new BrowserDataCleanup(paths, log).Run(loaded.Settings.BrowserDataCleanupDays);
                cancellation.ThrowIfCancellationRequested();
                var notifications = new NotificationPresenter(application.Dispatcher, strings, log);
                lifetime.AddCleanup("close-notifications", () => { notifications.Dispose(); return Task.CompletedTask; });
                var notifier = new PluginNotifier(notifications.ShowMessage, notifications.ShowMessageWithButton,
                    notifications.ShowError, log);
                var urlOpening = new UrlOpeningService(OpenResultsUrl, notifier, strings, log);
                TrayIcon? tray = null;
                var rollback = new ResourceRollbackScope(log);
                lifetime.AddCleanup("stop-runtime", () => rollback.DisposeAsync().AsTask());
                var runtime = Create(paths, loaded.Settings, store, strings, notifier, urlOpening,
                    () => application.Dispatcher.InvokeAsync(() =>
                    {
                        tray?.CloseMenu();
                        notifications.CloseAll();
                    }).Task, log, rollback);
                lifetime.AddStop("stop-runtime-triggers", () => _ = runtime.StopAsync());
                cancellation.ThrowIfCancellationRequested();
                var support = new ProjectSupport(urlOpening);
                var startup = new WindowsStartupRegistration(paths.ExecutablePath, log);
                var onboardingModel = new OnboardingModel(runtime.Settings, startup, strings);
                var onboarding = new OnboardingWindowController(application.Dispatcher, onboardingModel,
                    () => new OnboardingWindowView(strings, SystemTheme.IsLight(), paths.TrayIconPath, onboardingModel).Window);
                lifetime.AddCleanup("close-onboarding", () => { onboarding.Dispose(); return Task.CompletedTask; });
                var settingsModel = new SettingsWindowModel(runtime.Settings, runtime.Providers, runtime.OcrLanguages,
                    language, CultureInfo.CurrentUICulture, support, urlOpening, paths, strings, WebViewEnvironmentFactory.RuntimeVersion,
                    AudioOutputDevice.DefaultName, startup, onboarding.Show);
                var settingsWindow = new SingleWindowController(application.Dispatcher,
                    () => new SettingsWindowView(strings, SystemTheme.IsLight(), paths.TrayIconPath, settingsModel).Window);
                lifetime.AddCleanup("close-settings", () => { settingsWindow.Dispose(); return Task.CompletedTask; });
                tray = new TrayIcon(paths.TrayIconPath, strings, application.Dispatcher, log,
                    () => { activation.TryRequestOpen(); return Task.CompletedTask; },
                    () => runtime.Settings.HotkeyStatus is { IsActive: true } hotkey ? hotkey.Gesture : null,
                    () => { settingsWindow.Show(); return Task.CompletedTask; },
                    () => { support.Open(); return Task.CompletedTask; },
                    lifetime.RequestExitAsync);
                lifetime.AddStop("remove-tray", tray.RemoveForShutdown);
                lifetime.AddCleanup("close-tray", () => { tray.Dispose(); return Task.CompletedTask; });
                watchdog.AddEmergencyCleanup(tray.RemoveForShutdown);
                cancellation.ThrowIfCancellationRequested();
                activation.SetReady(runtime.OpenAsync);
                onboarding.ShowIfNeeded();
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
        UrlOpeningService urlOpening,
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
            log,
            searchBrowserDispatcher,
            () => environments.CreateAsync(paths.SearchProfileDirectory, enableExtensions: true),
            (content, anchor, lightTheme) => CreateSearchBrowserWindowView(
                strings, content, anchor, lightTheme)));
        var visualSearchRollback = rollback.Own(new ResourceRollbackScope(log));
        var traceHttpClient = visualSearchRollback.Own(new HttpClient { Timeout = Timeout.InfiniteTimeSpan });
        var providerRouter = visualSearchRollback.Own(new VisualSearchProviderRouter(
            [
                new VisualSearchProviderRegistration(
                    new SearchProviderDescriptor(SearchProviderIds.GoogleLens, () => strings.GoogleLensProviderName),
                    () => new GoogleLensProvider(
                        jpeg => new GoogleLensBrowserOperation(jpeg, log))),
                new VisualSearchProviderRegistration(
                    new SearchProviderDescriptor(SearchProviderIds.YandexImages, () => strings.YandexImagesProviderName),
                    () => new YandexImagesProvider(log)),
                new VisualSearchProviderRegistration(
                    new SearchProviderDescriptor(SearchProviderIds.TraceMoe, () => strings.TraceMoeProviderName),
                    () => new TraceMoeProvider(traceHttpClient)),
            ],
            SearchProviderIds.GoogleLens,
            log));
        var visualSearchLifetime = visualSearchRollback.TransferAllTo(
            new VisualSearchLifetime(providerRouter.StopAsync, traceHttpClient, log));
        var visualSearchPresenter = new VisualSearchResultPresenter(
            searchBrowserHost,
            urlOpening,
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
        var imageAsk = new ImageAskWorkflow(
            (image, question) => new GoogleAiModeBrowserOperation(
                image, question, (jpeg, asked) => new GoogleLensBrowserOperation(jpeg, log, asked), log),
            ImageCropper.EncodeJpeg,
            visualSearchPresenter,
            strings,
            log);
        var lensPrewarm = new LensPrewarmWorkflow(
            image => new GoogleLensBrowserOperation(image, log),
            ImageCropper.EncodeJpeg,
            visualSearchPresenter,
            strings,
            log);
        var musicClock = new SystemMusicRecognitionClock();
        var musicRollback = rollback.Own(new ResourceRollbackScope(log));
        var musicThrottle = musicRollback.Own(new ShazamRequestThrottle(musicClock));
        var musicHttpClient = musicRollback.Own(new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan,
            DefaultRequestVersion = HttpVersion.Version11,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact,
        });
        var musicLifetime = musicRollback.TransferAllTo(
            new MusicRecognitionLifetime(musicThrottle, musicHttpClient, log));
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
        var musicResultPresenter = new MusicResultPresenter(urlOpening, notifier, strings);
        var providerSelection = new ProviderSelectionStore(
            providerRouter,
            settings,
            notifier,
            strings,
            log);
        var ocrLanguages = new OcrLanguageCatalog();
        var translationRollback = rollback.Own(new ResourceRollbackScope(log));
        var translationHttpClient = translationRollback.Own(new HttpClient(new SocketsHttpHandler
        { UseCookies = false, AutomaticDecompression = DecompressionMethods.All })
        {
            Timeout = Timeout.InfiniteTimeSpan,
            DefaultRequestVersion = HttpVersion.Version20,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
        });
        var translationMemory = TranslationMemoryProfilingEnabled ? new TranslationMemoryProfiler(paths.LogsDirectory) : null;
        if (translationMemory is not null) translationRollback.Own(translationMemory);
        translationMemory?.Mark("runtime_ready");
        var translationDispatcher = new StaDispatcher("CircleToSearch image translation");
        translationRollback.Own(translationDispatcher, translationDispatcher.StopAsync);
        var imageTranslationSigner = translationRollback.Replace(translationDispatcher, new GoogleImageTranslationSigner(
            translationHttpClient,
            translationDispatcher,
            () => environments.CreateAsync(paths.ImageTranslationProfileDirectory),
            translationMemory));
        var translationLifetime = translationRollback.TransferAllTo(
            new ScreenTranslationLifetime(imageTranslationSigner.StopAsync, translationHttpClient,
                translationMemory, log));
        var screenTranslation = new ScreenTranslationWorkflow(
            new GoogleImageTranslationProvider(translationHttpClient, imageTranslationSigner, translationMemory),
            log);
        var textSearch = new TextSearchWorkflow(
            new TextSearchUrlBuilder(),
            () => settings.Snapshot.TextSearchEngineId,
            () => settings.Snapshot.TextSearchInBuiltInBrowser,
            searchBrowserHost,
            urlOpening,
            notifier,
            strings,
            log);
        var overlayControllerDependencies = new OverlayControllerDependencies(
            Clipboard.SetText,
            OverlayVisualResources.AnimationsEnabled,
            PointerPosition: null,
            new WindowsOcrRecognizer(),
            TextHitToleranceDips: 3,
            () => settings.Snapshot.ImageTranslationPrivacyConsentAccepted,
            () => settings.SetTranslationConsent(true).ThrowIfFailed(strings.StorageSaveFailed),
            () => settings.SetTranslationConsent(false).ThrowIfFailed(strings.StorageSaveFailed),
            log,
            translationMemory,
            video => new TraceVideoPreview(video,
                () => environments.CreateAsync(paths.TraceVideoProfileDirectory), log),
            SystemTheme.IsLight,
            ocrLanguages,
            Clipboard.SetImage,
            new SelectionHintRotation().Next);
        var overlayControllerFactory = new OverlayControllerFactory(
            context => CreateOverlayControllers(context, overlayControllerDependencies));
        var overlayWindowFactory = new OverlayWindowFactory(overlayControllerFactory);
        var imageSave = new ImageSaveService(
            action => Application.Current.Dispatcher.InvokeAsync(action).Task, strings, notifier);
        var workflow = new OverlaySessionWorkflow(
            new OverlaySessionFactory(log, new PointerMonitorCapture(), overlayWindowFactory),
            visualSearch,
            (overlay, cancellation) => new OverlayMusicSession(
                overlay, musicRecognition, musicResultPresenter, log, cancellation),
            (overlay, cancellation) => new OverlayTranslationSession(
                overlay, screenTranslation, cancellation),
            (overlay, maxLongSidePx, cancellation) => new OverlayTraceSession(
                overlay, visualSearch, maxLongSidePx, urlOpening, cancellation),
            providerSelection,
            strings,
            log,
            textSearch,
            imageSave.SaveAsync,
            (maxLongSidePx, cancellation) => new OverlayAskSession(imageAsk, maxLongSidePx, cancellation),
            (maxLongSidePx, cancellation) => new OverlayLensSession(lensPrewarm, maxLongSidePx, cancellation),
            cancellation => new OverlayTextSearchSession(textSearch, cancellation));
        var coordinator = new SearchCoordinator(
            workflow,
            hideOwnWindows,
            () => SearchSessionOptions.From(settings.Snapshot, ocrLanguages, CultureInfo.CurrentUICulture,
                KeyboardInputLanguageSource.CaptureForeground()),
            notifier,
            strings,
            log);
        var lifetime = new PluginRuntimeLifetime(
            coordinator.StopAsync,
            hotkeyWindow.StopAsync,
            searchBrowserHost.StopAsync,
            musicLifetime.StopAsync,
            translationLifetime.StopAsync,
            visualSearchLifetime.StopAsync,
            log);
        var runtime = rollback.TransferAllTo(new AppRuntime(coordinator, lifetime, settings, providerSelection, ocrLanguages));

        hotkeyWindow.HotkeyPressed += () =>
        {
            try
            {
                // Registration waits on this dispatcher; reading settings here could deadlock its lock.
                _ = Task.Run(() =>
                {
                    if (!settings.Snapshot.IgnoreHotkeyInFullscreen || !FullscreenAppDetector.IsForegroundFullscreenApp())
                        return coordinator.StartFromHotkeyAsync();
                    log.Info(nameof(CompositionRoot), "hotkey ignored in a fullscreen app");
                    return Task.CompletedTask;
                });
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

    internal static SearchBrowserWindowView CreateSearchBrowserWindowView(
        UiStrings strings,
        FrameworkElement content,
        POINT anchor,
        bool lightTheme) => new(
            strings,
            content,
            lightTheme,
            loadingOverlayEnabled: false,
            window => new BottomResultsPanel(window, anchor));

    internal static OverlayControllers CreateOverlayControllers(
        OverlayControllerContext context,
        OverlayControllerDependencies dependencies)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentNullException.ThrowIfNull(dependencies.SetClipboard);
        ArgumentNullException.ThrowIfNull(dependencies.AnimationsEnabled);
        ArgumentNullException.ThrowIfNull(dependencies.OcrRecognizer);
        ArgumentNullException.ThrowIfNull(dependencies.TranslationConsentAccepted);
        ArgumentNullException.ThrowIfNull(dependencies.AcceptTranslationConsent);
        ArgumentNullException.ThrowIfNull(dependencies.TraceTheme);
        if (!double.IsFinite(dependencies.TextHitToleranceDips) || dependencies.TextHitToleranceDips < 0)
            throw new ArgumentOutOfRangeException(nameof(dependencies.TextHitToleranceDips));

        var rollback = new List<IDisposable>();
        T Track<T>(T resource) where T : IDisposable
        {
            rollback.Add(resource);
            return resource;
        }

        try
        {
            var activityPresenter = Track(new OverlayActivityPresenter(
                context.Visual.ActivityHost,
                dependencies.AnimationsEnabled));
            var toast = Track(new ToastOverlayController(
                context.Visual.Bottom,
                context.Visual.LightTheme,
                dependencies.AnimationsEnabled));
            var clipboardCopy = new ClipboardCopyService(
                dependencies.SetClipboard,
                toast.Show,
                context.Strings,
                dependencies.SetImageClipboard);
            var publishCommand = context.PublishCommand ?? (_ => { });
            void UpdateSearchIcons(string providerId)
            {
                var visual = context.Visual;
                visual.TextSelection.Toolbar.SetActionContent(
                    visual.TextSelection.SearchButton, context.Strings.TextSearch,
                    ProviderVisualCatalog.CreateTextSearchMark(providerId, context.TextSearchEngineId, visual.LightTheme));
                visual.ImageSelection.Toolbar.SetActionContent(
                    visual.ImageSelection.SearchButton, context.Strings.TextSearch,
                    ProviderVisualCatalog.CreateSearchMark(providerId, visual.LightTheme));
            }
            UpdateSearchIcons(context.SelectedProviderId);
            void ChangeTrayLayout(Action change) =>
                context.Visual.Bottom.TrayTransitions.Apply(change, dependencies.AnimationsEnabled());
            var provider = Track(new ProviderMenuController(
                context.Visual.Provider,
                context.Visual.Bottom.Root,
                context.Providers,
                context.SelectedProviderId,
                context.Strings,
                context.Visual.LightTheme,
                context.CanUseProvider,
                providerId =>
                {
                    UpdateSearchIcons(providerId);
                    context.ProviderSelected(providerId);
                },
                ChangeTrayLayout));
            var selectionHint = Track(new SelectionHintOverlayController(
                context.Visual.Actions.Hint
                    ?? throw new InvalidOperationException("The selection hint visual is missing."),
                context.Strings,
                dependencies.NextSelectionHint?.Invoke() ?? SelectionHint.EscapeCancel,
                ChangeTrayLayout,
                dependencies.AnimationsEnabled,
                context.CoordinateRoot.Dispatcher));
            var mapper = new OverlayCoordinateMapper(
                context.Scale,
                context.Overscan,
                context.Monitor.Size);
            var selection = Track(new SelectionOverlayController(
                context.Visual.Selection,
                context.CoordinateRoot,
                context.Monitor,
                context.Scale,
                context.Options.PaddingPx,
                context.Options.MinDiagonalPx,
                context.Overscan,
                context.CanAcceptPointerInput,
                context.CanStartSelection,
                context.SelectionStarted,
                context.SelectionCompleted,
                context.SelectionRejected,
                context.SelectionHoldCompleted,
                dependencies.PointerPosition,
                subscribeInput: false,
                selectionDrawn: context.SelectionDrawn));
            var textSelection = Track(new TextSelectionOverlayController(
                context.Visual.TextSelection,
                context.CoordinateRoot,
                context.Visual.Selection.InputSurface,
                mapper,
                new OcrTextHitTester(dependencies.TextHitToleranceDips * context.Scale),
                clipboardCopy,
                () => provider.SelectedProviderId,
                publishCommand,
                context.Strings,
                context.Visual.LightTheme,
                selectionHint.SetTextHovered));
            var frameSource = context.Visual.Selection.Screenshot.Source as BitmapSource
                ?? throw new InvalidOperationException("The overlay frame source is missing.");
            OverlayImageTextCoordinator? imageText = null;
            var ocr = Track(new OcrOverlayController(
                frameSource,
                context.CoordinateRoot.Dispatcher,
                dependencies.OcrRecognizer,
                context.OcrLanguageTag,
                outcome => imageText?.OnCompleted(outcome),
                dependencies.Log,
                dependencies.MemoryProfiler));
            var pointer = Track(new PointerGestureRouter(
                context.Visual.Selection,
                context.CoordinateRoot,
                selection,
                textSelection,
                context.CanAcceptPointerInput,
                context.CanStartSelection,
                dependencies.PointerPosition));
            var actionTray = Track(new ActionTrayOverlayController(
                context.Visual.Actions,
                context.Visual.Bottom.Root));
            imageText = Track(new OverlayImageTextCoordinator(
                pointer,
                textSelection,
                ocr,
                context.Visual.Actions.Prompt,
                context.Strings,
                context.OcrLanguageTag ?? context.InputLanguage.Tag,
                frameSource,
                dependencies.OcrLanguages,
                toast,
                activityPresenter,
                context.Visual.Effects,
                context.CoordinateRoot,
                context.Visual.LightTheme,
                restoreActionTray: actionTray.Restore,
                changeTrayLayout: ChangeTrayLayout));
            var inputLanguage = Track(new KeyboardInputLanguageSource(
                imageText.OnInputLanguageChanged,
                tag =>
                {
                    imageText.SetInitialInputLanguage(tag);
                    imageText.Start();
                }));
            var translation = Track(new ScreenTranslationOverlayController(
                context.Visual.TranslationAction,
                activityPresenter,
                context.Visual.TranslationOverlay,
                context.Visual.Bottom,
                context.Visual.Effects,
                context.CoordinateRoot,
                context.Strings,
                dependencies.TranslationConsentAccepted,
                dependencies.AcceptTranslationConsent,
                () => context.TranslationTargetLanguageTag,
                publishCommand,
                context.TransitionMode,
                toast.Show,
                dependencies.AnimationsEnabled,
                context.Visual.LightTheme,
                context.Visual.Selection.Screenshot,
                imageText.OnImageChanged,
                dependencies.MemoryProfiler));
            var debug = Track(new DebugOverlayController(
                context.Visual.Debug,
                context.Visual.LightTheme,
                context.PublishCommand is not null,
                context.GetMode,
                context.DebugScenarioSelected,
                toast.Show,
                dependencies.ResetTranslationConsent,
                context.Strings));
            var imageSelection = Track(new ImageSelectionOverlayController(
                context.Visual.ImageSelection,
                context.CoordinateRoot,
                mapper,
                selection,
                translation,
                clipboardCopy,
                () => (BitmapSource)context.Visual.Selection.Screenshot.Source,
                context.CreateSelectionCopy,
                context.SelectionCompleted,
                publishCommand,
                context.CloseRequested ?? (() => publishCommand(new CancelSession())),
                context.Strings,
                context.HiddenToolbarActions));
            var music = Track(new MusicOverlayController(
                context.Visual.Music,
                activityPresenter,
                context.Visual.Bottom.LayoutTransitions,
                context.Visual.Effects,
                context.Visual.Root,
                context.Strings,
                context.Visual.LightTheme,
                context.GetMode,
                context.MusicStartRequested,
                context.MusicCancelRequested,
                context.MusicResultCommandRequested,
                clipboardCopy,
                dependencies.AnimationsEnabled));
            var trace = Track(new TraceOverlayController(
                context.Visual.Root,
                activityPresenter,
                context.Visual.Bottom,
                context.Visual.Effects,
                context.Strings,
                dependencies.TraceTheme,
                clipboardCopy,
                context.GetMode,
                context.TransitionMode,
                context.PublishCommand,
                context.CreateSelectionCopy,
                dependencies.CreateTraceVideo));
            var controllers = new OverlayControllers(
                selection,
                textSelection,
                pointer,
                imageText,
                inputLanguage,
                ocr,
                translation,
                provider,
                music,
                trace,
                actionTray,
                toast,
                debug,
                activityPresenter,
                imageSelection,
                selectionHint);
            rollback.Clear();
            return controllers;
        }
        catch
        {
            for (var index = rollback.Count - 1; index >= 0; index--)
            {
                try { rollback[index].Dispose(); }
                catch (Exception exception)
                {
                    dependencies.Log?.SafeError(
                        nameof(CompositionRoot),
                        "rollback-overlay-controller",
                        exception);
                }
            }
            throw;
        }
    }

    private static bool OpenResultsUrl(string url)
    {
        // Null only means the target was handed to an already running process, e.g. ms-settings: URIs.
        using var process = Process.Start(new ProcessStartInfo
        {
            UseShellExecute = true,
            FileName = url,
        });
        return true;
    }
}

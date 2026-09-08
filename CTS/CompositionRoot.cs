using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows.Controls;
using System.Windows;
using Flow.Launcher.Plugin;
using CircleToSearch.Capture;
using CircleToSearch.Capture.OverlayInteractions;
using CircleToSearch.Interop;
using CircleToSearch.MusicRecognition;
using CircleToSearch.MusicRecognition.Audio;
using CircleToSearch.MusicRecognition.Shazam;
using CircleToSearch.Search;
using CircleToSearch.Search.Browser;
using CircleToSearch.Settings;
using CircleToSearch.Trigger;
using CircleToSearch.Ui;
using CircleToSearch.TextRecognition;
using CircleToSearch.Translation;
using System.Globalization;
using GdiBitmap = System.Drawing.Bitmap;
using GdiRectangle = System.Drawing.Rectangle;

namespace CircleToSearch;

public static class CompositionRoot
{
    internal const string HotkeyThreadName = "CircleToSearch hotkey";
    internal const string SearchBrowserThreadName = "CircleToSearch WebView2";

    public static UiStrings CreateUiStrings(PluginInitContext context) =>
        new(context.API.GetTranslation);

    public static PluginRuntime Create(PluginInitContext context, UiStrings strings)
    {
        var api = context.API;
        var pluginDirectory = context.CurrentPluginMetadata.PluginDirectory;
        var log = new PluginLog(pluginDirectory);
        var settings = api.LoadSettingJsonStorage<PluginSettings>();
        var dataDirectory = api.GetDataDirectory();
        if (string.IsNullOrWhiteSpace(dataDirectory)) dataDirectory = pluginDirectory;
        var iconPath = Path.Combine(pluginDirectory, "Images", "app.png");
        var webView2Version = SearchBrowserHost.GetRuntimeVersion(pluginDirectory);
        log.Info(nameof(CompositionRoot), webView2Version is null
            ? "WebView2 Runtime was not detected"
            : $"WebView2 Runtime detected: {webView2Version}");

        var notifier = new PluginNotifier(
            (title, message) => api.ShowMsg(title, message, iconPath),
            (title, message, button, action) =>
                api.ShowMsgWithButton(title, button, action, message, iconPath),
            (title, message) => api.ShowMsgError(title, message),
            log);
        var searchBrowserHost = new SearchBrowserHost(
            pluginDirectory,
            Path.Combine(dataDirectory, "WebView2Profile"),
            strings,
            log,
            new StaDispatcher(SearchBrowserThreadName));
        var traceHttpClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        var providerRouter = new VisualSearchProviderRouter(
            [
                new VisualSearchProviderRegistration(
                    new SearchProviderDescriptor(SearchProviderIds.GoogleLens, strings.GoogleLensProviderName),
                    () => new GoogleLensProvider(
                        png => new GoogleLensBrowserOperation(png, log))),
                new VisualSearchProviderRegistration(
                    new SearchProviderDescriptor(SearchProviderIds.YandexImages, strings.YandexImagesProviderName),
                    () => new YandexImagesProvider(log)),
                new VisualSearchProviderRegistration(
                    new SearchProviderDescriptor(SearchProviderIds.TraceMoe, strings.TraceMoeProviderName),
                    () => new TraceMoeProvider(traceHttpClient)),
            ],
            SearchProviderIds.GoogleLens,
            log);
        var visualSearchPresenter = new VisualSearchResultPresenter(
            searchBrowserHost,
            OpenResultsUrl,
            notifier,
            strings,
            log);
        var visualSearch = new VisualSearchWorkflow(
            providerRouter,
            (frame, bounds) => ImageCropper.Encode(frame, bounds, settings.MaxLongSidePx),
            visualSearchPresenter,
            notifier,
            strings,
            log);
        var musicClock = new SystemMusicRecognitionClock();
        var musicThrottle = new ShazamRequestThrottle(musicClock);
        var musicHttpClient = new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan,
            DefaultRequestVersion = HttpVersion.Version11,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact,
        };
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
            () => api.SaveSettingJsonStorage<PluginSettings>(),
            notifier,
            strings,
            log);
        var ocrLanguages = new OcrLanguageCatalog();
        var translationHttpClient = new HttpClient(new SocketsHttpHandler
        { UseCookies = false, AutomaticDecompression = DecompressionMethods.All })
        {
            Timeout = Timeout.InfiniteTimeSpan,
            DefaultRequestVersion = HttpVersion.Version20,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
        };
        var imageTranslationSigner = new GoogleImageTranslationSigner(translationHttpClient,
            new StaDispatcher("CircleToSearch image translation"), Path.Combine(dataDirectory, "ImageTranslationProfile"));
        var screenTranslation = new ScreenTranslationWorkflow(new GoogleImageTranslationProvider(translationHttpClient, imageTranslationSigner));
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
            ocrLanguageTag: () => ocrLanguages.Validate(settings.OcrLanguageTag),
            targetLanguageTag: () =>
            {
                if (!string.IsNullOrWhiteSpace(settings.TranslationTargetLanguageTag))
                    return settings.TranslationTargetLanguageTag;
                return string.IsNullOrWhiteSpace(CultureInfo.CurrentUICulture.Name)
                    ? "en"
                    : CultureInfo.CurrentUICulture.Name;
            },
            translationConsentAccepted: () => settings.ImageTranslationPrivacyConsentAccepted,
            acceptTranslationConsent: () =>
            {
                settings.ImageTranslationPrivacyConsentAccepted = true;
                api.SaveSettingJsonStorage<PluginSettings>();
            },
            log: log);
        var overlayWindowFactory = new OverlayWindowFactory(overlayControllerFactory,
            video => new TraceVideoPreview(video,
                () => Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(
                    userDataFolder: Path.Combine(dataDirectory, "TraceVideoProfile")), log));
        var workflow = new OverlaySessionWorkflow(
            new OverlaySessionFactory(log, new PointerMonitorCapture(), overlayWindowFactory),
            visualSearch,
            musicRecognition,
            musicResultPresenter,
            providerSelection,
            settings,
            strings,
            log,
            textSearch,
            screenTranslation,
            OpenResultsUrl);
        var coordinator = new SearchCoordinator(
            workflow,
            () => api.HideMainWindow(),
            settings,
            notifier,
            strings,
            log);
        var hotkeyWindow = new HotkeyWindow(new StaDispatcher(HotkeyThreadName), log);
        var registrar = new HotkeyRegistrar(hotkeyWindow, strings, log);
        var queryTrigger = new QueryTrigger(coordinator, iconPath, registrar.DescribeStatus, strings);

        hotkeyWindow.HotkeyPressed += () =>
        {
            try
            {
                _ = coordinator.StartFromHotkeyAsync();
            }
            catch (Exception exception)
            {
                log.Error(nameof(CompositionRoot), "dispatching the hotkey failed", exception);
            }
        };

        if (!registrar.TryApply(settings.HotkeyGesture))
            log.Warn(nameof(CompositionRoot), $"hotkey '{settings.HotkeyGesture}' is not active");

        return new PluginRuntime(
            coordinator,
            queryTrigger,
            () => new SettingsPanel(
                settings,
                registrar.TryApply,
                api.SaveSettingJsonStorage<PluginSettings>,
                webView2Version,
                providerRouter.GetEffectiveDescriptor(settings.SearchProviderId).DisplayName,
                strings,
                ocrLanguages.AvailableLanguages),
            hotkeyWindow,
            providerRouter,
            searchBrowserHost,
            [musicHttpClient, musicThrottle, imageTranslationSigner, translationHttpClient, traceHttpClient],
            log);
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

public sealed class PluginRuntime : IDisposable
{
    private readonly SearchCoordinator _coordinator;
    private readonly HotkeyWindow _hotkeyWindow;
    private readonly VisualSearchProviderRouter _providerRouter;
    private readonly SearchBrowserHost _searchBrowserHost;
    private readonly IReadOnlyList<IDisposable> _musicResources;
    private readonly PluginLog _log;

    public PluginRuntime(
        SearchCoordinator coordinator,
        QueryTrigger queryTrigger,
        Func<Control> createSettingPanel,
        HotkeyWindow hotkeyWindow,
        VisualSearchProviderRouter providerRouter,
        SearchBrowserHost searchBrowserHost,
        IReadOnlyList<IDisposable> musicResources,
        PluginLog log)
    {
        Coordinator = coordinator;
        QueryTrigger = queryTrigger;
        CreateSettingPanel = createSettingPanel;
        _coordinator = coordinator;
        _hotkeyWindow = hotkeyWindow;
        _providerRouter = providerRouter;
        _searchBrowserHost = searchBrowserHost;
        _musicResources = musicResources;
        _log = log;
    }

    public SearchCoordinator Coordinator { get; }

    public QueryTrigger QueryTrigger { get; }

    public Func<Control> CreateSettingPanel { get; }

    public void Dispose()
    {
        // logged so it is visible whether the host disposes the plugin on "Reload plugin data";
        // if this line never appears, every reload leaks a hotkey hook and an STA thread
        _log.Info(nameof(PluginRuntime), "disposing: canceling the active session and unregistering the hotkey");
        try
        {
            _coordinator.CancelActiveSession()
                .WaitAsync(TimeSpan.FromSeconds(2))
                .GetAwaiter()
                .GetResult();
        }
        catch (TimeoutException)
        {
            _log.Warn(nameof(PluginRuntime), "active session did not stop within the shutdown timeout");
        }
        foreach (var resource in _musicResources) resource.Dispose();
        _hotkeyWindow.Dispose();
        _providerRouter.Dispose();
        _searchBrowserHost.Dispose();
    }
}

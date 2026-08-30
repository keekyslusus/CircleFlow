using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows.Controls;
using Flow.Launcher.Plugin;
using CircleToSearch.Capture;
using CircleToSearch.Interop;
using CircleToSearch.MusicRecognition;
using CircleToSearch.MusicRecognition.Audio;
using CircleToSearch.MusicRecognition.Shazam;
using CircleToSearch.Search;
using CircleToSearch.Settings;
using CircleToSearch.Trigger;
using CircleToSearch.Ui;
using GdiBitmap = System.Drawing.Bitmap;
using GdiRectangle = System.Drawing.Rectangle;

namespace CircleToSearch;

public static class CompositionRoot
{
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
        var webView2Version = GoogleLensWindow.GetRuntimeVersion(pluginDirectory);
        log.Info(nameof(CompositionRoot), webView2Version is null
            ? "WebView2 Runtime was not detected"
            : $"WebView2 Runtime detected: {webView2Version}");

        var hotkeyWindow = new HotkeyWindow(log);
        var registrar = new HotkeyRegistrar(hotkeyWindow, strings, log);
        var musicClock = new SystemMusicRecognitionClock();
        var musicThrottle = new ShazamRequestThrottle(musicClock);
        var musicHttpClient = new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan,
            DefaultRequestVersion = HttpVersion.Version11,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact,
        };
        var shazamClient = new ShazamClient(musicHttpClient, new PixelUserAgentProvider());
        log.Info(nameof(ShazamClient), $"using User-Agent: {shazamClient.UserAgent}");
        var musicRecognizer = new ProgressiveMusicRecognizer(
            new LoopbackCaptureSessionFactory(log),
            shazamClient,
            musicThrottle,
            musicClock,
            log);
        var musicSimulator = new MusicRecognitionSimulator(strings);
        var providerRouter = new VisualSearchProviderRouter(
            [
                new VisualSearchProviderRegistration(
                    new SearchProviderDescriptor(SearchProviderIds.GoogleLens, strings.GoogleLensProviderName),
                    () => new GoogleLensProvider(new GoogleLensWindow(
                        pluginDirectory,
                        Path.Combine(dataDirectory, "WebView2Profile"),
                        strings,
                        log))),
                new VisualSearchProviderRegistration(
                    new SearchProviderDescriptor(SearchProviderIds.YandexImages, strings.YandexImagesProviderName),
                    () => new YandexImagesProvider(log)),
            ],
            SearchProviderIds.GoogleLens,
            log);
        var coordinator = new SearchCoordinator(
            providerRouter,
            new OverlaySessionFactory(log),
            (frame, bounds) => ImageCropper.Encode(frame, bounds, settings.MaxLongSidePx),
            musicRecognizer,
            musicSimulator,
            OpenResultsUrl,
            () => api.HideMainWindow(),
            (title, message) => api.ShowMsg(title, message, iconPath),
            (title, message, button, action) =>
                api.ShowMsgWithButton(title, button, action, message, iconPath),
            (title, message) => api.ShowMsgError(title, message),
            () => api.SaveSettingJsonStorage<PluginSettings>(),
            settings,
            strings,
            log);
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
                strings),
            hotkeyWindow,
            providerRouter,
            [musicHttpClient, musicThrottle],
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
    private readonly IReadOnlyList<IDisposable> _musicResources;
    private readonly PluginLog _log;

    public PluginRuntime(
        SearchCoordinator coordinator,
        QueryTrigger queryTrigger,
        Func<Control> createSettingPanel,
        HotkeyWindow hotkeyWindow,
        VisualSearchProviderRouter providerRouter,
        IReadOnlyList<IDisposable> musicResources,
        PluginLog log)
    {
        Coordinator = coordinator;
        QueryTrigger = queryTrigger;
        CreateSettingPanel = createSettingPanel;
        _coordinator = coordinator;
        _hotkeyWindow = hotkeyWindow;
        _providerRouter = providerRouter;
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
    }
}

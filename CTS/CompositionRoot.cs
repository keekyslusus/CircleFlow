using System.Diagnostics;
using System.IO;
using System.Windows.Controls;
using Flow.Launcher.Plugin;
using CircleToSearch.Capture;
using CircleToSearch.Interop;
using CircleToSearch.Search;
using CircleToSearch.Settings;
using CircleToSearch.Trigger;
using GdiBitmap = System.Drawing.Bitmap;
using GdiRectangle = System.Drawing.Rectangle;

namespace CircleToSearch;

public static class CompositionRoot
{
    public static PluginRuntime Create(PluginInitContext context)
    {
        var api = context.API;
        var pluginDirectory = context.CurrentPluginMetadata.PluginDirectory;
        var log = new PluginLog(pluginDirectory);
        var settings = api.LoadSettingJsonStorage<PluginSettings>();
        var dataDirectory = api.GetDataDirectory();
        if (string.IsNullOrWhiteSpace(dataDirectory)) dataDirectory = pluginDirectory;
        var iconPath = Path.Combine(pluginDirectory, "Images", "app.png");

        var hotkeyWindow = new HotkeyWindow(log);
        var registrar = new HotkeyRegistrar(hotkeyWindow, log);
        var clipboardDispatcher = new StaDispatcher("CircleToSearch clipboard");
        var pasteInjector = new BrowserPasteInjector(log);
        var sessions = new LensSessionManager(
            new LensSessionStore(dataDirectory),
            () => WebView2SessionFarmer.FarmAsync(
                pluginDirectory,
                Path.Combine(dataDirectory, "WebView2Profile"),
                log,
                TimeSpan.FromSeconds(20)),
            log);
        var uploadProvider = new GoogleLensProvider(sessions, log);
        var pasteProvider = new LensPasteProvider(
            png => Task.Run(() => ClipboardImageService.TryCopy(clipboardDispatcher, png)),
            OpenLens,
            pasteInjector.RunWatchAsync,
            pasteInjector,
            log);
        IVisualSearchProvider provider = settings.SearchMode switch
        {
            PluginSettings.UploadMode => uploadProvider,
            PluginSettings.PasteMode => pasteProvider,
            _ => new FallbackVisualSearchProvider(uploadProvider, pasteProvider),
        };
        var coordinator = new SearchCoordinator(
            provider,
            cancel => OverlayWindow.SelectAsync(
                log,
                new OverlayOptions(settings.PaddingPx, settings.LassoMinDiagonalPx),
                cancel),
            (frame, bounds) => ImageCropper.Encode(frame, bounds, settings.MaxLongSidePx),
            OpenResultsUrl,
            () => api.HideMainWindow(),
            (title, message) => api.ShowMsgError(title, message),
            settings,
            log);
        var queryTrigger = new QueryTrigger(coordinator, iconPath, registrar.DescribeStatus);

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
            () => new SettingsPanel(settings, registrar.TryApply, api.SaveSettingJsonStorage<PluginSettings>),
            hotkeyWindow,
            clipboardDispatcher,
            log);
    }

    private static Process? OpenLens(string url)
    {
        try
        {
            return Process.Start(new ProcessStartInfo
            {
                UseShellExecute = true,
                FileName = url,
            });
        }
        catch
        {
            return null;
        }
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
    private readonly StaDispatcher _clipboardDispatcher;
    private readonly PluginLog _log;

    public PluginRuntime(
        SearchCoordinator coordinator,
        QueryTrigger queryTrigger,
        Func<Control> createSettingPanel,
        HotkeyWindow hotkeyWindow,
        StaDispatcher clipboardDispatcher,
        PluginLog log)
    {
        Coordinator = coordinator;
        QueryTrigger = queryTrigger;
        CreateSettingPanel = createSettingPanel;
        _coordinator = coordinator;
        _hotkeyWindow = hotkeyWindow;
        _clipboardDispatcher = clipboardDispatcher;
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
        _coordinator.CancelActiveSelection();
        _hotkeyWindow.Dispose();
        _clipboardDispatcher.Dispose();
    }
}

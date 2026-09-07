using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CircleToSearch.Interop;
using CircleToSearch.Ui;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using DrawingColor = System.Drawing.Color;
using Ellipse = System.Windows.Shapes.Ellipse;

namespace CircleToSearch.Search.Browser;

public sealed class SearchBrowserHost : ISearchBrowserHost, IDisposable
{
    private static readonly bool LoadingOverlayEnabled = false;
    private static readonly TimeSpan NavigationTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(2);

    private readonly string _pluginDirectory;
    private readonly string _userDataFolder;
    private readonly UiStrings _strings;
    private readonly PluginLog _log;
    private readonly IStaDispatcher _dispatcher;
    private readonly Func<Task<CoreWebView2Environment>> _createEnvironment;
    private readonly TimeSpan _shutdownTimeout;
    private readonly SemaphoreSlim _showGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _lifecycleGate = new();
    private CoreWebView2Environment? _environment;
    private Window? _window;
    private BottomResultsPanel? _resultsPanel;
    private WebView2? _webView;
    private CancellationTokenSource? _windowLifetime;
    private Grid? _loadingOverlay;
    private TextBlock? _loadingText;
    private TextBlock? _titleText;
    private Button? _closeButton;
    private int _loadingGeneration;
    private long _showGeneration;
    private SearchBrowserUiOperation? _activeOperation;
    private int _dispatcherDisposeStarted;
    private int _disposed;

    internal SearchBrowserHost(
        string pluginDirectory,
        string userDataFolder,
        UiStrings strings,
        PluginLog log,
        IStaDispatcher dispatcher,
        Func<Task<CoreWebView2Environment>>? createEnvironment = null,
        TimeSpan? shutdownTimeout = null)
    {
        _pluginDirectory = pluginDirectory;
        _userDataFolder = userDataFolder;
        _strings = strings;
        _log = log;
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _createEnvironment = createEnvironment ?? (() => CoreWebView2Environment.CreateAsync(
            userDataFolder: _userDataFolder,
            options: new CoreWebView2EnvironmentOptions { AreBrowserExtensionsEnabled = true }));
        _shutdownTimeout = shutdownTimeout ?? ShutdownTimeout;
        if (_shutdownTimeout < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(shutdownTimeout));
    }

    public static string? GetRuntimeVersion(string pluginDirectory)
    {
        try
        {
            NativeLibrary.TryLoad(Path.Combine(pluginDirectory, "WebView2Loader.dll"), out _);
            return CoreWebView2Environment.GetAvailableBrowserVersionString();
        }
        catch
        {
            return null;
        }
    }

    public async Task<SearchBrowserShowResult> ShowAsync(
        SearchProviderDescriptor descriptor,
        PreparedVisualSearch preparedSearch,
        CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(preparedSearch);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancel, _lifetime.Token);
        SearchBrowserUiOperation? activeOperation = null;
        try
        {
            await _showGate.WaitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new SearchBrowserShowResult(SearchBrowserShowStatus.Canceled);
        }

        try
        {
            if (linked.IsCancellationRequested)
                return new SearchBrowserShowResult(SearchBrowserShowStatus.Canceled);

            var operation = new SearchBrowserUiOperation();
            activeOperation = operation;
            lock (_lifecycleGate)
            {
                if (_disposed != 0)
                    return new SearchBrowserShowResult(SearchBrowserShowStatus.Canceled);
                _activeOperation = operation;
            }
            NativeMethods.GetCursorPos(out var anchor);
            if (!_dispatcher.TryPost(() =>
                {
                    if (operation.TryStart())
                        _ = ShowOnUiThreadAsync(descriptor, preparedSearch, linked.Token, operation, anchor);
                }))
            {
                operation.CancelBeforeStart();
                return new SearchBrowserShowResult(
                    _lifetime.IsCancellationRequested
                        ? SearchBrowserShowStatus.Canceled
                        : SearchBrowserShowStatus.InitializationFailed);
            }

            var result = await operation.Result.ConfigureAwait(false);
            return _lifetime.IsCancellationRequested
                ? new SearchBrowserShowResult(SearchBrowserShowStatus.Canceled)
                : result;
        }
        finally
        {
            if (activeOperation is not null)
            {
                lock (_lifecycleGate)
                {
                    if (ReferenceEquals(_activeOperation, activeOperation))
                        _activeOperation = null;
                }
            }
            _showGate.Release();
        }
    }

    private async Task ShowOnUiThreadAsync(
        SearchProviderDescriptor descriptor,
        PreparedVisualSearch preparedSearch,
        CancellationToken cancel,
        SearchBrowserUiOperation operation,
        POINT anchor)
    {
        var generation = ++_showGeneration;
        try
        {
            try
            {
                cancel.ThrowIfCancellationRequested();
                await EnsureWindowAsync(anchor, descriptor, cancel).ConfigureAwait(true);
                cancel.ThrowIfCancellationRequested();
            }
            catch (WebView2RuntimeNotFoundException exception)
            {
                _log.Error(nameof(SearchBrowserHost), "WebView2 Runtime is unavailable", exception);
                CloseFailedShow();
                operation.SetOutcome(new SearchBrowserShowResult(
                    SearchBrowserShowStatus.RuntimeUnavailable));
                return;
            }
            catch (OperationCanceledException)
            {
                operation.SetOutcome(new SearchBrowserShowResult(SearchBrowserShowStatus.Canceled));
                return;
            }
            catch (Exception exception)
            {
                _log.Error(nameof(SearchBrowserHost), "initializing the search browser failed", exception);
                CloseFailedShow();
                operation.SetOutcome(new SearchBrowserShowResult(
                    SearchBrowserShowStatus.InitializationFailed));
                return;
            }

            var window = _window!;
            var webView = _webView!;
            using var showLifetime = CancellationTokenSource.CreateLinkedTokenSource(
                cancel,
                _lifetime.Token,
                _windowLifetime!.Token);
            ApplyProviderText(descriptor);
            ApplyTheme(SystemTheme.IsLight());
            ShowLoadingOverlay();
            webView.CoreWebView2.Stop();
            window.Show();
            window.Activate();

            var session = new WebView2VisualSearchBrowserSession(
                webView,
                _environment!,
                () => generation == _showGeneration &&
                      ReferenceEquals(_window, window) &&
                      !showLifetime.IsCancellationRequested);
            SearchBrowserShowStatus status;
            try
            {
                if (preparedSearch.Kind == PreparedVisualSearchKind.Url)
                {
                    var navigation = await session.NavigateAsync(
                            preparedSearch.RequireResultsUrl(),
                            NavigationTimeout,
                            showLifetime.Token)
                        .ConfigureAwait(true);
                    status = navigation.Status switch
                    {
                        BrowserNavigationStatus.Succeeded => SearchBrowserShowStatus.Shown,
                        BrowserNavigationStatus.Canceled => SearchBrowserShowStatus.Canceled,
                        _ => SearchBrowserShowStatus.NavigationFailed,
                    };
                }
                else
                {
                    var browserOperationStatus = await preparedSearch.RequireBrowserOperation()
                        .ExecuteAsync(session, showLifetime.Token)
                        .ConfigureAwait(true);
                    status = browserOperationStatus switch
                    {
                        VisualSearchBrowserOperationStatus.Succeeded => SearchBrowserShowStatus.Shown,
                        VisualSearchBrowserOperationStatus.Canceled => SearchBrowserShowStatus.Canceled,
                        _ => SearchBrowserShowStatus.ProviderOperationFailed,
                    };
                }
            }
            catch (OperationCanceledException)
            {
                status = SearchBrowserShowStatus.Canceled;
            }
            catch (Exception exception)
            {
                var operationName = preparedSearch.Kind == PreparedVisualSearchKind.Url
                    ? "initial search navigation"
                    : "provider browser operation";
                _log.Error(nameof(SearchBrowserHost), $"{operationName} failed", exception);
                status = preparedSearch.Kind == PreparedVisualSearchKind.Url
                    ? SearchBrowserShowStatus.NavigationFailed
                    : SearchBrowserShowStatus.ProviderOperationFailed;
            }

            if (status == SearchBrowserShowStatus.Shown)
                HideLoadingOverlay();
            else if (status != SearchBrowserShowStatus.Canceled || ReferenceEquals(_window, window))
                CloseFailedShow();
            operation.SetOutcome(new SearchBrowserShowResult(status));
        }
        catch (Exception exception)
        {
            _log.Error(nameof(SearchBrowserHost), "completing the search browser operation failed", exception);
            CloseFailedShow();
            operation.SetOutcome(new SearchBrowserShowResult(
                cancel.IsCancellationRequested
                    ? SearchBrowserShowStatus.Canceled
                    : SearchBrowserShowStatus.InitializationFailed));
        }
        finally
        {
            operation.Finish();
        }
    }

    private async Task EnsureWindowAsync(
        POINT anchor,
        SearchProviderDescriptor descriptor,
        CancellationToken cancel)
    {
        if (_window is not null && _webView is not null)
        {
            var existingWindow = _window;
            ApplyProviderText(descriptor);
            _resultsPanel?.MoveTo(anchor);
            if (_resultsPanel is not null) await _resultsPanel.ShowAsync().ConfigureAwait(true);
            cancel.ThrowIfCancellationRequested();
            if (!ReferenceEquals(_window, existingWindow)) throw new OperationCanceledException();
            return;
        }

        NativeLibrary.TryLoad(Path.Combine(_pluginDirectory, "WebView2Loader.dll"), out _);
        Directory.CreateDirectory(_userDataFolder);
        _environment ??= await _createEnvironment().ConfigureAwait(true);
        cancel.ThrowIfCancellationRequested();

        var lightTheme = SystemTheme.IsLight();
        var palette = PluginPalette.For(lightTheme);
        var background = Frozen(palette.WindowSurface);
        var webViewBackground = ToDrawingColor(palette.WindowSurface);
        var webView = new WebView2
        {
            Visibility = LoadingOverlayEnabled ? Visibility.Hidden : Visibility.Visible,
            DefaultBackgroundColor = webViewBackground,
        };
        var content = new Grid { Background = background };
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition());
        Grid.SetRow(webView, 1);
        content.Children.Add(webView);
        Grid? loadingOverlay = null;
        TextBlock? loadingText = null;
        if (LoadingOverlayEnabled)
        {
            loadingOverlay = CreateLoadingOverlay(palette, out loadingText);
            Grid.SetRow(loadingOverlay, 1);
            content.Children.Add(loadingOverlay);
        }

        var closed = false;
        var windowLifetime = new CancellationTokenSource();
        var window = new Window
        {
            Title = _strings.SearchBrowserWindowTitle(descriptor.DisplayName),
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Background = background,
            Content = content,
        };
        var header = new DockPanel { Margin = new Thickness(16, 8, 12, 8) };
        var close = new Button
        {
            ToolTip = _strings.Close,
            Width = 32,
            Height = 28,
            Padding = new Thickness(0),
            Background = background,
            Foreground = Frozen(palette.PrimaryText),
            BorderThickness = new Thickness(0),
        };
        var closeIcon = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M 0,0 L 10,10 M 10,0 L 0,10"),
            Width = 10,
            Height = 10,
            StrokeThickness = 1.5,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        closeIcon.SetBinding(
            System.Windows.Shapes.Shape.StrokeProperty,
            new Binding(nameof(Control.Foreground)) { Source = close });
        close.Content = closeIcon;
        AutomationProperties.SetName(close, _strings.Close);
        close.Click += (_, _) => window.Close();
        _closeButton = close;
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        _titleText = new TextBlock
        {
            Text = _strings.SearchBrowserWindowTitle(descriptor.DisplayName),
            Foreground = Frozen(palette.PrimaryText),
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 14,
        };
        header.Children.Add(_titleText);
        content.Children.Add(header);
        _resultsPanel = new BottomResultsPanel(window, anchor);
        window.SourceInitialized += (_, _) => ApplyWindowChromeTheme(window, lightTheme);
        window.Closed += (_, _) =>
        {
            closed = true;
            windowLifetime.Cancel();
            webView.Dispose();
            if (ReferenceEquals(_window, window))
            {
                _showGeneration++;
                _window = null;
                _resultsPanel = null;
                _webView = null;
                _windowLifetime = null;
                _loadingOverlay = null;
                _loadingText = null;
                _titleText = null;
                _closeButton = null;
            }
            windowLifetime.Dispose();
        };
        window.PreviewKeyDown += OnWindowPreviewKeyDown;

        _window = window;
        _webView = webView;
        _windowLifetime = windowLifetime;
        _loadingOverlay = loadingOverlay;
        _loadingText = loadingText;

        try
        {
            await _resultsPanel.ShowAsync().ConfigureAwait(true);
            cancel.ThrowIfCancellationRequested();
            if (closed) throw new OperationCanceledException();
            var controllerOptions = _environment.CreateCoreWebView2ControllerOptions();
            controllerOptions.DefaultBackgroundColor = webViewBackground;
            await webView.EnsureCoreWebView2Async(_environment, controllerOptions).ConfigureAwait(true);
            cancel.ThrowIfCancellationRequested();
            if (closed) throw new OperationCanceledException();
            await webView.CoreWebView2
                .AddScriptToExecuteOnDocumentCreatedAsync(OverlayScrollbarScript.Create())
                .ConfigureAwait(true);
            cancel.ThrowIfCancellationRequested();
            if (closed) throw new OperationCanceledException();
            var extensionDirectory = await Task.Run(() =>
                SearchBrowserExtension.Prepare(_pluginDirectory, _userDataFolder), cancel).ConfigureAwait(true);
            if (closed) throw new OperationCanceledException();
            var extension = await webView.CoreWebView2.Profile
                .AddBrowserExtensionAsync(extensionDirectory).ConfigureAwait(true);
            cancel.ThrowIfCancellationRequested();
            if (closed) throw new OperationCanceledException();
            if (!extension.IsEnabled) await extension.EnableAsync(true).ConfigureAwait(true);
            cancel.ThrowIfCancellationRequested();
            if (closed) throw new OperationCanceledException();
            _log.Info(nameof(SearchBrowserHost), $"uBlock Origin Lite enabled: {extension.Id}");
            webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            webView.CoreWebView2.Settings.IsZoomControlEnabled = true;
            ApplyTheme(lightTheme);
        }
        catch
        {
            var wasClosed = closed;
            if (!closed) window.Close();
            if (wasClosed) throw new OperationCanceledException();
            throw;
        }
    }

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs args)
    {
        var key = args.Key == Key.System ? args.SystemKey : args.Key;
        if (key == Key.Left &&
            (args.KeyboardDevice.Modifiers & ModifierKeys.Alt) != 0 &&
            _webView?.CoreWebView2 is { CanGoBack: true } core)
        {
            core.GoBack();
            args.Handled = true;
            return;
        }
        if (key != Key.Escape &&
            (key != Key.W || (args.KeyboardDevice.Modifiers & ModifierKeys.Control) == 0)) return;

        args.Handled = true;
        var window = (Window)sender;
        window.Dispatcher.BeginInvoke(() =>
        {
            if (ReferenceEquals(_window, window)) window.Close();
        });
    }

    private Grid CreateLoadingOverlay(PluginThemePalette palette, out TextBlock loadingText)
    {
        var dots = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        for (var index = 0; index < PluginPalette.SearchBrowserLoadingDots.Count; index++)
        {
            var dot = new Ellipse
            {
                Width = 10,
                Height = 10,
                Margin = new Thickness(5),
                Fill = Frozen(PluginPalette.SearchBrowserLoadingDots[index]),
            };
            dot.BeginAnimation(
                UIElement.OpacityProperty,
                new DoubleAnimation(0.25, 1, TimeSpan.FromMilliseconds(520))
                {
                    AutoReverse = true,
                    BeginTime = TimeSpan.FromMilliseconds(index * 140),
                    RepeatBehavior = RepeatBehavior.Forever,
                });
            dots.Children.Add(dot);
        }

        loadingText = new TextBlock
        {
            Margin = new Thickness(0, 18, 0, 0),
            FontFamily = new FontFamily("Segoe UI Variable Text"),
            FontSize = 16,
            Foreground = Frozen(palette.PrimaryText),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        var center = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        center.Children.Add(dots);
        center.Children.Add(loadingText);

        var overlay = new Grid
        {
            Background = Frozen(palette.WindowSurface),
            IsHitTestVisible = true,
        };
        overlay.Children.Add(center);
        Panel.SetZIndex(overlay, 1);
        return overlay;
    }

    private void ApplyProviderText(SearchProviderDescriptor descriptor)
    {
        var title = _strings.SearchBrowserWindowTitle(descriptor.DisplayName);
        if (_window is not null) _window.Title = title;
        if (_titleText is not null) _titleText.Text = title;
        if (_loadingText is not null)
            _loadingText.Text = _strings.SearchBrowserLoading(descriptor.DisplayName);
    }

    private void ApplyTheme(bool lightTheme)
    {
        var palette = PluginPalette.For(lightTheme);
        var background = Frozen(palette.WindowSurface);
        if (_window is not null)
        {
            _window.Background = background;
            if (_window.Content is Panel content) content.Background = background;
            ApplyWindowChromeTheme(_window, lightTheme);
        }

        if (_loadingOverlay is not null) _loadingOverlay.Background = background;
        if (_loadingText is not null) _loadingText.Foreground = Frozen(palette.PrimaryText);
        if (_titleText is not null) _titleText.Foreground = Frozen(palette.PrimaryText);
        if (_closeButton is not null)
        {
            _closeButton.Background = background;
            _closeButton.Foreground = Frozen(palette.PrimaryText);
        }
        if (_webView is not null)
        {
            _webView.DefaultBackgroundColor = ToDrawingColor(palette.WindowSurface);
            if (_webView.CoreWebView2 is { } coreWebView)
            {
                coreWebView.Profile.PreferredColorScheme = lightTheme
                    ? CoreWebView2PreferredColorScheme.Light
                    : CoreWebView2PreferredColorScheme.Dark;
            }
        }
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static DrawingColor ToDrawingColor(Color color) =>
        DrawingColor.FromArgb(color.A, color.R, color.G, color.B);

    private static void ApplyWindowChromeTheme(Window window, bool lightTheme)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        var useDarkMode = lightTheme ? 0 : 1;
        var result = NativeMethods.DwmSetWindowAttribute(
            hwnd,
            NativeMethods.DwmwaUseImmersiveDarkMode,
            ref useDarkMode,
            sizeof(int));
        if (result != 0)
        {
            NativeMethods.DwmSetWindowAttribute(
                hwnd,
                NativeMethods.DwmwaUseImmersiveDarkModeBefore20H1,
                ref useDarkMode,
                sizeof(int));
        }
    }

    private void ShowLoadingOverlay()
    {
        var overlay = _loadingOverlay;
        if (overlay is null) return;
        _loadingGeneration++;
        overlay.BeginAnimation(UIElement.OpacityProperty, null);
        overlay.Opacity = 1;
        overlay.Visibility = Visibility.Visible;
        if (_webView is not null) _webView.Visibility = Visibility.Hidden;
    }

    private void HideLoadingOverlay()
    {
        var overlay = _loadingOverlay;
        if (overlay is null || overlay.Visibility != Visibility.Visible) return;
        var generation = _loadingGeneration;

        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(180));
        fade.Completed += (_, _) =>
        {
            if (!ReferenceEquals(_loadingOverlay, overlay) || generation != _loadingGeneration) return;
            if (_webView is not null) _webView.Visibility = Visibility.Visible;
            overlay.Visibility = Visibility.Collapsed;
            overlay.BeginAnimation(UIElement.OpacityProperty, null);
            overlay.Opacity = 1;
        };
        overlay.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    private void CloseFailedShow()
    {
        if (_window is not null) _window.Close();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _lifetime.Cancel();
        SearchBrowserUiOperation? activeOperation;
        lock (_lifecycleGate) activeOperation = _activeOperation;
        _dispatcher.Send(() =>
        {
            _showGeneration++;
            _window?.Close();
            _webView?.Dispose();
            _window = null;
            _webView = null;
            activeOperation?.CancelBeforeStart();
        });
        if (activeOperation is null || activeOperation.UiFinished.Wait(_shutdownTimeout))
        {
            DisposeDispatcher();
            return;
        }

        _log.Warn(
            nameof(SearchBrowserHost),
            "browser UI operation did not stop within the shutdown timeout; dispatcher cleanup was deferred");
        _ = activeOperation.UiFinished.ContinueWith(
            _ => DisposeDispatcher(),
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default);
    }

    private void DisposeDispatcher()
    {
        if (Interlocked.Exchange(ref _dispatcherDisposeStarted, 1) != 0) return;
        _dispatcher.Dispose();
    }
}

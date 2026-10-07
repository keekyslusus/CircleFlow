using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CircleToSearch.Search.Browser;
using CircleToSearch.Ui;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace CircleToSearch.Shell.TestBrowser;

// A developer window on the search browser profile, so pages show the same banners, cookies and filters as searches.
internal sealed class TestBrowserWindow
{
    private readonly Func<Task<CoreWebView2Environment>> _createEnvironment;
    private readonly string _extensionArchivePath;
    private readonly string _filtersPath;
    private readonly string _userDataFolder;
    private readonly PluginLog _log;
    private readonly WebView2 _webView;
    private readonly TextBox _address;
    private readonly TextBlock _status;
    private readonly List<Button> _browserButtons = [];
    private string? _filterScriptId;
    private string? _extensionId;
    private CoreWebView2Environment? _environment;

    internal TestBrowserWindow(
        UiStrings strings,
        bool lightTheme,
        Func<Task<CoreWebView2Environment>> createEnvironment,
        string extensionArchivePath,
        string filtersPath,
        string userDataFolder,
        Action openFiltersFile,
        PluginLog log)
    {
        _createEnvironment = createEnvironment;
        _extensionArchivePath = extensionArchivePath;
        _filtersPath = filtersPath;
        _userDataFolder = userDataFolder;
        _log = log;
        var palette = PluginPalette.For(lightTheme);
        var background = new SolidColorBrush(palette.Roles.Background);
        var foreground = new SolidColorBrush(palette.Roles.OnBackground);
        background.Freeze();
        foreground.Freeze();

        _webView = new WebView2 { DefaultBackgroundColor = System.Drawing.Color.FromArgb(
            palette.Roles.Background.A, palette.Roles.Background.R, palette.Roles.Background.G, palette.Roles.Background.B) };
        _address = new TextBox { VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) };
        _address.KeyDown += OnAddressKeyDown;
        _status = new TextBlock
        {
            Foreground = foreground,
            Margin = new Thickness(16),
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
        };

        var toolbar = new DockPanel { Margin = new Thickness(8), LastChildFill = true };
        var back = CreateBrowserButton(strings.TestBrowserText("back"), () => _webView.CoreWebView2?.GoBack());
        DockPanel.SetDock(back, Dock.Left);
        toolbar.Children.Add(back);
        Button? extensionButton = null;
        extensionButton = CreateBrowserButton(strings.TestBrowserText("ubol"), () => _ = ShowExtensionPopupAsync(extensionButton!));
        foreach (var button in new[]
                 {
                     CreateBrowserButton(strings.TestBrowserText("devtools"), () => _webView.CoreWebView2?.OpenDevToolsWindow()),
                     extensionButton,
                     CreateButton(strings.TestBrowserText("edit_filters"), openFiltersFile),
                     CreateBrowserButton(strings.TestBrowserText("reload_filters"), () => _ = ReloadFiltersAsync()),
                 })
        {
            DockPanel.SetDock(button, Dock.Right);
            toolbar.Children.Add(button);
        }
        toolbar.Children.Add(_address);

        var content = new DockPanel { Background = background };
        DockPanel.SetDock(toolbar, Dock.Top);
        DockPanel.SetDock(_status, Dock.Top);
        content.Children.Add(toolbar);
        content.Children.Add(_status);
        content.Children.Add(_webView);

        Window = new Window
        {
            Title = strings.TestBrowserText("title"),
            Width = 1100,
            Height = 800,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = background,
            Content = content,
        };
        Window.Loaded += async (_, _) => await InitializeAsync(strings);
        Window.Closed += (_, _) => _webView.Dispose();
    }

    internal Window Window { get; }

    private static Button CreateButton(string text, Action click)
    {
        var button = new Button { Content = text, Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(2, 0, 2, 0) };
        button.Click += (_, _) => click();
        return button;
    }

    private Button CreateBrowserButton(string text, Action click)
    {
        var button = CreateButton(text, click);
        button.IsEnabled = false;
        _browserButtons.Add(button);
        return button;
    }

    private async Task InitializeAsync(UiStrings strings)
    {
        try
        {
            _environment = await _createEnvironment();
            await _webView.EnsureCoreWebView2Async(_environment);
            var core = _webView.CoreWebView2;
            core.Settings.AreDevToolsEnabled = true;
            core.Settings.AreDefaultContextMenusEnabled = true;
            core.SourceChanged += (_, _) => _address.Text = core.Source;
            // Popups would open outside this window, without the filters and DevTools set up here.
            core.NewWindowRequested += (_, args) =>
            {
                args.Handled = true;
                core.Navigate(args.Uri);
            };
            _filterScriptId = await core.AddScriptToExecuteOnDocumentCreatedAsync(
                await Task.Run(() => CosmeticFilters.LoadScript(_filtersPath, _log)));
            _extensionId = (await SearchBrowserExtension.EnsureEnabledAsync(
                _webView, _extensionArchivePath, _userDataFolder, _log, CancellationToken.None)).Id;
            foreach (var button in _browserButtons) button.IsEnabled = true;
            _address.Focus();
        }
        catch (Exception exception)
        {
            _log.Error(nameof(TestBrowserWindow), "initializing the test browser failed", exception);
            _status.Text = strings.TestBrowserText("failed");
            _status.Visibility = Visibility.Visible;
        }
    }

    private void OnAddressKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key != Key.Enter || _webView.CoreWebView2 is not { } core) return;
        args.Handled = true;
        var text = _address.Text.Trim();
        if (text.Length == 0) return;
        // "example.com" and "localhost:3000" have no scheme to parse, so they are treated as https addresses.
        var withScheme = text.Contains("://", StringComparison.Ordinal) || text.StartsWith("about:", StringComparison.Ordinal)
            ? text
            : "https://" + text;
        if (!Uri.TryCreate(withScheme, UriKind.Absolute, out var target)) return;
        try { core.Navigate(target.AbsoluteUri); }
        catch (ArgumentException) { }
    }

    private async Task ShowExtensionPopupAsync(Button anchor)
    {
        if (_extensionId is null || _environment is null || _webView.CoreWebView2 is not { } core) return;
        try
        {
            var pageToken = Guid.NewGuid().ToString("N");
            await core.ExecuteScriptAsync($"window.__circleFlowTestBrowser = '{pageToken}'");
            await ExtensionPopupWindow.ShowAsync(anchor, _environment, _extensionId, core.Source, pageToken,
                uri => _webView.CoreWebView2?.Navigate(uri));
        }
        catch (Exception exception)
        {
            _log.Error(nameof(TestBrowserWindow), "showing the uBO Lite popup failed", exception);
        }
    }

    private async Task ReloadFiltersAsync()
    {
        if (_webView.CoreWebView2 is not { } core) return;
        try
        {
            var script = await Task.Run(() => CosmeticFilters.LoadScript(_filtersPath, _log));
            if (_filterScriptId is not null) core.RemoveScriptToExecuteOnDocumentCreated(_filterScriptId);
            _filterScriptId = await core.AddScriptToExecuteOnDocumentCreatedAsync(script);
            core.Reload();
        }
        catch (Exception exception)
        {
            _log.Error(nameof(TestBrowserWindow), "reloading filters failed", exception);
        }
    }
}

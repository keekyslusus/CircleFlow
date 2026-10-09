using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CircleToSearch;
using CircleToSearch.Capture;
using CircleToSearch.Interop;
using CircleToSearch.MusicRecognition;
using CircleToSearch.Search;
using CircleToSearch.Search.Browser;
using CircleToSearch.Settings;
using CircleToSearch.Shell;
using CircleToSearch.Shell.SettingsPreview;
using CircleToSearch.TextRecognition;
using CircleToSearch.Ui;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

internal static class StartupHook
{
    public static void Initialize() => _ = Task.Run(RunAsync);

    private static async Task RunAsync()
    {
        var reportPath = Path.Combine(new AppPaths().TempDirectory, "publish-probe.json");
        try
        {
            var timer = Stopwatch.StartNew();
            while (Application.Current is null)
            {
                if (timer.Elapsed > TimeSpan.FromSeconds(15)) throw new TimeoutException("Application was not created.");
                await Task.Delay(25);
            }
            var application = Application.Current;
            var report = await application.Dispatcher.InvokeAsync(ProbeAsync, DispatcherPriority.ApplicationIdle)
                .Task.Unwrap().WaitAsync(TimeSpan.FromSeconds(45));
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            await application.Dispatcher.InvokeAsync(() =>
            {
                var args = (SessionEndingCancelEventArgs)Activator.CreateInstance(typeof(SessionEndingCancelEventArgs),
                    BindingFlags.Instance | BindingFlags.NonPublic, null, [ReasonSessionEnding.Logoff], null)!;
                // Raise only the managed event; never request an actual Windows logoff.
                typeof(Application).GetMethod("OnSessionEnding", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(application, [args]);
                Require(!args.Cancel, "The application vetoed Windows session ending.");
            });
        }
        catch (Exception exception)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new { Success = false, Error = exception.ToString() }));
            Environment.Exit(1);
        }
    }

    private static async Task<object> ProbeAsync()
    {
        var paths = new AppPaths();
        Require(Path.GetFileName(Environment.ProcessPath) == "CircleFlow.exe", "The published executable must be the process host.");
        Require(!string.Equals(Environment.CurrentDirectory, paths.RootDirectory, StringComparison.OrdinalIgnoreCase), "Expected a different working directory.");
        Require(Path.GetDirectoryName(Environment.ProcessPath) == paths.RootDirectory, "Assets must resolve beside the apphost.");
        var log = new PluginLog(paths.LogsDirectory);
        var notifications = new PluginNotifier((_, _) => { }, (_, _, _, _, _) => { }, (_, message) => throw new InvalidOperationException(message), log);
        var embeddedStrings = new UiStrings(LocalUiStrings.LoadEmbeddedEnglish().Get);
        var settingsWindow = new SettingsWindowView(embeddedStrings, true, paths.TrayIconPath,
            CreateSettingsModel(paths, embeddedStrings, notifications, log),
            feedback => new ClipboardCopyService(_ => { }, feedback, embeddedStrings),
            new RemoteImageLoader(new HttpClient())).Window;
        Require(settingsWindow.Icon is not null, "Settings icon did not load.");
        settingsWindow.Close();
        var source = LocalUiStrings.LoadEnglish(paths.LanguagesDirectory)
            .Translate(paths.LanguagesDirectory, CultureInfo.GetCultureInfo("fr-CA"));
        var strings = new UiStrings(source.Get);
        foreach (var property in typeof(UiStrings).GetProperties().Where(property => property.PropertyType == typeof(string)))
            Require(!string.IsNullOrWhiteSpace((string?)property.GetValue(strings)), "Missing published string: " + property.Name);
        Require(strings.TrayOpen == "Open", "Published English fallback did not resolve.");
        Require(File.Exists(paths.TrayIconPath), "Missing icon.");
        Require(File.Exists(Path.Combine(paths.LicensesDirectory, "LICENSE.txt")), "Missing license.");
        Require(File.Exists(Path.Combine(paths.LicensesDirectory, "THIRD_PARTY_NOTICES.txt")), "Missing third-party notices.");
        using (var icon = new System.Drawing.Icon(paths.TrayIconPath)) Require(icon.Width > 0, "Invalid ICO.");
        var extension = SearchBrowserExtension.Prepare(paths.ExtensionArchivePath, paths.SearchProfileDirectory);
        Require(File.Exists(Path.Combine(extension, "manifest.json")), "Bundled extension failed to extract.");
        var languages = OcrEngine.AvailableRecognizerLanguages;
        Require(languages.Count > 0, "Install at least one Windows OCR language pack to run this check.");
        var ocr = OcrEngine.TryCreateFromLanguage(languages[0]);
        Require(ocr is not null, "OCR engine could not be created.");
        using var bitmap = new SoftwareBitmap(BitmapPixelFormat.Bgra8, 64, 32, BitmapAlphaMode.Premultiplied);
        _ = await ocr!.RecognizeAsync(bitmap);
        var winrt = typeof(WinRT.ComWrappersSupport).Assembly;
        Require(AssemblyLoadContext.GetLoadContext(winrt) == AssemblyLoadContext.Default, "WinRT loaded outside the default context.");
        RequireLocalAssembly(winrt, paths);
        RequireLocalAssembly(typeof(object).Assembly, paths);
        RequireLocalAssembly(typeof(Application).Assembly, paths);
        RequireLocalAssembly(typeof(AppRuntime).Assembly, paths);
        var reference = typeof(WebView2CompositionControl).Assembly.GetReferencedAssemblies().Single(name => name.Name == "WinRT.Runtime");
        Require(AssemblyLoadContext.Default.LoadFromAssemblyName(reference) == winrt, "WebView2 resolved a different WinRT runtime.");
        var factory = new WebViewEnvironmentFactory(paths, strings, notifications);
        var environment = await factory.CreateAsync(paths.SearchProfileDirectory);
        var compositionEnvironment = await factory.CreateAsync(paths.TraceVideoProfileDirectory);
        var translationEnvironment = await factory.CreateAsync(paths.ImageTranslationProfileDirectory);
        using var browser = new WebView2();
        using var composition = new WebView2CompositionControl();
        var panel = new Grid();
        panel.RowDefinitions.Add(new RowDefinition());
        panel.RowDefinitions.Add(new RowDefinition());
        panel.Children.Add(browser);
        Grid.SetRow(composition, 1);
        panel.Children.Add(composition);
        var window = new Window
        {
            Title = strings.PluginTitle, Content = panel, Width = 400, Height = 300,
            ShowActivated = false, ShowInTaskbar = false,
        };
        try
        {
            window.Show();
            await browser.EnsureCoreWebView2Async(environment);
            await composition.EnsureCoreWebView2Async(compositionEnvironment);
            await Task.WhenAll(NavigateAsync(browser.CoreWebView2), NavigateAsync(composition.CoreWebView2));
            Require(await browser.ExecuteScriptAsync("1 + 1") == "2", "Regular WebView2 did not execute JavaScript.");
            Require(await composition.CoreWebView2.ExecuteScriptAsync("1 + 1") == "2", "Composition WebView2 did not execute JavaScript.");
            _ = await ocr.RecognizeAsync(bitmap);
            Require(AssemblyLoadContext.Default.Assemblies.Count(assembly => assembly.GetName().Name == "WinRT.Runtime") == 1,
                "Multiple WinRT runtimes are loaded.");
            Require(!AppDomain.CurrentDomain.GetAssemblies().Any(assembly => assembly.GetName().Name!.StartsWith("Flow.Launcher")), "Flow assembly loaded.");
            using var process = Process.GetCurrentProcess();
            var loader = process.Modules.Cast<ProcessModule>().Single(module => module.ModuleName.Equals("WebView2Loader.dll", StringComparison.OrdinalIgnoreCase)).FileName;
            Require(loader.StartsWith(paths.RootDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "WebView2 loader came from outside the published folder.");
            Require(!Directory.EnumerateDirectories(paths.RootDirectory, "*.WebView2").Any(), "Unexpected default WebView2 profile beside the executable.");
            return new
            {
                Success = true, ProcessPath = Environment.ProcessPath, WorkingDirectory = Environment.CurrentDirectory,
                WinRtRuntime = winrt.Location, DotNetRuntime = typeof(object).Assembly.Location,
                WebView2Loader = loader, WebView2Version = environment.BrowserVersionString,
                OcrLanguage = languages[0].LanguageTag, OcrRecognizedTwice = true,
                RegularWebView2 = true, CompositionWebView2 = true, PublishedLocalization = true,
                Profiles = new[] { environment.UserDataFolder, compositionEnvironment.UserDataFolder, translationEnvironment.UserDataFolder },
            };
        }
        finally
        {
            window.Close();
        }
    }

    // In-memory settings, a probe-only music history file and a no-op URL opener keep the probe from changing user data or opening anything.
    private static SettingsWindowModel CreateSettingsModel(AppPaths paths, UiStrings strings, IPluginNotifier notifier, PluginLog log)
    {
        var settings = new SettingsService(new AppSettings(), _ => { },
            gesture => new(true, new(gesture ?? string.Empty, gesture is not null)), log);
        var router = new VisualSearchProviderRouter(
            [new(new SearchProviderDescriptor(SearchProviderIds.GoogleLens, () => strings.GoogleLensProviderName),
                () => throw new InvalidOperationException("Not used by settings."))],
            SearchProviderIds.GoogleLens, log);
        var language = new UiLanguage(LocalUiStrings.LoadEnglish(paths.LanguagesDirectory),
            new AppLanguageCatalog(paths.LanguagesDirectory), CultureInfo.GetCultureInfo("en-US"), log);
        var urlOpening = new UrlOpeningService(_ => false, notifier, strings, log);
        var history = new MusicHistory(Path.Combine(paths.TempDirectory, "publish-probe-music-history.json"),
            () => false, () => MusicHistory.KeepForever, TimeProvider.System, log);
        return new SettingsWindowModel(settings, new ProviderSelectionStore(router, settings, notifier, strings, log),
            new OcrLanguageCatalog(), history, new MusicResultPresenter(urlOpening, notifier, strings),
            language, CultureInfo.GetCultureInfo("en-US"), new ProjectSupport(urlOpening), urlOpening,
            paths, strings, () => null, () => null, new WindowsStartupRegistration(paths.ExecutablePath, log), () => { }, () => { }, () => { },
            checkForUpdates: null);
    }

    private static async Task NavigateAsync(CoreWebView2 core)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Completed(object? sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            if (args.IsSuccess) completion.TrySetResult();
            else completion.TrySetException(new InvalidOperationException("Local navigation failed: " + args.WebErrorStatus));
        }
        core.NavigationCompleted += Completed;
        try
        {
            core.NavigateToString("<!doctype html><html><body></body></html>");
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally { core.NavigationCompleted -= Completed; }
    }

    private static void RequireLocalAssembly(Assembly assembly, AppPaths paths) =>
        Require(Path.GetDirectoryName(assembly.Location)!.Equals(Path.Combine(paths.RootDirectory, "deps"), StringComparison.OrdinalIgnoreCase), "Assembly is not bundled: " + assembly.FullName);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

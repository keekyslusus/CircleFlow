using CircleToSearch.Search;
using CircleToSearch.Settings;
using CircleToSearch.Shell;
using CircleToSearch.Shell.SettingsPreview;

namespace CircleToSearch.Tests;

internal sealed class TestSettingsWindow
{
    public TestSettingsWindow(SettingsService? settings = null, bool openSucceeds = true,
        string? webViewRuntimeVersion = "140.0.3485.54")
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
        Model = new SettingsWindowModel(Settings, providers, new ProjectSupport(urlOpening), urlOpening, Paths,
            TestUiStrings.English, () => webViewRuntimeVersion);
    }

    public SettingsService Settings { get; }
    public SettingsWindowModel Model { get; }
    public AppPaths Paths { get; } = new();
    public TestPluginNotifier Notifier { get; } = new();
    public List<string> Opened { get; } = [];

    public SettingsWindowView CreateView(bool light = true) =>
        new(TestUiStrings.English, light, Paths.TrayIconPath, Model);

    private static VisualSearchProviderRegistration Registration(string id, string name) =>
        new(new SearchProviderDescriptor(id, name), () => throw new InvalidOperationException("Not used by settings."));
}

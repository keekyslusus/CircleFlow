using CircleToSearch.Interop;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class WebViewEnvironmentFactoryTests
{
    [Fact]
    public async Task Invalid_requested_profiles_fail_before_any_directory_or_browser_is_created()
    {
        var paths = NewPaths();
        var factory = new WebViewEnvironmentFactory(paths, TestUiStrings.English, new TestPluginNotifier());
        foreach (var path in new[] { "", "relative", paths.RootDirectory, paths.DataDirectory + "-other", Path.Combine(paths.DataDirectory, "..", "outside") })
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => factory.CreateAsync(path));
            Assert.Equal(TestUiStrings.English.BrowserProfileOutsideData, error.Message);
        }
        Assert.False(Directory.Exists(paths.RootDirectory));
    }

    [Fact]
    public async Task All_three_environments_report_the_explicit_profiles_under_Data()
    {
        if (await IsolatedTestHost.RunAsync<WebViewEnvironmentFactoryTests>()) return;
        Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", null);
        var paths = NewPaths();
        AppDataDirectory.Initialize(paths);
        var factory = new WebViewEnvironmentFactory(paths, TestUiStrings.English, new TestPluginNotifier());
        using var dispatcher = new StaDispatcher("CircleFlow profile test");
        var test = await dispatcher.InvokeAsync(async () =>
        {
            foreach (var profile in new[] { paths.SearchProfileDirectory, paths.ImageTranslationProfileDirectory, paths.TraceVideoProfileDirectory })
            {
                var environment = await factory.CreateAsync(profile, enableExtensions: profile == paths.SearchProfileDirectory);
                Assert.Equal(profile, environment.UserDataFolder, ignoreCase: true);
            }
        }, CancellationToken.None);
        await test!.WaitAsync(TimeSpan.FromSeconds(15));
    }

    [Fact]
    public async Task External_environment_override_is_rejected_without_deleting_external_data()
    {
        if (await IsolatedTestHost.RunAsync<WebViewEnvironmentFactoryTests>()) return;
        var paths = NewPaths();
        var external = Path.Combine(paths.RootDirectory, "external-profile");
        Directory.CreateDirectory(external);
        var marker = Path.Combine(external, "keep.txt");
        File.WriteAllText(marker, "keep external data");
        Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", external);
        var notifier = new TestPluginNotifier();
        var factory = new WebViewEnvironmentFactory(paths, TestUiStrings.English, notifier);
        using var dispatcher = new StaDispatcher("CircleFlow overridden profile test");
        var test = await dispatcher.InvokeAsync(async () =>
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => factory.CreateAsync(paths.SearchProfileDirectory));
            Assert.Equal(TestUiStrings.English.BrowserProfileOutsideData, error.Message);
            Assert.Equal((TestUiStrings.English.PluginTitle, error.Message), Assert.Single(notifier.Errors));
            Assert.Equal("keep external data", File.ReadAllText(marker));
        }, CancellationToken.None);
        await test!.WaitAsync(TimeSpan.FromSeconds(15));
    }

    private static AppPaths NewPaths() => new(Path.Combine(TestOutputPaths.TempDirectory, "browser profiles " + Guid.NewGuid().ToString("N")));
}

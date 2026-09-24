using CircleToSearch.Interop;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class BrowserDataCleanupTests
{
    private static readonly DateTime Start = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task First_run_only_starts_counting_and_keeps_browser_data()
    {
        var (paths, now, cleanup) = Create();
        var data = BrowserData(paths.SearchProfileDirectory);

        await cleanup.Run(28);

        Assert.True(Directory.Exists(data));
        now.Value = Start.AddDays(27);
        await cleanup.Run(28);
        Assert.True(Directory.Exists(data));
    }

    [Theory]
    [InlineData(28)]
    [InlineData(56)]
    public async Task Due_cleanup_removes_browser_data_of_every_profile_and_keeps_extensions(int days)
    {
        var (paths, now, cleanup) = Create();
        string[] profiles = [paths.SearchProfileDirectory, paths.ImageTranslationProfileDirectory, paths.TraceVideoProfileDirectory];
        foreach (var profile in profiles) BrowserData(profile);
        var extension = Path.Combine(paths.SearchProfileDirectory, "CircleFlowExtensions", "uBlockOriginLite");
        Directory.CreateDirectory(extension);
        await cleanup.Run(days);

        now.Value = Start.AddDays(days - 1);
        await cleanup.Run(days);
        Assert.All(profiles, profile => Assert.True(Directory.Exists(Path.Combine(profile, "EBWebView"))));

        now.Value = Start.AddDays(days);
        await cleanup.Run(days);
        Assert.Equal(["CircleFlowExtensions"], Entries(paths.SearchProfileDirectory));
        Assert.Empty(Entries(paths.ImageTranslationProfileDirectory));
        Assert.Empty(Entries(paths.TraceVideoProfileDirectory));
        Assert.True(Directory.Exists(extension));

        BrowserData(paths.SearchProfileDirectory);
        now.Value = Start.AddDays(days * 2 - 1);
        await cleanup.Run(days);
        Assert.True(Directory.Exists(Path.Combine(paths.SearchProfileDirectory, "EBWebView")));
    }

    [Fact]
    public async Task Never_keeps_browser_data_but_still_removes_interrupted_trash()
    {
        var (paths, now, cleanup) = Create();
        var data = BrowserData(paths.SearchProfileDirectory);
        var trash = Path.Combine(paths.TraceVideoProfileDirectory, "EBWebView.trash-0123");
        Directory.CreateDirectory(Path.Combine(trash, "Default", "Cache"));
        File.WriteAllText(Path.Combine(trash, "Default", "Cache", "data_0"), "cached");
        await cleanup.Run(28);

        now.Value = Start.AddDays(365);
        await cleanup.Run(BrowserDataCleanup.Never);

        Assert.True(Directory.Exists(data));
        Assert.False(Directory.Exists(trash));
    }

    [Fact]
    public async Task Browser_data_in_use_is_kept_and_retried_on_the_next_start()
    {
        var (paths, now, cleanup) = Create();
        var data = BrowserData(paths.SearchProfileDirectory);
        await cleanup.Run(28);
        now.Value = Start.AddDays(30);

        using (new FileStream(Path.Combine(data, "Default", "Cookies"), FileMode.Open, FileAccess.Read, FileShare.None))
            await cleanup.Run(28);
        Assert.True(Directory.Exists(data));

        await cleanup.Run(28);
        Assert.False(Directory.Exists(data));
    }

    private static IEnumerable<string?> Entries(string profile) =>
        Directory.EnumerateFileSystemEntries(profile).Select(Path.GetFileName);

    private static string BrowserData(string profile)
    {
        var data = Path.Combine(profile, "EBWebView");
        Directory.CreateDirectory(Path.Combine(data, "Default"));
        File.WriteAllText(Path.Combine(data, "Default", "Cookies"), "cookies");
        return data;
    }

    private static (AppPaths Paths, Clock Now, BrowserDataCleanup Cleanup) Create()
    {
        var paths = new AppPaths(TestOutputPaths.NewTempDirectory("browser-cleanup-" + Guid.NewGuid().ToString("N")));
        var now = new Clock { Value = Start };
        return (paths, now, new BrowserDataCleanup(paths, new PluginLog(paths.LogsDirectory), () => now.Value));
    }

    private sealed class Clock
    {
        public DateTime Value { get; set; }
    }
}

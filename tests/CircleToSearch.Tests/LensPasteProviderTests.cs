using System.Diagnostics;
using CircleToSearch.Search;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class LensPasteProviderTests
{
    [Fact]
    public async Task Copies_image_opens_lens_and_reports_handled()
    {
        var copied = new List<byte[]>();
        var opened = new List<string>();
        var watchStarted = 0;
        var injector = new BrowserPasteInjector(SilentLog());
        var provider = new LensPasteProvider(
            png => { copied.Add(png); return Task.FromResult(true); },
            url => { opened.Add(url); return Process.GetCurrentProcess(); },
            () => { watchStarted++; return Task.CompletedTask; },
            injector,
            SilentLog());

        var outcome = await provider.SearchAsync([1, 2, 3], CancellationToken.None);

        Assert.True(outcome.Success);
        Assert.Null(outcome.ResultsUrl);
        Assert.Equal([new byte[] { 1, 2, 3 }], copied);
        Assert.Equal([LensPasteProvider.LensHomeUrl], opened);
        Assert.Equal(1, watchStarted);
    }

    [Fact]
    public async Task Clipboard_failure_fails_without_opening_the_browser()
    {
        var opened = new List<string>();
        var watchStarted = 0;
        var provider = new LensPasteProvider(
            _ => Task.FromResult(false),
            url => { opened.Add(url); return Process.GetCurrentProcess(); },
            () => { watchStarted++; return Task.CompletedTask; },
            new BrowserPasteInjector(SilentLog()),
            SilentLog());

        var outcome = await provider.SearchAsync([1], CancellationToken.None);

        Assert.False(outcome.Success);
        Assert.Equal(LensUploadFailure.ClipboardUnavailable, outcome.Failure);
        Assert.Empty(opened);
        Assert.Equal(0, watchStarted);
    }

    [Fact]
    public async Task Browser_launch_failure_fails_without_scheduling_paste()
    {
        var watchStarted = 0;
        var provider = new LensPasteProvider(
            _ => Task.FromResult(true),
            _ => null,
            () => { watchStarted++; return Task.CompletedTask; },
            new BrowserPasteInjector(SilentLog()),
            SilentLog());

        var outcome = await provider.SearchAsync([1], CancellationToken.None);

        Assert.False(outcome.Success);
        Assert.Equal(LensUploadFailure.BrowserLaunchFailed, outcome.Failure);
        Assert.Equal(0, watchStarted);
    }

    private static PluginLog SilentLog()
    {
        var directory = Path.Combine(Path.GetTempPath(), "CircleToSearch.Tests");
        Directory.CreateDirectory(directory);
        return new PluginLog(directory);
    }
}

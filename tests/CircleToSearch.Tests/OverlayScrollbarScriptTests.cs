using CircleToSearch.Search.Browser;
using CircleToSearch.Ui;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class OverlayScrollbarScriptTests
{
    [Fact]
    public void Script_uses_overlay_thumb_and_exact_idle_delay()
    {
        var script = OverlayScrollbarScript.Create();

        Assert.Contains("position', 'fixed'", script);
        Assert.Contains("pointer-events', 'none'", script);
        Assert.Contains("width: 0 !important", script);
        Assert.Contains($"width', '{OverlayScrollbarPolicy.ThumbWidthPixels}px'", script);
        Assert.Contains($"const hideDelay = {OverlayScrollbarPolicy.HideDelayMilliseconds};", script);
        Assert.Contains($"transition-duration', '{OverlayScrollbarPolicy.FadeInMilliseconds}ms'", script);
        Assert.Contains($"transition-duration', '{OverlayScrollbarPolicy.FadeOutMilliseconds}ms'", script);
    }

    [Fact]
    public void Script_contains_palette_colors_for_both_themes()
    {
        var script = OverlayScrollbarScript.Create();

        Assert.Contains(CssColor(PluginPalette.For(lightTheme: true).SearchBrowserScrollbarThumb), script);
        Assert.Contains(CssColor(PluginPalette.For(lightTheme: false).SearchBrowserScrollbarThumb), script);
        Assert.Contains("prefers-color-scheme: dark", script);
    }

    private static string CssColor(System.Windows.Media.Color color) =>
        $"#{color.R:X2}{color.G:X2}{color.B:X2}{color.A:X2}";
}

using CircleToSearch.Search.Browser;
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
        Assert.Contains($"width', '{OverlayScrollbarScript.ThumbWidthPixels}px'", script);
        Assert.Contains($"const hideDelay = {OverlayScrollbarScript.HideDelayMilliseconds};", script);
        Assert.Contains($"transition-duration', '{OverlayScrollbarScript.FadeInMilliseconds}ms'", script);
        Assert.Contains($"transition-duration', '{OverlayScrollbarScript.FadeOutMilliseconds}ms'", script);
    }

    [Fact]
    public void Script_contains_palette_colors_for_both_themes()
    {
        var script = OverlayScrollbarScript.Create();

        Assert.Contains("#30343A8F", script);
        Assert.Contains("#E8EAEDA6", script);
        Assert.Contains("prefers-color-scheme: dark", script);
    }
}

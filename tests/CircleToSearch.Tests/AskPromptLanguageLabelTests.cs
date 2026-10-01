using CircleToSearch.Capture;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class AskPromptLanguageLabelTests
{
    [Theory]
    [InlineData("ru-RU", "RU")]
    [InlineData("en-US", "EN")]
    [InlineData("zh-Hans-CN", "ZH")]
    [InlineData("haw-US", "HAW")]
    [InlineData(null, null)]
    [InlineData("", null)]
    public void Keyboard_layout_tag_becomes_its_iso_language_code(string? tag, string? expected) =>
        Assert.Equal(expected, PromptLanguageTag.CodeFor(tag));
}

using CircleToSearch.Trigger;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class HotkeyGestureParserTests
{
    [Fact]
    public void Parses_the_default_gesture()
    {
        var parsed = HotkeyGestureParser.TryParse("Ctrl+Alt+Space", out var modifiers, out var virtualKey);

        Assert.True(parsed);
        Assert.Equal((uint)(0x2 | 0x1), modifiers);
        Assert.Equal(0x20u, virtualKey);
    }

    [Fact]
    public void Formats_canonically()
    {
        Assert.Equal("Ctrl+Alt+Space", HotkeyGestureParser.Format(0x3, 0x20));
        Assert.Equal("Win+Shift+F5", HotkeyGestureParser.Format(0x8 | 0x4, 0x74));
    }

    [Fact]
    public void Round_trips_modifiers_and_key()
    {
        foreach (var gesture in new[] { "Ctrl+Alt+Space", "Win+Shift+A", "Alt+F3", "Win+Ctrl+Shift+Q" })
        {
            Assert.True(HotkeyGestureParser.TryParse(gesture, out var modifiers, out var virtualKey));
            Assert.Equal(gesture, HotkeyGestureParser.Format(modifiers, virtualKey));
        }
    }

    [Fact]
    public void Parsing_is_case_insensitive()
    {
        Assert.True(HotkeyGestureParser.TryParse("ctrl+alt+space", out _, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Space")]
    [InlineData("Ctrl")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Nope")]
    [InlineData("Ctrl+Alt+Ctrl")]
    public void Invalid_gestures_are_rejected(string? gesture)
    {
        Assert.False(HotkeyGestureParser.TryParse(gesture, out _, out _));
    }
}

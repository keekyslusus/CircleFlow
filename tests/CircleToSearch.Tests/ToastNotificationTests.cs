using CircleToSearch.Capture;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class ToastNotificationTests
{
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void Message_must_not_be_empty(string message) =>
        Assert.Throws<ArgumentException>(() => new ToastNotification(message, ToastTone.Error));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Duration_must_be_positive(double milliseconds) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new ToastNotification(
            "Message",
            ToastTone.Neutral,
            TimeSpan.FromMilliseconds(milliseconds)));

    [Fact]
    public void Default_duration_is_three_seconds()
    {
        var notification = new ToastNotification("Message", ToastTone.Success);

        Assert.Equal(TimeSpan.FromMilliseconds(3000), notification.Duration);
    }

    [Theory]
    [InlineData(0x00, 0x00, 0x00, "#000000")]
    [InlineData(0x03, 0x7B, 0xD5, "#037BD5")]
    [InlineData(0xFF, 0xFF, 0xFF, "#FFFFFF")]
    public void Color_sample_formats_fixed_width_uppercase_hex(
        byte red,
        byte green,
        byte blue,
        string expected) =>
        Assert.Equal(expected, new ToastColorSample(red, green, blue).HexCode);
}

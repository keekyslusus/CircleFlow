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
}

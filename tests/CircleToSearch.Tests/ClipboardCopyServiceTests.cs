using CircleToSearch.Capture;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class ClipboardCopyServiceTests
{
    [Fact]
    public void Success_copies_exact_input_and_shows_one_success_toast()
    {
        var copied = new List<string>();
        var notifications = new List<ToastNotification>();
        var service = new ClipboardCopyService(copied.Add, notifications.Add, TestUiStrings.English);

        var result = service.TryCopy("original text");

        Assert.True(result);
        Assert.Equal(["original text"], copied);
        var toast = Assert.Single(notifications);
        Assert.Equal("Copied: original text", toast.Message);
        Assert.Equal(ToastTone.Success, toast.Tone);
    }

    [Theory]
    [InlineData(49)]
    [InlineData(50)]
    public void Preview_at_or_below_limit_is_not_truncated(int length)
    {
        var notifications = new List<ToastNotification>();
        var service = new ClipboardCopyService(_ => { }, notifications.Add, TestUiStrings.English);
        var text = new string('a', length);

        Assert.True(service.TryCopy(text));

        Assert.Equal("Copied: " + text, Assert.Single(notifications).Message);
    }

    [Fact]
    public void Preview_above_limit_is_truncated_without_truncating_clipboard()
    {
        string? copied = null;
        var notifications = new List<ToastNotification>();
        var service = new ClipboardCopyService(text => copied = text, notifications.Add, TestUiStrings.English);
        var text = new string('a', 51);

        Assert.True(service.TryCopy(text));

        Assert.Equal(text, copied);
        Assert.Equal("Copied: " + new string('a', 50) + "...", Assert.Single(notifications).Message);
    }

    [Fact]
    public void Preview_collapses_whitespace_without_changing_clipboard_text()
    {
        string? copied = null;
        var notifications = new List<ToastNotification>();
        var service = new ClipboardCopyService(text => copied = text, notifications.Add, TestUiStrings.English);
        const string text = " \tone \r\n two\u00A0\u00A0three ";

        Assert.True(service.TryCopy(text));

        Assert.Equal(text, copied);
        Assert.Equal("Copied: one two three", Assert.Single(notifications).Message);
    }

    [Theory]
    [InlineData("😀")]
    [InlineData("e\u0301")]
    public void Preview_does_not_split_unicode_text_element_at_limit(string boundaryElement)
    {
        var notifications = new List<ToastNotification>();
        var service = new ClipboardCopyService(_ => { }, notifications.Add, TestUiStrings.English);
        var prefix = new string('a', 49);

        Assert.True(service.TryCopy(prefix + boundaryElement + "z"));

        Assert.Equal("Copied: " + prefix + boundaryElement + "...", Assert.Single(notifications).Message);
    }

    [Fact]
    public void Clipboard_exception_returns_false_and_shows_only_error_toast()
    {
        var notifications = new List<ToastNotification>();
        var service = new ClipboardCopyService(
            _ => throw new InvalidOperationException(),
            notifications.Add,
            TestUiStrings.English);

        Assert.False(service.TryCopy("text"));

        var toast = Assert.Single(notifications);
        Assert.Equal(TestUiStrings.English.CopyFailed, toast.Message);
        Assert.Equal(ToastTone.Error, toast.Tone);
    }
}

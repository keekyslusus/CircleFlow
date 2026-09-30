using CircleToSearch.Links;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class ScreenLinkTests
{
    [Theory]
    [InlineData("https://github.com/keekyslusus", "github.com")]
    [InlineData("  http://Example.COM/path?q=1  ", "example.com")]
    [InlineData("https://www.youtube.com/watch?v=1", "youtube.com")]
    [InlineData("www.example.org/menu", "example.org")]
    public void Web_link_opens_and_is_labeled_with_its_host(string content, string host)
    {
        var link = ScreenLink.Classify(content);

        Assert.Equal(ScreenLinkKind.Web, link.Kind);
        Assert.Equal(host, link.Label);
        Assert.Equal(content.Trim(), link.CopyText);
        Assert.NotNull(link.Target);
        Assert.True(ScreenLink.CanOpen(link.Target));
    }

    [Fact]
    public void Look_alike_host_is_shown_in_punycode()
    {
        var link = ScreenLink.Classify("https://\u0430pple.com/login");

        Assert.Equal(ScreenLinkKind.Web, link.Kind);
        Assert.StartsWith("xn--", link.Label);
    }

    [Fact]
    public void User_info_cannot_disguise_the_real_host()
    {
        Assert.Equal("evil.example", ScreenLink.Classify("https://google.com@evil.example/").Label);
    }

    [Theory]
    [InlineData("tg://login?token=abc")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("https://exa mple.com")]
    [InlineData("just some words")]
    public void Anything_else_is_only_copied(string content)
    {
        var link = ScreenLink.Classify(content);

        Assert.Equal(ScreenLinkKind.Text, link.Kind);
        Assert.Null(link.Target);
        Assert.Equal(content, link.CopyText);
    }

    [Theory]
    [InlineData("tg://login?token=abc")]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("ms-settings:privacy")]
    public void Only_web_and_mail_uris_can_be_opened(string uri)
    {
        Assert.False(ScreenLink.CanOpen(new Uri(uri)));
    }

    [Fact]
    public void Mailto_opens_and_copies_the_address()
    {
        var link = ScreenLink.Classify("mailto:hello@example.com?subject=Hi");

        Assert.Equal(ScreenLinkKind.Email, link.Kind);
        Assert.Equal("hello@example.com", link.Label);
        Assert.Equal("hello@example.com", link.CopyText);
        Assert.True(ScreenLink.CanOpen(link.Target!));
    }

    [Fact]
    public void Wifi_code_copies_the_password_and_shows_the_network()
    {
        var link = ScreenLink.Classify("WIFI:T:WPA;S:Cafe\\;Guest;P:pa\\:ss\\\\word;H:false;;");

        Assert.Equal(ScreenLinkKind.Wifi, link.Kind);
        Assert.Equal("Cafe;Guest", link.Label);
        Assert.Equal("pa:ss\\word", link.CopyText);
        Assert.Null(link.Target);
    }

    [Fact]
    public void Wifi_fields_can_come_in_any_order()
    {
        var link = ScreenLink.Classify("WIFI:P:secret;S:Home;T:WPA;;");

        Assert.Equal(("Home", "secret"), (link.Label, link.CopyText));
    }

    [Theory]
    [InlineData("otpauth://totp/Example:alice%40example.com?secret=JBSWY3DPEHPK3PXP&issuer=Example",
        "Example:alice@example.com")]
    [InlineData("OTPAUTH://hotp/?secret=JBSWY3DPEHPK3PXP", "hotp")]
    public void Two_factor_code_is_a_secret_labeled_without_its_key(string content, string label)
    {
        var link = ScreenLink.Classify(content);

        Assert.Equal(ScreenLinkKind.Secret, link.Kind);
        Assert.Equal(label, link.Label);
        Assert.DoesNotContain("JBSWY3DPEHPK3PXP", link.Label);
        Assert.Equal(content, link.CopyText);
        Assert.Null(link.Target);
    }

    [Theory]
    [InlineData("github.com", "github.com")]
    [InlineData("login.microsoft.com.account-verify-secure.evil.example", "...com.account-verify-secure.evil.example")]
    [InlineData("xn--80ak6aa92e.xn--p1ai", "xn--80ak6aa92e.xn--p1ai")]
    [InlineData("a1234567890123456789012345678901234567890123456789.example", "...example")]
    public void Long_host_keeps_its_registrable_end(string host, string label)
    {
        Assert.Equal(label, CircleToSearch.Capture.QrCodeVisualFactory.ShortenHost(host));
    }

    [Fact]
    public void Open_wifi_network_copies_its_name()
    {
        var link = ScreenLink.Classify("WIFI:T:nopass;S:Library;;");

        Assert.Equal(ScreenLinkKind.Text, link.Kind);
        Assert.Equal("Library", link.CopyText);
    }
}

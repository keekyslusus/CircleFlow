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

    [Theory]
    [InlineData("https://github.com/keekyslusus", "github.com", "https://github.com/keekyslusus")]
    [InlineData("github.com", "github.com", "https://github.com/")]
    [InlineData("Example.com/path?q=1", "example.com", "https://example.com/path?q=1")]
    [InlineData("www.example.org", "example.org", "https://www.example.org/")]
    [InlineData("t.me/durov", "t.me", "https://t.me/durov")]
    [InlineData("docs.rs/serde", "docs.rs", "https://docs.rs/serde")]
    [InlineData("amazon.in/deals", "amazon.in", "https://amazon.in/deals")]
    [InlineData("notion.so/templates", "notion.so", "https://notion.so/templates")]
    [InlineData("example. com", "example.com", "https://example.com/")]
    [InlineData("https: //example.com / docs", "example.com", "https://example.com/docs")]
    [InlineData("https://example.com/very/long/\r\npath", "example.com", "https://example.com/very/long/path")]
    [InlineData("(example.com).", "example.com", "https://example.com/")]
    [InlineData("«ya.ru»,", "ya.ru", "https://ya.ru/")]
    [InlineData("https://en.wikipedia.org/wiki/Foo_(bar).", "en.wikipedia.org", "https://en.wikipedia.org/wiki/Foo_(bar)")]
    public void Recognized_link_opens_its_host(string text, string host, string target)
    {
        var link = ScreenLink.FromRecognizedText(text);

        Assert.NotNull(link);
        Assert.Equal(ScreenLinkKind.Web, link.Kind);
        Assert.Equal(host, link.Label);
        Assert.Equal(target, link.Target!.AbsoluteUri);
        Assert.True(ScreenLink.CanOpen(link.Target));
    }

    [Theory]
    [InlineData("hello@example.com", "hello@example.com")]
    [InlineData("john . doe @ example . com", "john.doe@example.com")]
    [InlineData("<support@example.co.uk>.", "support@example.co.uk")]
    [InlineData("mailto:hello@example.com", "hello@example.com")]
    public void Recognized_address_opens_as_mail(string text, string address)
    {
        var link = ScreenLink.FromRecognizedText(text);

        Assert.NotNull(link);
        Assert.Equal(ScreenLinkKind.Email, link.Kind);
        Assert.Equal(address, link.Label);
        Assert.Equal(Uri.UriSchemeMailto, link.Target!.Scheme);
        Assert.Equal("mailto:" + address, link.Target.OriginalString);
    }

    [Fact]
    public void Recognized_non_ascii_host_is_shown_in_punycode()
    {
        var link = ScreenLink.FromRecognizedText("пример.рф");

        Assert.Equal("xn--e1afmkfd.xn--p1ai", link?.Label);
    }

    [Theory]
    [InlineData("examp1e.com", "examp1e.com")]
    [InlineData("rnicrosoft.com", "rnicrosoft.com")]
    [InlineData("g00gle.com", "g00gle.com")]
    public void Recognized_look_alike_characters_are_shown_as_read(string text, string host)
    {
        Assert.Equal(host, ScreenLink.FromRecognizedText(text)?.Label);
    }

    [Theory]
    [InlineData("report.pdf")]
    [InlineData("main.cs")]
    [InlineData("Program.cs")]
    [InlineData("README.md")]
    [InlineData("script.py")]
    [InlineData("main.rs")]
    [InlineData("install.sh")]
    [InlineData("libc.so")]
    [InlineData("Makefile.in")]
    [InlineData("archive.zip")]
    [InlineData("video.mov")]
    [InlineData("index.html")]
    [InlineData("app.js")]
    [InlineData("1.2.3")]
    [InlineData("v2.0")]
    [InlineData("3.14")]
    [InlineData("10.0.19041")]
    [InlineData("192.168.1.1")]
    [InlineData("e.g.")]
    [InlineData("i.e.")]
    [InlineData("U.S.")]
    [InlineData("etc.")]
    [InlineData("Hello. World")]
    [InlineData("Done. Co")]
    [InlineData("Hello world. This is fine.")]
    [InlineData("Visit example.com")]
    [InlineData("example.com and more")]
    [InlineData("Contact: hello@example.com")]
    [InlineData("admin@server")]
    [InlineData("localhost:3000")]
    [InlineData(@"C:\Windows\notepad.exe")]
    [InlineData("tg://login?token=abc")]
    [InlineData("javascript:alert(1)")]
    [InlineData("WIFI:T:WPA;S:Home;P:secret;;")]
    [InlineData("just some words")]
    [InlineData("...")]
    [InlineData("   ")]
    public void Recognized_text_that_is_not_one_link_is_not_opened(string text)
    {
        Assert.Null(ScreenLink.FromRecognizedText(text));
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
        Assert.Equal(label, CircleToSearch.Capture.LinkActionContent.ShortenHost(host));
    }

    [Fact]
    public void Open_wifi_network_copies_its_name()
    {
        var link = ScreenLink.Classify("WIFI:T:nopass;S:Library;;");

        Assert.Equal(ScreenLinkKind.Text, link.Kind);
        Assert.Equal("Library", link.CopyText);
    }
}

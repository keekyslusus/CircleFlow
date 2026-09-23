using System.Text;
using CircleToSearch.Capture;
using CircleToSearch.Search;
using CircleToSearch.Search.Browser;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class GoogleLensBrowserOperationTests
{
    [Fact]
    public async Task Direct_upload_success_does_not_use_the_page_script()
    {
        var session = new FakeBrowserSession
        {
            CurrentUri = new Uri("https://lens.google.com/search?p=abc"),
        };
        session.Navigations.Enqueue(BrowserNavigationResult.Succeeded());

        var result = await NewOperation().ExecuteAsync(session, CancellationToken.None);

        Assert.Equal(VisualSearchBrowserOperationStatus.Succeeded, result);
        Assert.Equal(1, session.PostNavigations);
        Assert.Equal(0, session.GetNavigations);
        Assert.Equal(0, session.ScriptCalls);
        Assert.Equal("POST", session.Events[0]);
    }

    [Fact]
    public async Task Direct_upload_failure_uses_page_script_and_validates_result_url()
    {
        var session = new FakeBrowserSession
        {
            CurrentUri = new Uri("https://www.google.com/search?udm=26&q=image"),
            Message = "CTS:submitted",
        };
        session.Navigations.Enqueue(BrowserNavigationResult.Failed("ConnectionReset"));
        session.Navigations.Enqueue(BrowserNavigationResult.Succeeded());
        session.Navigations.Enqueue(BrowserNavigationResult.Succeeded());

        var result = await NewOperation().ExecuteAsync(session, CancellationToken.None);

        Assert.Equal(VisualSearchBrowserOperationStatus.Succeeded, result);
        Assert.Equal(["POST", "GET", "SCRIPT", "WAIT", "MESSAGE"], session.Events);
        Assert.Equal(1, session.ScriptCalls);
        Assert.Equal(1, session.MessageCalls);
    }

    [Fact]
    public async Task Both_upload_paths_failing_returns_failed()
    {
        var session = new FakeBrowserSession();
        session.Navigations.Enqueue(BrowserNavigationResult.Failed());
        session.Navigations.Enqueue(BrowserNavigationResult.Failed());

        var result = await NewOperation().ExecuteAsync(session, CancellationToken.None);

        Assert.Equal(VisualSearchBrowserOperationStatus.Failed, result);
        Assert.Equal(0, session.ScriptCalls);
    }

    [Fact]
    public async Task Cancellation_after_direct_upload_does_not_start_page_fallback()
    {
        using var cancellation = new CancellationTokenSource();
        var session = new FakeBrowserSession
        {
            OnPostNavigation = cancellation.Cancel,
        };
        session.Navigations.Enqueue(BrowserNavigationResult.Canceled());

        var result = await NewOperation().ExecuteAsync(session, cancellation.Token);

        Assert.Equal(VisualSearchBrowserOperationStatus.Canceled, result);
        Assert.Equal(0, session.GetNavigations);
        Assert.Equal(0, session.ScriptCalls);
    }

    [Fact]
    public async Task Operation_can_only_run_once_and_releases_its_image()
    {
        var session = new FakeBrowserSession
        {
            CurrentUri = new Uri("https://lens.google.com/search?p=abc"),
        };
        session.Navigations.Enqueue(BrowserNavigationResult.Succeeded());
        var operation = NewOperation();

        Assert.Equal(
            VisualSearchBrowserOperationStatus.Succeeded,
            await operation.ExecuteAsync(session, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            operation.ExecuteAsync(session, CancellationToken.None));
    }

    [Fact]
    public async Task Question_continues_lens_results_in_ai_mode_with_the_page_token()
    {
        var session = new FakeBrowserSession
        {
            CurrentUri = new Uri(LensResults),
            ScriptResult = "\"token-1\"",
        };
        session.Navigations.Enqueue(BrowserNavigationResult.Succeeded());
        session.Navigations.Enqueue(BrowserNavigationResult.Succeeded());

        var result = await new GoogleLensBrowserOperation(CreateJpeg(), NewLog(), "What is red?")
            .ExecuteAsync(session, CancellationToken.None);

        Assert.Equal(VisualSearchBrowserOperationStatus.Succeeded, result);
        Assert.Equal(["POST", "SCRIPT", "GET"], session.Events);
        var target = Assert.Single(session.GetTargets);
        Assert.Contains("mstk=token-1", target.Query, StringComparison.Ordinal);
        Assert.Contains("mq=What%20is%20red%3F", target.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Question_keeps_lens_results_when_they_cannot_continue_in_ai_mode()
    {
        var session = new FakeBrowserSession
        {
            CurrentUri = new Uri("https://lens.google.com/search?p=abc"),
            ScriptResult = "\"token-1\"",
        };
        session.Navigations.Enqueue(BrowserNavigationResult.Succeeded());

        var result = await new GoogleLensBrowserOperation(CreateJpeg(), NewLog(), "What is red?")
            .ExecuteAsync(session, CancellationToken.None);

        Assert.Equal(VisualSearchBrowserOperationStatus.Succeeded, result);
        Assert.Equal(0, session.GetNavigations);
    }

    [Fact]
    public async Task Failed_ai_mode_navigation_fails_the_operation()
    {
        var session = new FakeBrowserSession
        {
            CurrentUri = new Uri(LensResults),
            ScriptResult = "\"token-1\"",
        };
        session.Navigations.Enqueue(BrowserNavigationResult.Succeeded());
        session.Navigations.Enqueue(BrowserNavigationResult.Failed("ConnectionReset"));

        var result = await new GoogleLensBrowserOperation(CreateJpeg(), NewLog(), "What is red?")
            .ExecuteAsync(session, CancellationToken.None);

        Assert.Equal(VisualSearchBrowserOperationStatus.Failed, result);
    }

    [Fact]
    public void Ai_mode_url_keeps_image_context_and_replaces_follow_up_fields()
    {
        var uri = GoogleLensBrowserOperation.CreateAiModeUri(
            new Uri(LensResults + "#frag"), "a-b_c", "Какого цвета круг? + & # test")!;

        Assert.Equal("https://www.google.com/search", uri.GetLeftPart(UriPartial.Path));
        Assert.Empty(uri.Fragment);
        var parameters = uri.Query.TrimStart('?').Split('&').Select(part => part.Split('=', 2))
            .ToLookup(pair => pair[0], pair => Uri.UnescapeDataString(pair[1]));
        Assert.Equal("CAIQ", Assert.Single(parameters["vsrid"]));
        Assert.Equal("sess", Assert.Single(parameters["gsessionid"]));
        Assert.Equal("un", Assert.Single(parameters["lns_mode"]));
        Assert.Equal("50", Assert.Single(parameters["udm"]));
        Assert.Equal("", Assert.Single(parameters["q"]));
        Assert.Equal("Какого цвета круг? + & # test", Assert.Single(parameters["mq"]));
        Assert.Equal("a-b_c", Assert.Single(parameters["mstk"]));
        Assert.Empty(parameters["source"]);
        Assert.Equal(["10", "1", "1", "1", "0"],
            new[] { "aep", "ntc", "aioh", "csuir", "cs" }.Select(name => Assert.Single(parameters[name])));
    }

    [Theory]
    [InlineData("https://www.google.com/sorry/index?continue=x", true)]
    [InlineData("https://google.com/sorry/index", true)]
    [InlineData("https://www.google.com/search?q=sorry", false)]
    [InlineData("https://example.com/sorry/index", false)]
    public void Traffic_check_is_recognized_only_on_google(string url, bool expected)
        => Assert.Equal(expected, GoogleLensBrowserOperation.IsGoogleTrafficCheck(new Uri(url)));

    [Theory]
    [InlineData("https://lens.google.com/search?vsrid=a&gsessionid=b")]
    [InlineData("http://www.google.com/search?vsrid=a&gsessionid=b")]
    [InlineData("https://www.google.com/search?gsessionid=b")]
    [InlineData("https://www.google.com/search?vsrid=a")]
    [InlineData("https://example.com/search?vsrid=a&gsessionid=b")]
    public void Ai_mode_url_requires_google_search_image_context(string url)
        => Assert.Null(GoogleLensBrowserOperation.CreateAiModeUri(new Uri(url), "token", "question"));

    [Theory]
    [InlineData("https://lens.google.com/search?p=abc", true)]
    [InlineData("https://www.google.com/search?udm=26", true)]
    [InlineData("https://google.com/search?q=x&udm=26", true)]
    [InlineData("https://lens.google.com/", false)]
    [InlineData("https://www.google.com/search?q=x", false)]
    [InlineData("http://lens.google.com/search", false)]
    [InlineData("https://example.com/search?udm=26", false)]
    public void Results_url_policy_accepts_only_real_lens_results(string url, bool expected)
        => Assert.Equal(expected, GoogleLensBrowserOperation.IsGoogleLensResultsUrl(new Uri(url)));

    [Fact]
    public void Direct_upload_builds_the_expected_raw_jpeg_multipart_request()
    {
        const string boundary = "----CircleToSearchTestBoundary";
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0xFF, 0x0D, 0x0A];

        using var body = GoogleLensBrowserOperation.CreateLensUploadBody(jpeg, boundary);

        var prefix = Encoding.ASCII.GetBytes(
            $"--{boundary}\r\n" +
            "Content-Disposition: form-data; name=\"encoded_image\"; filename=\"circle-to-search.jpg\"\r\n" +
            "Content-Type: image/jpeg\r\n\r\n");
        var suffix = Encoding.ASCII.GetBytes($"\r\n--{boundary}--\r\n");
        Assert.Equal(prefix.Concat(jpeg).Concat(suffix), body.ToArray());
        Assert.Equal(0, body.Position);
        Assert.Equal(
            $"Content-Type: multipart/form-data; boundary={boundary}",
            GoogleLensBrowserOperation.CreateLensUploadHeaders(boundary));
    }

    [Theory]
    [InlineData("bad\rboundary")]
    [InlineData("bad\nboundary")]
    public void Direct_upload_rejects_a_boundary_with_line_breaks(string boundary)
    {
        Assert.Throws<ArgumentException>(() =>
            GoogleLensBrowserOperation.CreateLensUploadBody([1, 2, 3], boundary));
    }

    private const string LensResults =
        "https://www.google.com/search?vsrid=CAIQ&udm=26&lns_mode=un&source=lns.web.ukn&gsessionid=sess&lns_surface=26";

    private static GoogleLensBrowserOperation NewOperation()
        => new(CreateJpeg(), NewLog());

    private static byte[] CreateJpeg()
    {
        using var bitmap = new System.Drawing.Bitmap(2, 2);
        return ImageCropper.EncodeJpeg(bitmap, new System.Drawing.Rectangle(0, 0, 2, 2), 1600);
    }

    private static PluginLog NewLog()
    {
        var directory = Path.Combine(Path.GetTempPath(), "CircleToSearch.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return new PluginLog(directory);
    }
}

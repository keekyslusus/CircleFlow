using System.Text;
using CircleToSearch.Search;
using CircleToSearch.Search.Browser;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class GoogleLensBrowserOperationTests
{
    [Fact]
    public async Task Direct_upload_success_does_not_use_the_page_script()
    {
        var session = new FakeSession
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
        var session = new FakeSession
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
        var session = new FakeSession();
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
        var session = new FakeSession
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
        var session = new FakeSession
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

    private static GoogleLensBrowserOperation NewOperation()
        => new(CreatePng(), NewLog());

    private static byte[] CreatePng()
    {
        using var bitmap = new System.Drawing.Bitmap(2, 2);
        using var stream = new MemoryStream();
        bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        return stream.ToArray();
    }

    private static PluginLog NewLog()
    {
        var directory = Path.Combine(Path.GetTempPath(), "CircleToSearch.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return new PluginLog(directory);
    }

    private sealed class FakeSession : IVisualSearchBrowserSession
    {
        public Queue<BrowserNavigationResult> Navigations { get; } = new();
        public List<string> Events { get; } = [];
        public Uri? CurrentUri { get; set; }
        public string ScriptResult { get; set; } = "\"ready\"";
        public string? Message { get; set; }
        public Action? OnPostNavigation { get; set; }
        public int PostNavigations { get; private set; }
        public int GetNavigations { get; private set; }
        public int ScriptCalls { get; private set; }
        public int MessageCalls { get; private set; }

        public Task<BrowserNavigationResult> NavigateAsync(
            Uri target,
            TimeSpan timeout,
            CancellationToken cancel)
        {
            Events.Add("GET");
            GetNavigations++;
            return Task.FromResult(Navigations.Dequeue());
        }

        public Task<BrowserNavigationResult> NavigatePostAsync(
            Uri target,
            Stream body,
            string headers,
            TimeSpan timeout,
            CancellationToken cancel)
        {
            Events.Add("POST");
            PostNavigations++;
            OnPostNavigation?.Invoke();
            return Task.FromResult(Navigations.Dequeue());
        }

        public Task<BrowserNavigationResult> WaitForNavigationAsync(
            TimeSpan timeout,
            CancellationToken cancel)
        {
            Events.Add("WAIT");
            return Task.FromResult(Navigations.Dequeue());
        }

        public Task<string> ExecuteScriptAsync(string script, CancellationToken cancel)
        {
            Events.Add("SCRIPT");
            ScriptCalls++;
            return Task.FromResult(ScriptResult);
        }

        public Task<string?> PostWebMessageAndWaitAsync(
            string message,
            Func<string, bool> predicate,
            TimeSpan timeout,
            CancellationToken cancel)
        {
            Events.Add("MESSAGE");
            MessageCalls++;
            return Task.FromResult(Message);
        }
    }
}

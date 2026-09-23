using System.Text.Json;
using CircleToSearch.Search;
using CircleToSearch.Search.Browser;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class GoogleAiModeBrowserOperationTests
{
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x01];

    [Fact]
    public async Task Submits_image_and_question_through_the_ai_mode_page()
    {
        var session = new FakeBrowserSession
        {
            CurrentUri = new Uri("https://www.google.com/search?udm=50"),
            Message = "CTS:submitted",
        };
        session.Navigations.Enqueue(BrowserNavigationResult.Succeeded());
        var fallback = new FakeOperation();

        var result = await NewOperation(fallback, "Кто это? + & #").ExecuteAsync(session, CancellationToken.None);

        Assert.Equal(VisualSearchBrowserOperationStatus.Succeeded, result);
        Assert.Equal(["GET", "SCRIPT", "MESSAGE"], session.Events);
        Assert.Equal("https://www.google.com/search?udm=50", Assert.Single(session.GetTargets).AbsoluteUri);
        using var message = JsonDocument.Parse(session.SentMessage!);
        Assert.Equal(Convert.ToBase64String(Jpeg), message.RootElement.GetProperty("image").GetString());
        Assert.Equal("Кто это? + & #", message.RootElement.GetProperty("question").GetString());
        Assert.Equal(0, fallback.Calls);
    }

    [Theory]
    [InlineData("CTS:send-timeout")]
    [InlineData("CTS:input-missing")]
    [InlineData("CTS:paste-ignored")]
    [InlineData(null)]
    public async Task Unaccepted_question_falls_back_to_the_lens_continuation(string? acknowledgement)
    {
        var session = new FakeBrowserSession
        {
            CurrentUri = new Uri("https://www.google.com/search?udm=50"),
            Message = acknowledgement,
        };
        session.Navigations.Enqueue(BrowserNavigationResult.Succeeded());
        var fallback = new FakeOperation { Result = VisualSearchBrowserOperationStatus.Failed };

        var result = await NewOperation(fallback).ExecuteAsync(session, CancellationToken.None);

        Assert.Equal(VisualSearchBrowserOperationStatus.Failed, result);
        Assert.Equal(1, fallback.Calls);
    }

    [Fact]
    public async Task Failed_page_navigation_or_bridge_install_falls_back()
    {
        var failedNavigation = new FakeBrowserSession();
        failedNavigation.Navigations.Enqueue(BrowserNavigationResult.Failed("ConnectionReset"));
        var failedBridge = new FakeBrowserSession
        {
            CurrentUri = new Uri("https://www.google.com/search?udm=50"),
            ScriptResult = "null",
        };
        failedBridge.Navigations.Enqueue(BrowserNavigationResult.Succeeded());

        foreach (var session in new[] { failedNavigation, failedBridge })
        {
            var fallback = new FakeOperation();
            Assert.Equal(
                VisualSearchBrowserOperationStatus.Succeeded,
                await NewOperation(fallback).ExecuteAsync(session, CancellationToken.None));
            Assert.Equal(1, fallback.Calls);
            Assert.Equal(0, session.MessageCalls);
        }
    }

    [Fact]
    public async Task Lost_acknowledgement_after_reaching_the_answer_does_not_ask_twice()
    {
        var session = new FakeBrowserSession
        {
            CurrentUri = new Uri("https://www.google.com/search?udm=50"),
            Message = null,
        };
        session.OnMessage = () => session.CurrentUri = new Uri("https://www.google.com/search?udm=50&q=x&vsrid=abc");
        session.Navigations.Enqueue(BrowserNavigationResult.Succeeded());
        var fallback = new FakeOperation();

        var result = await NewOperation(fallback).ExecuteAsync(session, CancellationToken.None);

        Assert.Equal(VisualSearchBrowserOperationStatus.Succeeded, result);
        Assert.Equal(0, fallback.Calls);
    }

    [Fact]
    public async Task Waits_for_the_user_to_pass_the_traffic_check_before_asking()
    {
        var session = new FakeBrowserSession
        {
            CurrentUri = new Uri("https://www.google.com/sorry/index?continue=x"),
            Message = "CTS:submitted",
        };
        session.OnWait = () => session.CurrentUri = new Uri("https://www.google.com/search?udm=50");
        session.Navigations.Enqueue(BrowserNavigationResult.Succeeded());
        session.Navigations.Enqueue(BrowserNavigationResult.Succeeded());

        var result = await NewOperation(new FakeOperation()).ExecuteAsync(session, CancellationToken.None);

        Assert.Equal(VisualSearchBrowserOperationStatus.Succeeded, result);
        Assert.Equal(["GET", "WAIT", "SCRIPT", "MESSAGE"], session.Events);
    }

    [Fact]
    public async Task Cancellation_does_not_start_the_fallback()
    {
        using var cancellation = new CancellationTokenSource();
        var session = new FakeBrowserSession();
        session.Navigations.Enqueue(BrowserNavigationResult.Canceled());
        var fallback = new FakeOperation();

        var result = await NewOperation(fallback).ExecuteAsync(session, cancellation.Token);

        Assert.Equal(VisualSearchBrowserOperationStatus.Canceled, result);
        Assert.Equal(0, fallback.Calls);
    }

    [Fact]
    public async Task Operation_can_only_run_once()
    {
        var session = new FakeBrowserSession { Message = "CTS:submitted" };
        session.Navigations.Enqueue(BrowserNavigationResult.Succeeded());
        var operation = NewOperation(new FakeOperation());

        await operation.ExecuteAsync(session, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            operation.ExecuteAsync(session, CancellationToken.None));
    }

    [Theory]
    [InlineData("https://www.google.com/search?udm=50&q=x&vsrid=abc&mstk=t", true)]
    [InlineData("https://google.com/search?vsrid=abc&udm=50", true)]
    [InlineData("https://www.google.com/search?udm=50", false)]
    [InlineData("https://www.google.com/search?udm=26&vsrid=abc", false)]
    [InlineData("https://example.com/search?udm=50&vsrid=abc", false)]
    public void Ai_mode_answer_requires_an_attached_image(string url, bool expected)
        => Assert.Equal(expected, GoogleAiModeBrowserOperation.IsAiModeAnswer(new Uri(url)));

    private static GoogleAiModeBrowserOperation NewOperation(FakeOperation fallback, string question = "What is this?")
        => new(Jpeg, question, fallback, NewLog());

    private static PluginLog NewLog()
    {
        var directory = Path.Combine(Path.GetTempPath(), "CircleToSearch.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return new PluginLog(directory);
    }

    private sealed class FakeOperation : IVisualSearchBrowserOperation
    {
        public int Calls { get; private set; }
        public VisualSearchBrowserOperationStatus Result { get; init; } = VisualSearchBrowserOperationStatus.Succeeded;

        public Task<VisualSearchBrowserOperationStatus> ExecuteAsync(
            IVisualSearchBrowserSession session,
            CancellationToken cancel)
        {
            Calls++;
            return Task.FromResult(Result);
        }
    }
}

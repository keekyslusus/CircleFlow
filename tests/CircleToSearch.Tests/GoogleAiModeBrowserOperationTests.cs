using System.Text.Json;
using CircleToSearch.Search;
using CircleToSearch.Search.Browser;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class GoogleAiModeBrowserOperationTests
{
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x01];
    private static readonly Uri AiMode = new("https://www.google.com/search?udm=50");

    [Fact]
    public async Task Loads_page_before_the_image_and_sends_the_question_last()
    {
        var session = new FakeBrowserSession { CurrentUri = AiMode };
        session.Navigations.Enqueue(BrowserNavigationResult.Succeeded());
        session.Messages.Enqueue("CTS:attached:1");
        session.Messages.Enqueue("CTS:submitted");
        var image = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var question = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var fallback = new FakeFallback();

        var execution = new GoogleAiModeBrowserOperation(image.Task, question.Task, fallback.Create, NewLog())
            .ExecuteAsync(session, CancellationToken.None);
        await WaitUntilAsync(() => session.Events.Count == 1);
        Assert.Equal(["GET"], session.Events);
        Assert.Equal(AiMode, Assert.Single(session.GetTargets));

        image.SetResult(Jpeg);
        await WaitUntilAsync(() => session.MessageCalls == 1);
        Assert.False(execution.IsCompleted);
        using (var attach = JsonDocument.Parse(session.SentMessages[0]))
        {
            Assert.Equal("attach", attach.RootElement.GetProperty("type").GetString());
            Assert.Equal(Convert.ToBase64String(Jpeg), attach.RootElement.GetProperty("image").GetString());
        }

        question.SetResult("Кто это? + & #");
        Assert.Equal(VisualSearchBrowserOperationStatus.Succeeded, await execution.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(["GET", "SCRIPT", "MESSAGE", "SCRIPT", "MESSAGE"], session.Events);
        using var send = JsonDocument.Parse(session.SentMessages[1]);
        Assert.Equal("send", send.RootElement.GetProperty("type").GetString());
        Assert.Equal("Кто это? + & #", send.RootElement.GetProperty("question").GetString());
        Assert.Empty(fallback.Calls);
    }

    [Theory]
    [InlineData("CTS:paste-ignored", "CTS:submitted")]
    [InlineData("CTS:input-missing", "CTS:submitted")]
    [InlineData("CTS:attached", "CTS:send-timeout")]
    [InlineData("CTS:attached", null)]
    [InlineData(null, "CTS:submitted")]
    public async Task Unaccepted_step_falls_back_with_the_same_image_and_question(
        string? attachReply, string? sendReply)
    {
        var session = new FakeBrowserSession { CurrentUri = AiMode };
        session.Navigations.Enqueue(BrowserNavigationResult.Succeeded());
        session.Messages.Enqueue(attachReply);
        session.Messages.Enqueue(sendReply);
        var fallback = new FakeFallback { Result = VisualSearchBrowserOperationStatus.Failed };

        var result = await NewOperation(fallback).ExecuteAsync(session, CancellationToken.None);

        Assert.Equal(VisualSearchBrowserOperationStatus.Failed, result);
        var (image, question) = Assert.Single(fallback.Calls);
        Assert.Same(Jpeg, image);
        Assert.Equal("What is this?", question);
        Assert.Equal(2, session.MessageCalls);
    }

    [Fact]
    public async Task Image_the_page_ignored_while_loading_is_attached_again_after_the_question()
    {
        var session = new FakeBrowserSession { CurrentUri = AiMode };
        session.Navigations.Enqueue(BrowserNavigationResult.Succeeded());
        session.Messages.Enqueue("CTS:paste-ignored");
        session.Messages.Enqueue("CTS:attached");
        session.Messages.Enqueue("CTS:submitted");
        var fallback = new FakeFallback();

        var result = await NewOperation(fallback).ExecuteAsync(session, CancellationToken.None);

        Assert.Equal(VisualSearchBrowserOperationStatus.Succeeded, result);
        Assert.Equal(["attach", "attach", "send"], session.SentMessages.Select(message =>
            JsonDocument.Parse(message).RootElement.GetProperty("type").GetString()));
        Assert.Empty(fallback.Calls);
    }

    [Fact]
    public async Task Failed_page_navigation_or_bridge_install_falls_back()
    {
        var failedNavigation = new FakeBrowserSession();
        failedNavigation.Navigations.Enqueue(BrowserNavigationResult.Failed("ConnectionReset"));
        var failedBridge = new FakeBrowserSession { CurrentUri = AiMode, ScriptResult = "null" };
        failedBridge.Navigations.Enqueue(BrowserNavigationResult.Succeeded());

        foreach (var session in new[] { failedNavigation, failedBridge })
        {
            var fallback = new FakeFallback();
            Assert.Equal(
                VisualSearchBrowserOperationStatus.Succeeded,
                await NewOperation(fallback).ExecuteAsync(session, CancellationToken.None));
            Assert.Single(fallback.Calls);
            Assert.Equal(0, session.MessageCalls);
        }
    }

    [Fact]
    public async Task Lost_acknowledgement_after_reaching_the_answer_does_not_ask_twice()
    {
        var session = new FakeBrowserSession { CurrentUri = AiMode };
        session.Navigations.Enqueue(BrowserNavigationResult.Succeeded());
        session.Messages.Enqueue("CTS:attached");
        session.Messages.Enqueue(null);
        session.OnMessage = () =>
        {
            if (session.MessageCalls == 2)
                session.CurrentUri = new Uri("https://www.google.com/search?udm=50&q=x&vsrid=abc");
        };
        var fallback = new FakeFallback();

        var result = await NewOperation(fallback).ExecuteAsync(session, CancellationToken.None);

        Assert.Equal(VisualSearchBrowserOperationStatus.Succeeded, result);
        Assert.Empty(fallback.Calls);
    }

    [Fact]
    public async Task Traffic_check_waits_for_the_revealed_user_before_attaching()
    {
        var session = new FakeBrowserSession { CurrentUri = new Uri("https://www.google.com/sorry/index?continue=x") };
        session.OnWait = () => session.CurrentUri = AiMode;
        session.Navigations.Enqueue(BrowserNavigationResult.Succeeded());
        session.Navigations.Enqueue(BrowserNavigationResult.Succeeded());
        session.Messages.Enqueue("CTS:attached");
        session.Messages.Enqueue("CTS:submitted");

        var result = await NewOperation(new FakeFallback()).ExecuteAsync(session, CancellationToken.None);

        Assert.Equal(VisualSearchBrowserOperationStatus.Succeeded, result);
        Assert.Equal(["GET", "WAIT", "SCRIPT", "MESSAGE", "SCRIPT", "MESSAGE"], session.Events);
    }

    [Fact]
    public async Task Canceling_the_draft_before_the_question_does_not_start_the_fallback()
    {
        using var cancellation = new CancellationTokenSource();
        var session = new FakeBrowserSession { CurrentUri = AiMode, Message = "CTS:attached" };
        session.Navigations.Enqueue(BrowserNavigationResult.Succeeded());
        var question = new TaskCompletionSource<string>();
        var fallback = new FakeFallback();

        var execution = new GoogleAiModeBrowserOperation(Task.FromResult(Jpeg), question.Task, fallback.Create, NewLog())
            .ExecuteAsync(session, cancellation.Token);
        await WaitUntilAsync(() => session.MessageCalls == 1);
        cancellation.Cancel();

        Assert.Equal(VisualSearchBrowserOperationStatus.Canceled, await execution.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Empty(fallback.Calls);
    }

    [Fact]
    public async Task Operation_can_only_run_once()
    {
        var session = new FakeBrowserSession { CurrentUri = AiMode };
        session.Navigations.Enqueue(BrowserNavigationResult.Succeeded());
        session.Messages.Enqueue("CTS:attached");
        session.Messages.Enqueue("CTS:submitted");
        var operation = NewOperation(new FakeFallback());

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

    private static GoogleAiModeBrowserOperation NewOperation(FakeFallback fallback) =>
        new(Task.FromResult(Jpeg), Task.FromResult("What is this?"), fallback.Create, NewLog());

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 200 && !condition(); attempt++) await Task.Delay(10);
        Assert.True(condition());
    }

    private static PluginLog NewLog()
    {
        var directory = Path.Combine(Path.GetTempPath(), "CircleToSearch.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return new PluginLog(directory);
    }

    private sealed class FakeFallback : IVisualSearchBrowserOperation
    {
        public List<(byte[] Image, string Question)> Calls { get; } = [];
        public VisualSearchBrowserOperationStatus Result { get; init; } = VisualSearchBrowserOperationStatus.Succeeded;

        public IVisualSearchBrowserOperation Create(byte[] image, string question)
        {
            Calls.Add((image, question));
            return this;
        }

        public Task<VisualSearchBrowserOperationStatus> ExecuteAsync(
            IVisualSearchBrowserSession session,
            CancellationToken cancel)
            => Task.FromResult(Result);
    }
}

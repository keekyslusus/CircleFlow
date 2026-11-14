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
        session.Statuses.Enqueue("attached:1");
        session.Statuses.Enqueue("submitted");
        var image = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
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
    [InlineData("paste-ignored", "paste-ignored", 2)]
    [InlineData("input-missing", "submitted", 1)]
    [InlineData("attached", "send-timeout", 2)]
    [InlineData("attached", null, 2)]
    [InlineData(null, "script-error", 2)]
    public async Task Unaccepted_step_falls_back_with_the_same_image_and_question(
        string? attachReply, string? secondReply, int expectedMessages)
    {
        var session = new FakeBrowserSession { CurrentUri = AiMode };
        session.Navigations.Enqueue(BrowserNavigationResult.Succeeded());
        session.Statuses.Enqueue(attachReply);
        session.Statuses.Enqueue(secondReply);
        var fallback = new FakeFallback { Result = VisualSearchBrowserOperationStatus.Failed };

        var result = await NewOperation(fallback).ExecuteAsync(session, CancellationToken.None);

        Assert.Equal(VisualSearchBrowserOperationStatus.Failed, result);
        var (image, question) = Assert.Single(fallback.Calls);
        Assert.Same(Jpeg, image);
        Assert.Equal("What is this?", question);
        Assert.Equal(expectedMessages, session.MessageCalls);
    }

    [Fact]
    public async Task Late_reply_to_an_earlier_request_is_not_taken_for_the_current_one()
    {
        var session = new FakeBrowserSession { CurrentUri = AiMode };
        session.Navigations.Enqueue(BrowserNavigationResult.Succeeded());
        session.Messages.Enqueue("CTS:99:attached:1");
        session.Messages.Enqueue("CTS:2:attached:1");
        session.Messages.Enqueue("CTS:1:submitted");
        var fallback = new FakeFallback { Result = VisualSearchBrowserOperationStatus.Failed };

        var result = await NewOperation(fallback).ExecuteAsync(session, CancellationToken.None);

        Assert.Equal(VisualSearchBrowserOperationStatus.Failed, result);
        Assert.Equal(3, session.MessageCalls);
        Assert.Single(fallback.Calls);
    }

    [Fact]
    public async Task Image_the_page_ignored_while_loading_is_attached_again_after_the_question()
    {
        var session = new FakeBrowserSession { CurrentUri = AiMode };
        session.Navigations.Enqueue(BrowserNavigationResult.Succeeded());
        session.Statuses.Enqueue("paste-ignored");
        session.Statuses.Enqueue("attached");
        session.Statuses.Enqueue("submitted");
        var fallback = new FakeFallback();

        var result = await NewOperation(fallback).ExecuteAsync(session, CancellationToken.None);

        Assert.Equal(VisualSearchBrowserOperationStatus.Succeeded, result);
        Assert.Equal(["attach", "attach", "send"], session.SentMessages.Select(message =>
            JsonDocument.Parse(message).RootElement.GetProperty("type").GetString()));
        Assert.Empty(fallback.Calls);
    }

    [Fact]
    public async Task Repeated_attach_reuses_one_random_file_name_that_changes_per_operation()
    {
        async Task<string?[]> AttachNamesAsync()
        {
            var session = new FakeBrowserSession { CurrentUri = AiMode };
            session.Navigations.Enqueue(BrowserNavigationResult.Succeeded());
            session.Statuses.Enqueue("paste-ignored");
            session.Statuses.Enqueue("attached");
            session.Statuses.Enqueue("submitted");
            await NewOperation(new FakeFallback()).ExecuteAsync(session, CancellationToken.None);
            return session.SentMessages.Take(2)
                .Select(message => JsonDocument.Parse(message).RootElement.GetProperty("name").GetString())
                .ToArray();
        }

        var first = await AttachNamesAsync();
        var second = await AttachNamesAsync();

        Assert.Matches(@"^[0-9a-f]{12}\.jpg$", first[0]);
        Assert.Equal(first[0], first[1]);
        Assert.NotEqual(first[0], second[0]);
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
        session.Statuses.Enqueue("attached");
        session.Statuses.Enqueue(null);
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
        session.Statuses.Enqueue("attached");
        session.Statuses.Enqueue("submitted");

        var result = await NewOperation(new FakeFallback()).ExecuteAsync(session, CancellationToken.None);

        Assert.Equal(VisualSearchBrowserOperationStatus.Succeeded, result);
        Assert.Equal(["GET", "WAIT", "SCRIPT", "MESSAGE", "SCRIPT", "MESSAGE"], session.Events);
    }

    [Fact]
    public async Task Canceling_the_draft_before_the_question_does_not_start_the_fallback()
    {
        using var cancellation = new CancellationTokenSource();
        var session = new FakeBrowserSession { CurrentUri = AiMode };
        session.Statuses.Enqueue("attached");
        session.Navigations.Enqueue(BrowserNavigationResult.Succeeded());
        var question = new TaskCompletionSource<string>();
        var fallback = new FakeFallback();

        var execution = new GoogleAiModeBrowserOperation(Task.FromResult<byte[]?>(Jpeg), question.Task, fallback.Create, NewLog())
            .ExecuteAsync(session, cancellation.Token);
        await WaitUntilAsync(() => session.MessageCalls == 1);
        cancellation.Cancel();

        Assert.Equal(VisualSearchBrowserOperationStatus.Canceled, await execution.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Empty(fallback.Calls);
    }

    private static readonly Uri TextQuestion =
        new("https://www.google.com/search?udm=50&q=%D0%9A%D1%82%D0%BE%20%D1%8D%D1%82%D0%BE%3F%20%2B%20%26%20%23");

    [Fact]
    public async Task Question_without_an_image_is_typed_into_the_open_page()
    {
        var session = new FakeBrowserSession { CurrentUri = AiMode };
        session.Navigations.Enqueue(BrowserNavigationResult.Succeeded());
        session.Statuses.Enqueue("submitted");
        var fallback = new FakeFallback();

        var result = await NewTextOperation(fallback).ExecuteAsync(session, CancellationToken.None);

        Assert.Equal(VisualSearchBrowserOperationStatus.Succeeded, result);
        Assert.Equal([AiMode], session.GetTargets);
        using var send = JsonDocument.Parse(Assert.Single(session.SentMessages));
        Assert.Equal("send", send.RootElement.GetProperty("type").GetString());
        Assert.Equal("Кто это? + & #", send.RootElement.GetProperty("question").GetString());
        Assert.Empty(fallback.Calls);
    }

    [Fact]
    public async Task Lost_acknowledgement_after_a_text_answer_does_not_ask_twice()
    {
        var session = new FakeBrowserSession { CurrentUri = AiMode };
        session.Navigations.Enqueue(BrowserNavigationResult.Succeeded());
        session.Statuses.Enqueue(null);
        session.OnMessage = () => session.CurrentUri = TextQuestion;

        var result = await NewTextOperation(new FakeFallback()).ExecuteAsync(session, CancellationToken.None);

        Assert.Equal(VisualSearchBrowserOperationStatus.Succeeded, result);
        Assert.Equal([AiMode], session.GetTargets);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Lost_acknowledgement_waits_for_the_answer_address_before_falling_back(bool answerArrives)
    {
        var session = new FakeBrowserSession { CurrentUri = AiMode };
        session.Navigations.Enqueue(BrowserNavigationResult.Succeeded());
        session.Navigations.Enqueue(BrowserNavigationResult.Succeeded());
        session.Statuses.Enqueue(null);
        if (answerArrives)
            session.OnMessage = () => _ = Task.Delay(150).ContinueWith(_ => session.CurrentUri = TextQuestion);
        var operation = new GoogleAiModeBrowserOperation(Task.FromResult<byte[]?>(null),
            Task.FromResult("Кто это? + & #"), new FakeFallback().Create, NewLog(), TimeSpan.FromMilliseconds(600));

        var result = await operation.ExecuteAsync(session, CancellationToken.None);

        Assert.Equal(VisualSearchBrowserOperationStatus.Succeeded, result);
        Assert.Equal(answerArrives ? [AiMode] : [AiMode, TextQuestion], session.GetTargets);
    }

    [Fact]
    public async Task Traffic_check_before_a_text_question_waits_for_the_user_and_types_it_after()
    {
        var session = new FakeBrowserSession { CurrentUri = new Uri("https://www.google.com/sorry/index?continue=x") };
        session.OnWait = () => session.CurrentUri = AiMode;
        session.Navigations.Enqueue(BrowserNavigationResult.Succeeded());
        session.Navigations.Enqueue(BrowserNavigationResult.Succeeded());
        session.Statuses.Enqueue("submitted");

        var result = await NewTextOperation(new FakeFallback()).ExecuteAsync(session, CancellationToken.None);

        Assert.Equal(VisualSearchBrowserOperationStatus.Succeeded, result);
        Assert.Equal(["GET", "WAIT", "SCRIPT", "MESSAGE"], session.Events);
    }

    [Theory]
    [InlineData(true, BrowserNavigationStatus.Succeeded, VisualSearchBrowserOperationStatus.Succeeded)]
    [InlineData(true, BrowserNavigationStatus.Failed, VisualSearchBrowserOperationStatus.NavigationFailed)]
    [InlineData(false, BrowserNavigationStatus.Succeeded, VisualSearchBrowserOperationStatus.Succeeded)]
    public async Task Text_question_the_page_did_not_take_falls_back_to_its_address(
        bool pageOpened, BrowserNavigationStatus answer, VisualSearchBrowserOperationStatus expected)
    {
        var session = new FakeBrowserSession { CurrentUri = AiMode };
        session.Navigations.Enqueue(pageOpened ? BrowserNavigationResult.Succeeded() : BrowserNavigationResult.Failed("x"));
        session.Navigations.Enqueue(new BrowserNavigationResult(answer, "x"));
        session.Statuses.Enqueue("send-timeout");
        var fallback = new FakeFallback();

        var result = await NewTextOperation(fallback).ExecuteAsync(session, CancellationToken.None);

        Assert.Equal(expected, result);
        Assert.Equal([AiMode, TextQuestion], session.GetTargets);
        Assert.Equal(pageOpened ? 1 : 0, session.MessageCalls);
        Assert.Empty(fallback.Calls);
    }

    // Google answers the address with a traffic check that the browser reports as a failed load, yet the check
    // continues to the answer once the user solves it.
    [Fact]
    public async Task Text_address_answered_with_a_traffic_check_is_left_for_the_user()
    {
        var session = new FakeBrowserSession { CurrentUri = AiMode };
        session.Navigations.Enqueue(BrowserNavigationResult.Succeeded());
        session.Navigations.Enqueue(BrowserNavigationResult.Failed("Unknown"));
        session.Statuses.Enqueue("send-timeout");
        session.OnGetNavigation = target =>
        {
            if (target == TextQuestion) session.CurrentUri = new Uri("https://www.google.com/sorry/index?continue=x");
        };

        var result = await NewTextOperation(new FakeFallback()).ExecuteAsync(session, CancellationToken.None);

        Assert.Equal(VisualSearchBrowserOperationStatus.Succeeded, result);
    }

    [Theory]
    [InlineData("https://www.google.com/search?udm=50&q=x", true)]
    [InlineData("https://www.google.com/search?udm=50", false)]
    [InlineData("https://www.google.com/search?q=x", false)]
    public void Ai_mode_text_answer_requires_a_query(string url, bool expected)
        => Assert.Equal(expected, GoogleAiModeBrowserOperation.IsAiModeTextAnswer(new Uri(url)));

    private static GoogleAiModeBrowserOperation NewTextOperation(FakeFallback fallback) =>
        new(Task.FromResult<byte[]?>(null), Task.FromResult("Кто это? + & #"), fallback.Create, NewLog(), TimeSpan.Zero);

    [Fact]
    public async Task Operation_can_only_run_once()
    {
        var session = new FakeBrowserSession { CurrentUri = AiMode };
        session.Navigations.Enqueue(BrowserNavigationResult.Succeeded());
        session.Statuses.Enqueue("attached");
        session.Statuses.Enqueue("submitted");
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
        new(Task.FromResult<byte[]?>(Jpeg), Task.FromResult("What is this?"), fallback.Create, NewLog(), TimeSpan.Zero);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 200 && !condition(); attempt++) await Task.Delay(10);
        Assert.True(condition());
    }

    private static PluginLog NewLog()
    {
        var directory = Path.Combine(TestOutputPaths.TempDirectory, "CircleToSearch.Tests", Guid.NewGuid().ToString("N"));
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

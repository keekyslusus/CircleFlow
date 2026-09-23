using CircleToSearch.Search.Browser;

namespace CircleToSearch.Tests;

internal sealed class FakeBrowserSession : IVisualSearchBrowserSession
{
    public Queue<BrowserNavigationResult> Navigations { get; } = new();
    public List<string> Events { get; } = [];
    public Uri? CurrentUri { get; set; }
    public string ScriptResult { get; set; } = "\"ready\"";
    public string? Message { get; set; }
    public Queue<string?> Messages { get; } = new();
    // Replies to requests that carry an id, answered as the page bridge would: CTS:<id>:<status>.
    public Queue<string?> Statuses { get; } = new();
    public List<string> SentMessages { get; } = [];
    public string? SentMessage => SentMessages.LastOrDefault();
    public Action? OnPostNavigation { get; set; }
    public Action? OnWait { get; set; }
    public Action? OnMessage { get; set; }
    public int PostNavigations { get; private set; }
    public int GetNavigations { get; private set; }
    public int ScriptCalls { get; private set; }
    public int MessageCalls { get; private set; }
    public List<Uri> GetTargets { get; } = [];

    public Task<BrowserNavigationResult> NavigateAsync(
        Uri target,
        TimeSpan timeout,
        CancellationToken cancel)
    {
        Events.Add("GET");
        GetNavigations++;
        GetTargets.Add(target);
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
        OnWait?.Invoke();
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
        SentMessages.Add(message);
        OnMessage?.Invoke();
        var reply = Statuses.Count > 0 ? Tagged(message, Statuses.Dequeue())
            : Messages.Count > 0 ? Messages.Dequeue()
            : Message;
        return Task.FromResult(reply is not null && predicate(reply) ? reply : null);
    }

    private static string? Tagged(string message, string? status)
    {
        if (status is null) return null;
        using var request = System.Text.Json.JsonDocument.Parse(message);
        return $"CTS:{request.RootElement.GetProperty("id").GetInt32()}:{status}";
    }
}

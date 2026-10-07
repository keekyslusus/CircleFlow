namespace CircleToSearch.Shell.SettingsPreview;

internal sealed class SettingsNavigationHistory(string current)
{
    private const int MaxDepth = 32;
    private readonly List<string> _back = [];
    private readonly List<string> _forward = [];

    internal string Current { get; private set; } = current;

    internal void Visit(string page)
    {
        if (page == Current) return;
        Push(_back, Current);
        _forward.Clear();
        Current = page;
    }

    internal string? GoBack() => Move(_back, _forward);

    internal string? GoForward() => Move(_forward, _back);

    private string? Move(List<string> from, List<string> to)
    {
        if (from.Count == 0) return null;
        Push(to, Current);
        Current = from[^1];
        from.RemoveAt(from.Count - 1);
        return Current;
    }

    private static void Push(List<string> stack, string page)
    {
        stack.Add(page);
        if (stack.Count > MaxDepth) stack.RemoveAt(0);
    }
}

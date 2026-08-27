using CircleToSearch.Search;
using Flow.Launcher.Plugin;

namespace CircleToSearch.Trigger;

public sealed class QueryTrigger
{
    private readonly SearchCoordinator _coordinator;
    private readonly string _iconPath;
    private readonly Func<string> _hotkeyStatus;

    public QueryTrigger(SearchCoordinator coordinator, string iconPath, Func<string> hotkeyStatus)
    {
        _coordinator = coordinator;
        _iconPath = iconPath;
        _hotkeyStatus = hotkeyStatus;
    }

    public List<Result> Build(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];

        return
        [
            new Result
            {
                Title = "Select screen area…",
                SubTitle = $"Search the region directly with Google Lens — hotkey: {_hotkeyStatus()}",
                IcoPath = _iconPath,
                Score = 80,
                Action = context =>
                {
                    _ = _coordinator.StartFromQueryAsync();
                    return true;
                },
            },
        ];
    }
}

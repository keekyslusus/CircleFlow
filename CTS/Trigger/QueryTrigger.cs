using CircleToSearch.Search;
using CircleToSearch.Ui;
using Flow.Launcher.Plugin;

namespace CircleToSearch.Trigger;

public sealed class QueryTrigger
{
    private readonly SearchCoordinator _coordinator;
    private readonly string _iconPath;
    private readonly Func<string> _hotkeyStatus;
    private readonly UiStrings _strings;

    public QueryTrigger(
        SearchCoordinator coordinator,
        string iconPath,
        Func<string> hotkeyStatus,
        UiStrings strings)
    {
        _coordinator = coordinator;
        _iconPath = iconPath;
        _hotkeyStatus = hotkeyStatus;
        _strings = strings;
    }

    public List<Result> Build(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];

        return
        [
            new Result
            {
                Title = _strings.QueryTitle,
                SubTitle = _strings.VisualSearchQuerySubtitle(_hotkeyStatus()),
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

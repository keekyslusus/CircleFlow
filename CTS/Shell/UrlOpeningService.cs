using CircleToSearch.Ui;

namespace CircleToSearch.Shell;

internal sealed class UrlOpeningService
{
    private readonly Func<string, bool> _openUrl;
    private readonly IPluginNotifier _notifier;
    private readonly UiStrings _strings;
    private readonly PluginLog _log;

    internal UrlOpeningService(
        Func<string, bool> openUrl,
        IPluginNotifier notifier,
        UiStrings strings,
        PluginLog log)
    {
        _openUrl = openUrl;
        _notifier = notifier;
        _strings = strings;
        _log = log;
    }

    internal bool TryOpen(string url, string? failureMessage = null)
    {
        bool opened;
        try { opened = _openUrl(url); }
        catch (Exception exception)
        {
            _log.SafeError(nameof(UrlOpeningService), "open-url", exception);
            opened = false;
        }

        if (opened) return true;
        _notifier.ShowError(_strings.PluginTitle, failureMessage ?? _strings.ResultsUrlOpenFailed);
        return false;
    }
}

using CircleToSearch.Shell;
using CircleToSearch.Ui;

namespace CircleToSearch.Search;

internal sealed class TextSearchWorkflow(
    TextSearchUrlBuilder urlBuilder,
    Func<string> engineId,
    UrlOpeningService urlOpening,
    IPluginNotifier notifier,
    UiStrings strings,
    PluginLog log)
{
    public bool Execute(string text, string providerId)
    {
        var engine = engineId();
        string url;
        try { url = urlBuilder.Build(text, providerId, engine); }
        catch (TextSearchQueryTooLongException)
        {
            notifier.ShowError(strings.PluginTitle, strings.TextSearchTooLong);
            return false;
        }
        catch (ArgumentException exception)
        {
            log.Warn(nameof(TextSearchWorkflow), $"text search policy rejected the request: {exception.GetType().Name}");
            notifier.ShowError(strings.PluginTitle, strings.TextSearchOpenFailed);
            return false;
        }

        if (urlOpening.TryOpen(url, strings.TextSearchOpenFailed))
        {
            log.Info(nameof(TextSearchWorkflow), $"text search opened with provider '{providerId}', engine '{engine}'");
            return true;
        }
        return false;
    }
}

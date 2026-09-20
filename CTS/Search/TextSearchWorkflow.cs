using CircleToSearch.Shell;
using CircleToSearch.Ui;

namespace CircleToSearch.Search;

internal sealed class TextSearchWorkflow(
    TextSearchUrlBuilder urlBuilder,
    UrlOpeningService urlOpening,
    IPluginNotifier notifier,
    UiStrings strings,
    PluginLog log)
{
    public bool Execute(string text, string providerId)
    {
        string url;
        try { url = urlBuilder.Build(text, providerId); }
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
            log.Info(nameof(TextSearchWorkflow), $"text search opened with provider '{providerId}'");
            return true;
        }
        return false;
    }
}

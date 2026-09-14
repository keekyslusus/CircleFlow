using CircleToSearch.Ui;

namespace CircleToSearch.Shell;

internal sealed class ProjectSupport(Func<string, bool> openUrl, IPluginNotifier notifier, UiStrings strings)
{
    internal const string ProjectUrl = "https://ko-fi.com/keekys";

    public void Open()
    {
        if (!openUrl(ProjectUrl)) notifier.ShowError(strings.PluginTitle, strings.ResultsUrlOpenFailed);
    }
}

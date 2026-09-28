using CircleToSearch.Ui;

namespace CircleToSearch.Shell;

internal sealed class WebViewRuntimeNotice(
    Func<string?> runtimeVersion,
    IPluginNotifier notifier,
    UrlOpeningService urlOpening,
    UiStrings strings)
{
    internal const string DownloadUrl =
        "https://developer.microsoft.com/en-us/microsoft-edge/webview2/?form=MA13LH&cs=3069339192#download";

    public void ShowIfMissing()
    {
        if (runtimeVersion() is null) Show(strings.WebViewRuntimeMissing);
    }

    public void Show(string message) =>
        notifier.ShowMessageWithButton(strings.WebViewRuntimeMissingTitle, message, strings.WebViewRuntimeDownload,
            () => urlOpening.TryOpen(DownloadUrl));
}

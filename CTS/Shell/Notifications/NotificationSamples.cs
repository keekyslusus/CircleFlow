using CircleToSearch.Ui;

namespace CircleToSearch.Shell.Notifications;

// Developer settings use these to check the notification design without waiting for a real event.
internal static class NotificationSamples
{
    public static void Show(IPluginNotifier notifier, WebViewRuntimeNotice runtimeNotice, UiStrings strings, string hotkey)
    {
        notifier.ShowMessage(strings.PluginTitle, strings.MusicNoMatch);
        notifier.ShowError(strings.PluginTitle, strings.HotkeyConflict(hotkey));
        notifier.ShowMessageWithButton(strings.UpdateAvailableTitle, strings.UpdateAvailable(ProjectSupport.Version),
            strings.UpdateInstall, () => { });
        runtimeNotice.Show(strings.WebViewRuntimeMissing);
    }
}

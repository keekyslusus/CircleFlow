namespace CircleToSearch.Ui;

internal sealed record NotificationLink(string Text, Action Open);

internal interface IPluginNotifier
{
    void ShowMessage(string title, string message);

    void ShowMessageWithButton(string title, string message, string button, Action action, NotificationLink? link = null);

    void ShowError(string title, string message);
}

internal sealed class PluginNotifier(
    Action<string, string> showMessage,
    Action<string, string, string, Action, NotificationLink?> showMessageWithButton,
    Action<string, string> showError,
    PluginLog log) : IPluginNotifier
{
    public void ShowMessage(string title, string message)
    {
        try { showMessage(title, message); }
        catch (Exception exception) { log.Error(nameof(PluginNotifier), "showing the message failed", exception); }
    }

    public void ShowMessageWithButton(string title, string message, string button, Action action, NotificationLink? link = null)
    {
        try { showMessageWithButton(title, message, button, action, link); }
        catch (Exception exception) { log.Error(nameof(PluginNotifier), "showing the message with button failed", exception); }
    }

    public void ShowError(string title, string message)
    {
        try { showError(title, message); }
        catch (Exception exception) { log.Error(nameof(PluginNotifier), "showing the error message failed", exception); }
    }
}

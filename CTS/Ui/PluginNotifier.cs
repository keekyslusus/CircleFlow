namespace CircleToSearch.Ui;

internal interface IPluginNotifier
{
    void ShowMessage(string title, string message);

    void ShowMessageWithButton(string title, string message, string button, Action action);

    void ShowError(string title, string message);
}

internal sealed class PluginNotifier(
    Action<string, string> showMessage,
    Action<string, string, string, Action> showMessageWithButton,
    Action<string, string> showError,
    PluginLog log) : IPluginNotifier
{
    public void ShowMessage(string title, string message)
    {
        try { showMessage(title, message); }
        catch (Exception exception) { log.Error(nameof(PluginNotifier), "showing the message failed", exception); }
    }

    public void ShowMessageWithButton(string title, string message, string button, Action action)
    {
        try { showMessageWithButton(title, message, button, action); }
        catch (Exception exception) { log.Error(nameof(PluginNotifier), "showing the message with button failed", exception); }
    }

    public void ShowError(string title, string message)
    {
        try { showError(title, message); }
        catch (Exception exception) { log.Error(nameof(PluginNotifier), "showing the error message failed", exception); }
    }
}

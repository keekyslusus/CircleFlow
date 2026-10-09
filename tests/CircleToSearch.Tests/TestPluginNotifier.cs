using CircleToSearch.Ui;

namespace CircleToSearch.Tests;

internal sealed class TestPluginNotifier : IPluginNotifier
{
    public List<(string Title, string Message)> Messages { get; } = [];
    public List<(string Title, string Message, string Button, Action Action, NotificationLink? Link)> Buttons { get; } = [];
    public List<(string Title, string Message)> Errors { get; } = [];

    public void ShowMessage(string title, string message) => Messages.Add((title, message));

    public void ShowMessageWithButton(string title, string message, string button, Action action, NotificationLink? link = null) =>
        Buttons.Add((title, message, button, action, link));

    public void ShowError(string title, string message) => Errors.Add((title, message));
}

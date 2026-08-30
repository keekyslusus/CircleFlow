using CircleToSearch.Ui;

namespace CircleToSearch.Tests;

internal sealed class TestPluginNotifier : IPluginNotifier
{
    public List<(string Title, string Message)> Messages { get; } = [];
    public List<(string Title, string Message, string Button, Action Action)> Buttons { get; } = [];
    public List<(string Title, string Message)> Errors { get; } = [];

    public void ShowMessage(string title, string message) => Messages.Add((title, message));

    public void ShowMessageWithButton(string title, string message, string button, Action action) =>
        Buttons.Add((title, message, button, action));

    public void ShowError(string title, string message) => Errors.Add((title, message));
}

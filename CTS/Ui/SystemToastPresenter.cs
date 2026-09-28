using System.Windows.Threading;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace CircleToSearch.Ui;

internal sealed class SystemToastPresenter(string appUserModelId, Dispatcher dispatcher, PluginLog log)
{
    private const string ButtonArgument = "button";
    // Windows raises Activated only while the toast object is alive.
    private readonly List<ToastNotification> _shown = [];

    public void ShowMessage(string title, string message) => Show(title, message, null, null);

    public void ShowError(string title, string message) => Show(title, message, null, null);

    public void ShowMessageWithButton(string title, string message, string button, Action action) =>
        Show(title, message, button, action);

    public void Clear()
    {
        lock (_shown) _shown.Clear();
        ToastNotificationManager.History.Clear(appUserModelId);
    }

    private void Show(string title, string message, string? button, Action? action)
    {
        var content = new XmlDocument();
        var toast = content.CreateElement("toast");
        content.AppendChild(toast);
        var visual = content.CreateElement("visual");
        toast.AppendChild(visual);
        var binding = content.CreateElement("binding");
        binding.SetAttribute("template", "ToastGeneric");
        visual.AppendChild(binding);
        foreach (var text in new[] { title, message })
        {
            var element = content.CreateElement("text");
            element.InnerText = text;
            binding.AppendChild(element);
        }
        if (button is not null && action is not null)
        {
            var actions = content.CreateElement("actions");
            var element = content.CreateElement("action");
            element.SetAttribute("content", button);
            element.SetAttribute("arguments", ButtonArgument);
            actions.AppendChild(element);
            toast.AppendChild(actions);
        }

        var notification = new ToastNotification(content);
        if (action is not null)
            notification.Activated += (_, arguments) =>
            {
                if (arguments is ToastActivatedEventArgs { Arguments: ButtonArgument })
                    dispatcher.BeginInvoke(() =>
                    {
                        try { action(); }
                        catch (Exception exception) { log.SafeError(nameof(SystemToastPresenter), "toast-action", exception); }
                    });
            };
        notification.Failed += (_, arguments) =>
            log.SafeError(nameof(SystemToastPresenter), "show-toast", arguments.ErrorCode);
        lock (_shown) _shown.Add(notification);
        ToastNotificationManager.CreateToastNotifier(appUserModelId).Show(notification);
    }
}

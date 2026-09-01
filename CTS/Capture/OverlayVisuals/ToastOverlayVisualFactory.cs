namespace CircleToSearch.Capture;

using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using CircleToSearch.Ui;

internal static class ToastOverlayVisualFactory
{
    internal const double MaximumWidth = 360;

    internal static ToastOverlayVisual Create(ToastNotification notification, bool lightTheme)
    {
        ArgumentNullException.ThrowIfNull(notification);
        var theme = PluginPalette.For(lightTheme);
        var accent = notification.Tone switch
        {
            ToastTone.Neutral => theme.Toast.NeutralAccent,
            ToastTone.Error => theme.Toast.ErrorAccent,
            ToastTone.Success => theme.Toast.SuccessAccent,
            _ => throw new ArgumentOutOfRangeException(nameof(notification)),
        };

        var message = new TextBlock
        {
            Foreground = OverlayVisualResources.Frozen(theme.Toast.Text),
            FontFamily = OverlayVisualResources.Font,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
        if (notification.ColorSample is { } sample)
            AddColorContent(message, notification.Message, sample, theme.Toast.SwatchBorder);
        else
            message.Text = notification.Message;
        var card = new Border
        {
            Child = message,
            Background = OverlayVisualResources.Frozen(theme.Toast.Surface),
            BorderBrush = OverlayVisualResources.Frozen(accent),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(20),
            Padding = new Thickness(16, 10, 16, 10),
            MinHeight = 40,
            MaxWidth = MaximumWidth,
            HorizontalAlignment = HorizontalAlignment.Center,
            IsHitTestVisible = false,
            Effect = OverlayVisualResources.DockShadow(6, theme.Toast.ShadowOpacity),
        };
        AutomationProperties.SetName(message, notification.AccessibleMessage);
        AutomationProperties.SetLiveSetting(message, AutomationLiveSetting.Polite);

        var slot = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsHitTestVisible = false,
        };
        Panel.SetZIndex(slot, 1);
        slot.Children.Add(card);
        return new ToastOverlayVisual(slot, card, message);
    }

    private static void AddColorContent(
        TextBlock message,
        string prefix,
        ToastColorSample sample,
        Color swatchBorder)
    {
        message.Inlines.Add(new Run($"{prefix} "));
        message.Inlines.Add(new InlineUIContainer(new Border
        {
            Width = 16,
            Height = 16,
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            BorderBrush = OverlayVisualResources.Frozen(swatchBorder),
            Background = OverlayVisualResources.Frozen(Color.FromRgb(sample.Red, sample.Green, sample.Blue)),
            IsHitTestVisible = false,
        })
        {
            BaselineAlignment = BaselineAlignment.Center,
        });
        message.Inlines.Add(new Run($" {sample.HexCode}"));
    }

    internal static AutomationPeer Announce(ToastOverlayVisual visual)
    {
        ArgumentNullException.ThrowIfNull(visual);
        var peer = UIElementAutomationPeer.CreatePeerForElement(visual.Message)
                   ?? throw new InvalidOperationException("The toast live region has no automation peer.");
        peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        return peer;
    }
}

namespace CircleToSearch.Capture;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CircleToSearch.Links;
using CircleToSearch.Ui;

internal sealed record QrCodeChip(
    ScreenLink Link, QrCodeViewfinder Viewfinder, FloatingToolbar Toolbar, Button Primary, Button? Copy);

internal static class QrCodeVisualFactory
{
    private const int LabelTextElementLimit = 40;

    internal static QrCodeVisual Create(bool lightTheme, Color accent, Thickness toolbarSafeInsets)
    {
        var viewfinders = new Canvas { IsHitTestVisible = false };
        var layer = new Grid { Background = null };
        layer.Children.Add(viewfinders);
        return new QrCodeVisual(layer, viewfinders, lightTheme, accent, toolbarSafeInsets);
    }

    internal static QrCodeChip CreateChip(QrCodeVisual visual, ScreenLink link, Rect boundsDips, UiStrings strings)
    {
        var palette = PluginPalette.For(visual.LightTheme);
        var toolbar = new FloatingToolbar(palette.FloatingToolbar, safeInsets: visual.ToolbarSafeInsets, preferBelow: true);
        var label = link.Kind == ScreenLinkKind.Web
            ? ShortenHost(link.Label)
            : ClipboardCopyService.Preview(link.Label, LabelTextElementLimit);
        var (primary, hasCopy) = link.Kind switch
        {
            ScreenLinkKind.Web => (Action(toolbar, label, PluginIcons.LinkOutlined, strings.QrOpen(link.Label)), true),
            ScreenLinkKind.Email => (Action(toolbar, label, PluginIcons.MailOutlined, strings.QrOpen(link.Label)), true),
            ScreenLinkKind.Wifi => (Action(toolbar, strings.QrCopyPassword, PluginIcons.WifiOutlined,
                strings.QrWifiNetwork(link.Label)), false),
            _ => (Action(toolbar, strings.QrCopyText, PluginIcons.CopyOutlined, label), false),
        };
        var copy = hasCopy ? toolbar.AddAction(strings.TextCopy, PluginIcons.CopyOutlined) : null;

        var viewfinder = new QrCodeViewfinder(boundsDips, visual.Accent);
        visual.Viewfinders.Children.Add(viewfinder.Element);
        visual.Layer.Children.Add(toolbar.Layer);
        return new QrCodeChip(link, viewfinder, toolbar, primary, copy);
    }

    // The registrable domain sits at the right end of a host, so a long host keeps that end; cutting from the
    // right would let login.microsoft.com.evil.example pass for Microsoft.
    internal static string ShortenHost(string host)
    {
        if (host.Length <= LabelTextElementLimit) return host;
        var tail = host[^LabelTextElementLimit..];
        var boundary = tail.IndexOf('.');
        return "..." + (boundary >= 0 && boundary < tail.Length - 1 ? tail[(boundary + 1)..] : tail);
    }

    private static Button Action(FloatingToolbar toolbar, string label, Geometry icon, string toolTip)
    {
        var button = toolbar.AddAction(label, icon);
        button.ToolTip = toolTip;
        System.Windows.Automation.AutomationProperties.SetName(button, toolTip);
        return button;
    }
}

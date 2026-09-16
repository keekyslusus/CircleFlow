using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CircleToSearch.Ui;

namespace CircleToSearch.Shell;

internal static class TrayMenuView
{
    internal static ContextMenu Create()
    {
        var menu = (ContextMenu)Application.LoadComponent(new Uri(
            "/CircleFlow;component/CTS/Shell/TrayMenu.xaml", UriKind.Relative));
        ApplyTheme(menu, SystemTheme.IsLight());
        return menu;
    }

    internal static void ApplyTheme(ContextMenu menu, bool light)
    {
        var palette = PluginPalette.Settings(light);
        (string Key, Color Color)[] colors =
        [
            ("Surface", palette.Paper), ("Text", palette.Text),
            ("Border", palette.Line), ("Hover", palette.Selected),
            ("Transparent", PluginPalette.Transparent),
        ];
        foreach (var (key, color) in colors)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            menu.Resources["Tray" + key] = brush;
        }
        menu.Resources["TrayShadowColor"] = PluginPalette.OpaqueBlack;
    }
}

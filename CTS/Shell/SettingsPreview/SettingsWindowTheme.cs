using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CircleToSearch.Interop;
using CircleToSearch.Ui;

namespace CircleToSearch.Shell.SettingsPreview;

// Shared by every app window that uses SettingsStyles.xaml, so they all read the same Settings* brushes.
internal static class SettingsWindowTheme
{
    public static void Apply(Window window, bool lightTheme, string iconPath)
    {
        var palette = PluginPalette.Settings(lightTheme);
        (string Key, Color Color)[] colors =
        [
            ("Paper", palette.Paper), ("Surface", palette.Surface), ("Card", palette.Card),
            ("Sidebar", palette.Sidebar), ("Text", palette.Text),
            ("ScrollbarThumb", palette.ScrollbarThumb),
            ("Muted", palette.Muted), ("Accent", palette.Accent), ("Line", palette.Line),
            ("Hover", palette.Hover), ("Selected", palette.Selected), ("Wash", palette.Wash),
            ("HeroStart", palette.HeroStart), ("HeroEnd", palette.HeroEnd),
            ("AccentLine", palette.AccentLine), ("Scrim", palette.Scrim),
            ("Transparent", PluginPalette.Transparent),
        ];
        foreach (var (key, color) in colors) window.Resources["Settings" + key] = Frozen(color);
        window.Resources["SettingsHeroStartColor"] = palette.HeroStart;
        window.Resources["SettingsHeroEndColor"] = palette.HeroEnd;
        window.Icon = BitmapFrame.Create(new Uri(iconPath), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        window.SourceInitialized += (_, _) =>
        {
            var darkMode = lightTheme ? 0 : 1;
            NativeMethods.DwmSetWindowAttribute(new WindowInteropHelper(window).Handle,
                NativeMethods.DwmwaUseImmersiveDarkMode, ref darkMode, sizeof(int));
        };
    }

    public static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}

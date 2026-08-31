namespace CircleToSearch.Ui;

using Microsoft.Win32;
using System.Windows.Media;

public static class SystemAccentColor
{
    private const double PastelChroma = 40.0;
    private const double PastelTone = 80.0;

    // WinRT UISettings would pull Microsoft.Windows.SDK.NET.dll (~24 MB) into the release
    // directory; the registry keys below hold the same accent the Settings app shows.
    // Read live instead of cached: the chip is built once per selection, so an accent
    // change is picked up on the next trigger without a plugin reload.
    public static Color Read()
    {
        return ToPastel(ReadRaw());
    }

    public static Color ReadRaw()
    {
        return TryRead(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent", "AccentColorMenu")
            ?? TryRead(@"Software\Microsoft\Windows\DWM", "AccentColor")
            ?? PluginPalette.SystemAccentFallback;
    }

    public static Color ToPastel(Color color)
    {
        var seed = color.R == 0 && color.G == 0 && color.B == 0
            ? Color.FromArgb(color.A, 1, 1, 1)
            : color;
        return HctColorConverter.Convert(seed, PastelChroma, PastelTone);
    }

    internal static Color? FromDword(int raw) =>
        Color.FromRgb((byte)raw, (byte)(raw >> 8), (byte)(raw >> 16));

    private static Color? TryRead(string keyPath, string valueName)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(keyPath);
            return key?.GetValue(valueName) is int raw ? FromDword(raw) : null;
        }
        catch
        {
            return null;
        }
    }
}

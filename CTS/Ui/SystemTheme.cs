namespace CircleToSearch.Ui;

using Microsoft.Win32;

public static class SystemTheme
{
    // Read live (not cached): the chip is built once per selection, so a theme switch
    // is picked up on the next trigger without a plugin reload.
    public static bool IsLight()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is not 0;
        }
        catch
        {
            return true;
        }
    }
}

using System.IO;
using CircleToSearch.Ui;
using Microsoft.Web.WebView2.Core;

namespace CircleToSearch.Interop;

internal sealed class WebViewEnvironmentFactory(AppPaths paths, UiStrings strings, IPluginNotifier notifier)
{
    public async Task<CoreWebView2Environment> CreateAsync(string profileDirectory, bool enableExtensions = false)
    {
        ValidateProfileDirectory(profileDirectory);
        Directory.CreateDirectory(profileDirectory);
        var environment = await CoreWebView2Environment.CreateAsync(
            userDataFolder: profileDirectory,
            options: new CoreWebView2EnvironmentOptions { AreBrowserExtensionsEnabled = enableExtensions });
        // WebView2 policies and environment variables can override the requested profile.
        ValidateProfileDirectory(environment.UserDataFolder);
        return environment;
    }

    // Resolves the runtime the loader would pick, including per-user installs and override policies.
    internal static string? RuntimeVersion()
    {
        try { return CoreWebView2Environment.GetAvailableBrowserVersionString(); }
        catch { return null; }
    }

    internal void ValidateProfileDirectory(string directory)
    {
        if (!paths.IsInsideData(directory))
        {
            notifier.ShowError(strings.PluginTitle, strings.BrowserProfileOutsideData);
            throw new InvalidOperationException(strings.BrowserProfileOutsideData);
        }
    }
}

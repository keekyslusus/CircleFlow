using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace CircleToSearch.Search.Browser;

internal static class SearchBrowserExtension
{
    internal const string PackageHash = "061A5DE7B1EEDF6C1FE0AFDA40D453C427EEFC2CBCDBB680C3EED37DC5CEF2C8";

    // Cookie notice and social widget lists are left off: they hide consent prompts and embedded posts people may need.
    internal static readonly string[] AnnoyanceRulesets =
        ["annoyances-notifications", "annoyances-others", "annoyances-overlays", "annoyances-widgets"];

    private static readonly string RulesetsMarker = string.Join(',', AnnoyanceRulesets);
    private static readonly TimeSpan RulesetTimeout = TimeSpan.FromSeconds(30);

    internal static string Prepare(string assetDirectory, string userDataFolder)
    {
        // Keep the registered directory stable across window openings and package updates.
        var directory = Path.Combine(userDataFolder, "CircleFlowExtensions", "uBlockOriginLite");
        var marker = Path.Combine(directory, ".package-sha256");
        if (File.Exists(marker) && File.ReadAllText(marker) == PackageHash &&
            File.Exists(Path.Combine(directory, "manifest.json"))) return directory;

        var archive = Path.Combine(assetDirectory, "Extensions", "uBlockOriginLite.zip");
        using var stream = File.OpenRead(archive);
        if (Convert.ToHexString(SHA256.HashData(stream)) != PackageHash)
            throw new InvalidDataException("The bundled uBlock Origin Lite package checksum does not match.");

        Directory.CreateDirectory(directory);
        // A re-extracted package must be registered again so the profile loads the new files.
        File.Delete(InstalledIdPath(directory));
        File.Delete(EnabledRulesetsPath(directory));
        stream.Position = 0;
        ZipFile.ExtractToDirectory(stream, directory, overwriteFiles: true);
        File.WriteAllText(marker, PackageHash);
        return directory;
    }

    // The unpacked extension's id and registration follow its absolute path, so a moved app folder must register again.
    internal static string? InstalledId(string directory)
    {
        var path = InstalledIdPath(directory);
        if (!File.Exists(path)) return null;
        var lines = File.ReadAllLines(path);
        return lines.Length == 2 && string.Equals(lines[1], directory, StringComparison.OrdinalIgnoreCase)
            ? lines[0]
            : null;
    }

    internal static void RememberInstalled(string directory, string id) =>
        File.WriteAllLines(InstalledIdPath(directory), [id, directory]);

    internal static async Task<CoreWebView2BrowserExtension> EnsureEnabledAsync(
        WebView2 webView,
        string assetDirectory,
        string userDataFolder,
        PluginLog log,
        CancellationToken cancel)
    {
        var (directory, listsApplied) = await Task.Run(() =>
        {
            var prepared = Prepare(assetDirectory, userDataFolder);
            return (prepared, EnabledRulesets(prepared) == RulesetsMarker);
        }, cancel).ConfigureAwait(true);
        cancel.ThrowIfCancellationRequested();
        var core = webView.CoreWebView2;
        var (extension, added) = await EnsureInstalledAsync(core.Profile, directory, log).ConfigureAwait(true);
        cancel.ThrowIfCancellationRequested();
        if (!extension.IsEnabled) await extension.EnableAsync(true).ConfigureAwait(true);
        log.Info(nameof(SearchBrowserExtension), $"uBlock Origin Lite enabled: {extension.Id}");
        // Only the first window of a profile does this work, and its search need not wait for it.
        if (added || !listsApplied)
            _ = EnableAnnoyanceRulesetsAsync(core.Environment, webView.Handle, directory, extension.Id, log);
        return extension;
    }

    // uBO Lite changes its lists only through messages from its own pages, so a hidden browser opens one.
    private static async Task EnableAnnoyanceRulesetsAsync(
        CoreWebView2Environment environment,
        IntPtr parentWindow,
        string directory,
        string extensionId,
        PluginLog log)
    {
        CoreWebView2Controller? controller = null;
        try
        {
            controller = await environment.CreateCoreWebView2ControllerAsync(parentWindow).ConfigureAwait(true);
            controller.IsVisible = false;
            var loaded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            controller.CoreWebView2.NavigationCompleted += (_, args) => loaded.TrySetResult(args.IsSuccess);
            controller.CoreWebView2.Navigate($"chrome-extension://{extensionId}/dashboard.html");
            if (!await loaded.Task.WaitAsync(RulesetTimeout).ConfigureAwait(true))
                throw new InvalidOperationException("The uBlock Origin Lite dashboard did not load.");
            var response = await controller.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.evaluate",
                JsonSerializer.Serialize(new { expression = AddRulesetsScript(), awaitPromise = true, returnByValue = true }))
                .WaitAsync(RulesetTimeout).ConfigureAwait(true);
            using var result = JsonDocument.Parse(response);
            if (!result.RootElement.GetProperty("result").TryGetProperty("value", out var missing) ||
                missing.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException($"uBlock Origin Lite did not answer the list request: {response}");
            // A list this uBO Lite version lacks or cannot fit would fail the same way in every window, so it is not retried.
            File.WriteAllText(EnabledRulesetsPath(directory), RulesetsMarker);
            if (missing.GetArrayLength() == 0)
                log.Info(nameof(SearchBrowserExtension), $"uBlock Origin Lite lists enabled: {string.Join(", ", AnnoyanceRulesets)}");
            else
                log.Warn(nameof(SearchBrowserExtension), $"uBlock Origin Lite did not enable these lists: {missing.GetRawText()}");
        }
        // Without the marker the next window tries again, so a failure here costs only this attempt.
        catch (Exception exception)
        {
            log.Error(nameof(SearchBrowserExtension), "enabling uBlock Origin Lite annoyance lists failed", exception);
        }
        finally { controller?.Close(); }
    }

    // Lists are added to what uBO Lite already chose, such as its regional list, and one a person turned off stays off.
    private static string AddRulesetsScript() => $$"""
        (async () => {
            const wanted = {{JsonSerializer.Serialize(AnnoyanceRulesets)}};
            const { enabledRulesets } = await chrome.runtime.sendMessage({ what: 'getCurrentConfig' });
            if (!wanted.every(id => enabledRulesets.includes(id)))
                await chrome.runtime.sendMessage({ what: 'applyRulesets', enabledRulesets: [...new Set([...enabledRulesets, ...wanted])] });
            const after = (await chrome.runtime.sendMessage({ what: 'getCurrentConfig' })).enabledRulesets;
            return wanted.filter(id => !after.includes(id));
        })()
        """;

    internal static string? EnabledRulesets(string directory)
    {
        try { return File.Exists(EnabledRulesetsPath(directory)) ? File.ReadAllText(EnabledRulesetsPath(directory)) : null; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return null; }
    }

    // The registration persists in the profile, and adding it again costs about 700 ms per window.
    private static async Task<(CoreWebView2BrowserExtension Extension, bool Added)> EnsureInstalledAsync(
        CoreWebView2Profile profile,
        string directory,
        PluginLog log)
    {
        string? id = null;
        try { id = InstalledId(directory); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            log.SafeError(nameof(SearchBrowserExtension), "read-extension-id", exception);
        }
        if (id is not null &&
            (await profile.GetBrowserExtensionsAsync().ConfigureAwait(true))
                .FirstOrDefault(extension => extension.Id == id) is { } installed)
            return (installed, false);
        var added = await profile.AddBrowserExtensionAsync(directory).ConfigureAwait(true);
        // The id only saves time on the next window, so failing to store it must not fail this one.
        try
        {
            RememberInstalled(directory, added.Id);
            // A new registration starts with uBO Lite's own lists, for example after the browser data was cleared.
            File.Delete(EnabledRulesetsPath(directory));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            log.SafeError(nameof(SearchBrowserExtension), "remember-extension-id", exception);
        }
        return (added, true);
    }

    private static string InstalledIdPath(string directory) => Path.Combine(directory, ".installed-id");

    private static string EnabledRulesetsPath(string directory) => Path.Combine(directory, ".enabled-rulesets");
}

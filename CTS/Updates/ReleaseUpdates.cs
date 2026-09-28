using System.Reflection;
using Velopack;
using Velopack.Locators;
using Velopack.Sources;

namespace CircleToSearch.Updates;

internal sealed class ReleaseUpdates(string repositoryUrl, string? testFeed)
{
    private readonly UpdateManager _manager = testFeed is null
        ? new(new GithubSource(repositoryUrl, accessToken: null, prerelease: false))
        : new(testFeed);

    // Set only by build_release.ps1 -UpdateFeed, so the update flow can be tried without publishing a release.
    public static string? BuiltInTestFeed { get; } = typeof(ReleaseUpdates).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(metadata => metadata.Key == "UpdateFeed")?.Value;

    public bool IsInstalled => _manager.IsInstalled;

    // Null for a portable copy: it has no Start menu shortcut, and Windows shows toasts only for apps it can resolve.
    public string? ToastAppUserModelId =>
        _manager.IsInstalled && !_manager.IsPortable ? VelopackLocator.Current.AppUserModelId : null;

    public async Task<AvailableUpdate?> FindAsync(CancellationToken cancellation)
    {
        var update = await _manager.CheckForUpdatesAsync().WaitAsync(cancellation);
        return update is null
            ? null
            : new AvailableUpdate(update.TargetFullRelease.Version.ToString(),
                download => _manager.DownloadUpdatesAsync(update, cancelToken: download),
                () => _manager.WaitExitThenApplyUpdates(update.TargetFullRelease, silent: false, restart: true));
    }
}

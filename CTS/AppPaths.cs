using System.IO;

namespace CircleToSearch;

internal sealed class AppPaths
{
    public AppPaths(string? rootDirectory = null)
    {
        rootDirectory ??= ResolveRootDirectory(AppContext.BaseDirectory);
        if (!Path.IsPathFullyQualified(rootDirectory))
            throw new ArgumentException("The application root must be an absolute path.", nameof(rootDirectory));
        RootDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootDirectory));
        _installDirectory = ResolveInstallDirectory(RootDirectory);
    }

    private readonly string _installDirectory;

    public string RootDirectory { get; }
    internal static string ResolveRootDirectory(string baseDirectory)
    {
        var directory = new DirectoryInfo(baseDirectory);
        // The published apphost stays beside the assets while the managed application lives in deps.
        return directory.Name.Equals("deps", StringComparison.OrdinalIgnoreCase)
            && directory.Parent is { } parent
            && File.Exists(Path.Combine(parent.FullName, "CircleFlow.exe"))
            ? parent.FullName
            : directory.FullName;
    }
    // Velopack replaces current\ on every update, so user data lives beside it next to Update.exe.
    internal static string ResolveInstallDirectory(string rootDirectory)
    {
        var directory = new DirectoryInfo(rootDirectory);
        return directory.Name.Equals("current", StringComparison.OrdinalIgnoreCase)
            && directory.Parent is { } parent
            && File.Exists(Path.Combine(parent.FullName, "Update.exe"))
            ? parent.FullName
            : directory.FullName;
    }
    public string ExecutablePath => Path.Combine(RootDirectory, "CircleFlow.exe");
    private string AssetsDirectory =>Path.Combine(RootDirectory, "Assets");
    public string LanguagesDirectory => Path.Combine(AssetsDirectory, "Languages");
    public string TrayIconPath => Path.Combine(AssetsDirectory, "Images", "app.ico");
    public string ExtensionArchivePath => Path.Combine(AssetsDirectory, "Extensions", "uBlockOriginLite.zip");
    public string CosmeticFiltersPath => Path.Combine(AssetsDirectory, "Extensions", "CircleFlowFilters.txt");
    public string SoundsDirectory => Path.Combine(AssetsDirectory, "Sounds");
    public string EmojiArchivePath => Path.Combine(AssetsDirectory, "Emoji", "NotoColorEmoji.zip");
    public string LicensesDirectory => Path.Combine(RootDirectory, "Licenses");
    public string DataDirectory => Path.Combine(_installDirectory, "Data");
    public string SettingsFilePath => Path.Combine(DataDirectory, "settings.json");
    public string SettingsBackupFilePath => Path.Combine(DataDirectory, "settings.json.bak");
    public string MusicHistoryFilePath => Path.Combine(DataDirectory, "music-history.json");
    public string LogsDirectory => Path.Combine(DataDirectory, "Logs");
    public string ProfilesDirectory => Path.Combine(DataDirectory, "Profiles");
    public string SearchProfileDirectory => Path.Combine(ProfilesDirectory, "Search");
    public string ImageTranslationProfileDirectory => Path.Combine(ProfilesDirectory, "ImageTranslation");
    public string TraceVideoProfileDirectory => Path.Combine(ProfilesDirectory, "TraceVideo");
    public string TempDirectory => Path.Combine(DataDirectory, "Temp");

    public bool IsInsideData(string path)
    {
        if (!Path.IsPathFullyQualified(path)) return false;
        try
        {
            return Path.GetFullPath(path).StartsWith(DataDirectory + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException) { return false; }
        catch (NotSupportedException) { return false; }
        catch (PathTooLongException) { return false; }
    }
}

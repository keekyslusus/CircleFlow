using System.IO;

namespace CircleToSearch;

internal sealed class AppPaths
{
    public AppPaths(string? rootDirectory = null)
    {
        rootDirectory ??= AppContext.BaseDirectory;
        if (!Path.IsPathFullyQualified(rootDirectory))
            throw new ArgumentException("The application root must be an absolute path.", nameof(rootDirectory));
        RootDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootDirectory));
    }

    public string RootDirectory { get; }
    public string LanguagesDirectory => Path.Combine(RootDirectory, "Languages");
    public string AppIconPath => Path.Combine(RootDirectory, "Images", "app.png");
    public string ExtensionArchivePath => Path.Combine(RootDirectory, "Extensions", "uBlockOriginLite.zip");
    public string DataDirectory => Path.Combine(RootDirectory, "Data");
    public string SettingsFilePath => Path.Combine(DataDirectory, "settings.json");
    public string SettingsBackupFilePath => Path.Combine(DataDirectory, "settings.json.bak");
    public string LogsDirectory => Path.Combine(DataDirectory, "Logs");
    public string SearchProfileDirectory => Path.Combine(DataDirectory, "Profiles", "Search");
    public string ImageTranslationProfileDirectory => Path.Combine(DataDirectory, "Profiles", "ImageTranslation");
    public string TraceVideoProfileDirectory => Path.Combine(DataDirectory, "Profiles", "TraceVideo");
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

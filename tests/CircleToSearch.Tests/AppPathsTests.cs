using System.Security.AccessControl;
using System.Security.Principal;
using CircleToSearch.Translation;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class AppPathsTests
{
    [Fact]
    public void Published_dependencies_resolve_assets_beside_the_apphost()
    {
        var root = NewRoot();
        var dependencies = Path.Combine(root, "deps");
        Directory.CreateDirectory(dependencies);
        try
        {
            Assert.Equal(dependencies, AppPaths.ResolveRootDirectory(dependencies));
            File.WriteAllBytes(Path.Combine(root, "CircleFlow.exe"), []);
            Assert.Equal(root, AppPaths.ResolveRootDirectory(dependencies + Path.DirectorySeparatorChar));
            Assert.Equal(root, AppPaths.ResolveRootDirectory(root));
            Assert.Equal(dependencies, new AppPaths(dependencies).RootDirectory);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Paths_are_absolute_and_construction_does_not_create_directories()
    {
        var root = NewRoot();
        var paths = new AppPaths(root + Path.DirectorySeparatorChar);
        Assert.Equal(root, paths.RootDirectory);
        Assert.False(Directory.Exists(root));
        Assert.Equal(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory), new AppPaths().RootDirectory);
        Assert.Throws<ArgumentException>(() => new AppPaths("relative-folder"));
        Assert.Throws<ArgumentException>(() => new AppPaths("C:relative-folder"));
        Assert.All(new[]
        {
            paths.SettingsFilePath, paths.SettingsBackupFilePath, paths.LogsDirectory,
            paths.SearchProfileDirectory, paths.ImageTranslationProfileDirectory,
            paths.TraceVideoProfileDirectory, paths.TempDirectory,
        }, path => Assert.True(Path.IsPathFullyQualified(path) && paths.IsInsideData(path)));
        Assert.False(paths.IsInsideData(paths.TrayIconPath));
        Assert.False(paths.IsInsideData(paths.ExtensionArchivePath));
        Assert.False(paths.IsInsideData(paths.LanguagesDirectory));
    }

    [Theory]
    [InlineData("Data-other/profile")]
    [InlineData("Data/../outside")]
    [InlineData("Data/Profiles/../../outside")]
    public void Containment_rejects_neighbor_directories_and_parent_traversal(string relative)
    {
        var paths = new AppPaths(NewRoot());
        Assert.False(paths.IsInsideData(Path.Combine(paths.RootDirectory, relative)));
        Assert.False(paths.IsInsideData("Data/Profiles/Search"));
        Assert.False(paths.IsInsideData(paths.DataDirectory));
        Assert.True(paths.IsInsideData(paths.SearchProfileDirectory.ToUpperInvariant()));
    }

    [Fact]
    public void Initialization_probes_storage_and_logs_stay_inside_Data()
    {
        var paths = new AppPaths(NewRoot());
        AppDataDirectory.Initialize(paths);
        AppDataDirectory.Initialize(paths);
        Assert.True(Directory.Exists(paths.LogsDirectory));
        Assert.True(Directory.Exists(paths.TempDirectory));
        Assert.False(Directory.Exists(paths.SearchProfileDirectory));
        Assert.Empty(Directory.GetFiles(paths.DataDirectory, "*", SearchOption.AllDirectories));
        new PluginLog(paths.LogsDirectory).Info("test", "inside Data");
        using (var profiler = new TranslationMemoryProfiler(paths.LogsDirectory)) profiler.Mark("test");
        Assert.All(Directory.GetFiles(paths.RootDirectory, "*", SearchOption.AllDirectories), file =>
        {
            Assert.True(paths.IsInsideData(file));
            Assert.Equal(paths.LogsDirectory, Path.GetDirectoryName(file));
        });
        Assert.False(File.Exists(paths.SettingsFilePath));
    }

    [Fact]
    public void Unavailable_Data_is_reported_without_replacing_it_or_using_another_directory()
    {
        var paths = new AppPaths(NewRoot());
        Directory.CreateDirectory(paths.RootDirectory);
        File.WriteAllText(paths.DataDirectory, "keep me");
        Assert.Throws<IOException>(() => AppDataDirectory.Initialize(paths));
        Assert.Equal("keep me", File.ReadAllText(paths.DataDirectory));
        Assert.Empty(Directory.GetDirectories(paths.RootDirectory));
        Assert.Single(Directory.GetFiles(paths.RootDirectory));
    }

    [Fact]
    public void Data_without_write_permission_fails_the_probe_and_leaves_no_files()
    {
        var paths = new AppPaths(NewRoot());
        var directory = Directory.CreateDirectory(paths.DataDirectory);
        var original = directory.GetAccessControl();
        var restricted = directory.GetAccessControl();
        using var identity = WindowsIdentity.GetCurrent();
        restricted.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.Write,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Deny));
        try
        {
            directory.SetAccessControl(restricted);
            Assert.Throws<UnauthorizedAccessException>(() => AppDataDirectory.Initialize(paths));
            Assert.Empty(Directory.GetFiles(paths.DataDirectory));
            Assert.False(Directory.Exists(paths.LogsDirectory));
        }
        finally { directory.SetAccessControl(original); }
    }

    [Fact]
    public void Unavailable_log_directory_also_fails_initialization()
    {
        var paths = new AppPaths(NewRoot());
        Directory.CreateDirectory(paths.DataDirectory);
        File.WriteAllText(paths.LogsDirectory, "keep me");
        Assert.Throws<IOException>(() => AppDataDirectory.Initialize(paths));
        Assert.Equal("keep me", File.ReadAllText(paths.LogsDirectory));
        Assert.False(Directory.Exists(paths.TempDirectory));
        Assert.Single(Directory.GetFiles(paths.DataDirectory));
    }

    private static string NewRoot() => Path.Combine(TestOutputPaths.TempDirectory, "пути с пробелами " + Guid.NewGuid().ToString("N"));
}

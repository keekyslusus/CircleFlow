namespace CircleToSearch.Tests;

// Tool-generated artifacts (chip previews, fade captures) land in tests/temp instead of the
// user profile temp so contributor machines stay clean; the folder is git-ignored.
internal static class TestOutputPaths
{
    public static string RepoDirectory => RepoRoot();

    public static string TempDirectory => Path.Combine(RepoRoot(), "tests", "temp");

    public static string NewTempDirectory(string name)
    {
        var path = Path.Combine(TempDirectory, name);
        if (Directory.Exists(path)) Directory.Delete(path, true);
        Directory.CreateDirectory(path);
        return path;
    }

    private static string RepoRoot()
    {
        var candidate = new DirectoryInfo(AppContext.BaseDirectory);
        while (candidate is not null && !File.Exists(Path.Combine(candidate.FullName, "CircleFlow.csproj")))
            candidate = candidate.Parent;
        return candidate?.FullName ?? AppContext.BaseDirectory;
    }
}

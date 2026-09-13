using System.IO;

namespace CircleToSearch;

internal static class AppDataDirectory
{
    public static void Initialize(AppPaths paths)
    {
        foreach (var directory in new[] { paths.DataDirectory, paths.LogsDirectory, paths.TempDirectory })
        {
            Directory.CreateDirectory(directory);
            var probePath = Path.Combine(directory, ".write-probe-" + Guid.NewGuid().ToString("N"));
            using var probe = new FileStream(probePath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 1, FileOptions.DeleteOnClose);
            probe.WriteByte(0);
            probe.Flush(flushToDisk: true);
        }
    }
}

using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;

namespace CircleToSearch.Search.Browser;

internal static class SearchBrowserExtension
{
    internal const string PackageHash = "061A5DE7B1EEDF6C1FE0AFDA40D453C427EEFC2CBCDBB680C3EED37DC5CEF2C8";

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

    private static string InstalledIdPath(string directory) => Path.Combine(directory, ".installed-id");
}

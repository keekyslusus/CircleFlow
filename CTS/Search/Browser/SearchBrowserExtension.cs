using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;

namespace CircleToSearch.Search.Browser;

internal static class SearchBrowserExtension
{
    internal const string PackageHash = "061A5DE7B1EEDF6C1FE0AFDA40D453C427EEFC2CBCDBB680C3EED37DC5CEF2C8";

    internal static string Prepare(string assetDirectory, string userDataFolder)
    {
        var archive = Path.Combine(assetDirectory, "Extensions", "uBlockOriginLite.zip");
        using var stream = File.OpenRead(archive);
        if (Convert.ToHexString(SHA256.HashData(stream)) != PackageHash)
            throw new InvalidDataException("The bundled uBlock Origin Lite package checksum does not match.");

        // Keep the registered directory stable across window openings and package updates.
        var directory = Path.Combine(userDataFolder, "CircleFlowExtensions", "uBlockOriginLite");
        var marker = Path.Combine(directory, ".package-sha256");
        if (File.Exists(marker) && File.ReadAllText(marker) == PackageHash &&
            File.Exists(Path.Combine(directory, "manifest.json"))) return directory;

        Directory.CreateDirectory(directory);
        stream.Position = 0;
        ZipFile.ExtractToDirectory(stream, directory, overwriteFiles: true);
        File.WriteAllText(marker, PackageHash);
        return directory;
    }
}

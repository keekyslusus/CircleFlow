using CircleToSearch.Search;
using CircleToSearch.Search.Browser;
using System.Text.Json;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class SearchBrowserExtensionTests
{
    [Fact]
    public void Bundled_extension_extracts_with_easylist_and_reuses_its_path()
    {
        var profile = Path.Combine(Path.GetTempPath(), "CircleFlowExtensionTests", Guid.NewGuid().ToString("N"));
        try
        {
            var directory = SearchBrowserExtension.Prepare(AppContext.BaseDirectory, profile);
            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "manifest.json")));
            Assert.Equal(3, manifest.RootElement.GetProperty("manifest_version").GetInt32());
            var rules = manifest.RootElement.GetProperty("declarative_net_request").GetProperty("rule_resources");
            foreach (var id in new[] { "easylist", "easyprivacy" })
                Assert.Contains(rules.EnumerateArray(), rule =>
                    rule.GetProperty("id").GetString() == id && rule.GetProperty("enabled").GetBoolean());
            var timestamp = File.GetLastWriteTimeUtc(Path.Combine(directory, "manifest.json"));
            Assert.Equal(directory, SearchBrowserExtension.Prepare(AppContext.BaseDirectory, profile));
            Assert.Equal(timestamp, File.GetLastWriteTimeUtc(Path.Combine(directory, "manifest.json")));
            Assert.True(File.Exists(Path.Combine(directory, "dashboard.html")));
        }
        finally { if (Directory.Exists(profile)) Directory.Delete(profile, true); }
    }

    [Fact]
    public void Modified_package_is_rejected_before_extraction()
    {
        var directory = Path.Combine(Path.GetTempPath(), "CircleFlowExtensionTests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(directory, "Extensions"));
            File.WriteAllText(Path.Combine(directory, "Extensions", "uBlockOriginLite.zip"), "invalid");
            Assert.Throws<InvalidDataException>(() => SearchBrowserExtension.Prepare(directory, Path.Combine(directory, "Profile")));
            Assert.False(Directory.Exists(Path.Combine(directory, "Profile")));
        }
        finally { Directory.Delete(directory, true); }
    }
}

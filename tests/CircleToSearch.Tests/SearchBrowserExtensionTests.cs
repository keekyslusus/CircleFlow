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
        var profile = Path.Combine(TestOutputPaths.TempDirectory, "CircleFlowExtensionTests", Guid.NewGuid().ToString("N"));
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
    public void Installed_id_and_enabled_lists_survive_reuse_and_are_forgotten_when_the_package_is_extracted_again()
    {
        var profile = Path.Combine(TestOutputPaths.TempDirectory, "CircleFlowExtensionTests", Guid.NewGuid().ToString("N"));
        try
        {
            var directory = SearchBrowserExtension.Prepare(AppContext.BaseDirectory, profile);
            Assert.Null(SearchBrowserExtension.InstalledId(directory));
            Assert.Null(SearchBrowserExtension.EnabledRulesets(directory));
            SearchBrowserExtension.RememberInstalled(directory, "extension-id");
            File.WriteAllText(Path.Combine(directory, ".enabled-rulesets"), "annoyances-others");
            SearchBrowserExtension.Prepare(AppContext.BaseDirectory, profile);
            Assert.Equal("extension-id", SearchBrowserExtension.InstalledId(directory));
            Assert.Equal("annoyances-others", SearchBrowserExtension.EnabledRulesets(directory));

            File.WriteAllText(Path.Combine(directory, ".package-sha256"), "previous package");
            SearchBrowserExtension.Prepare(AppContext.BaseDirectory, profile);
            Assert.Null(SearchBrowserExtension.InstalledId(directory));
            Assert.Null(SearchBrowserExtension.EnabledRulesets(directory));
        }
        finally { if (Directory.Exists(profile)) Directory.Delete(profile, true); }
    }

    [Fact]
    public void Installed_id_is_forgotten_when_the_app_folder_moves()
    {
        var root = Path.Combine(TestOutputPaths.TempDirectory, "CircleFlowExtensionTests", Guid.NewGuid().ToString("N"));
        var profile = Path.Combine(root, "before");
        var moved = Path.Combine(root, "after");
        try
        {
            var directory = SearchBrowserExtension.Prepare(AppContext.BaseDirectory, profile);
            SearchBrowserExtension.RememberInstalled(directory, "extension-id");
            var copied = directory.Replace(profile, moved);
            Directory.CreateDirectory(copied);
            // Copying what marks a finished extraction avoids moving files a virus scanner may still hold.
            foreach (var name in new[] { ".package-sha256", "manifest.json", ".installed-id" })
                File.Copy(Path.Combine(directory, name), Path.Combine(copied, name));

            var movedDirectory = SearchBrowserExtension.Prepare(AppContext.BaseDirectory, moved);
            Assert.Equal(copied, movedDirectory);
            Assert.True(File.Exists(Path.Combine(movedDirectory, ".installed-id")));
            Assert.Null(SearchBrowserExtension.InstalledId(movedDirectory));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void Modified_package_is_rejected_before_extraction()
    {
        var directory = Path.Combine(TestOutputPaths.TempDirectory, "CircleFlowExtensionTests", Guid.NewGuid().ToString("N"));
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

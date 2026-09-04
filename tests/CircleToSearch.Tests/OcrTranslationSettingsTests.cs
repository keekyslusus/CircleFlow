using System.Text.Json;
using CircleToSearch.Settings;
using CircleToSearch.TextRecognition;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class OcrTranslationSettingsTests
{
    [Fact]
    public void Settings_ignore_legacy_source_language_and_round_trip_translation_settings()
    {
        const string json = """{"OcrLanguageTag":"ru-RU","TranslationTargetLanguageTag":"en-US","TranslationPrivacyConsentAccepted":true}""";
        var restored = JsonSerializer.Deserialize<PluginSettings>(json)!;

        Assert.Equal("en-US", restored.TranslationTargetLanguageTag);
        Assert.True(restored.TranslationPrivacyConsentAccepted);
        Assert.Null(typeof(PluginSettings).GetProperty("OcrLanguageTag"));
    }

    [Fact]
    public void Catalog_exposes_only_the_installed_language_options_it_was_given()
    {
        var catalog = new OcrLanguageCatalog([new OcrLanguageOption("en-US", "English")]);

        var language = Assert.Single(catalog.AvailableLanguages);
        Assert.Equal("en-US", language.Tag);
        Assert.Equal("English", language.DisplayName);
    }
}

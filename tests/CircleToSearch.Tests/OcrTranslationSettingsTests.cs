using System.Text.Json;
using CircleToSearch.Settings;
using CircleToSearch.TextRecognition;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class OcrTranslationSettingsTests
{
    [Fact]
    public void Settings_round_trip_language_tags_and_privacy_consent()
    {
        var settings = new PluginSettings
        {
            OcrLanguageTag = "ru-RU",
            TranslationTargetLanguageTag = "en-US",
            ImageTranslationPrivacyConsentAccepted = true,
        };

        var restored = JsonSerializer.Deserialize<PluginSettings>(JsonSerializer.Serialize(settings))!;

        Assert.Equal("ru-RU", restored.OcrLanguageTag);
        Assert.Equal("en-US", restored.TranslationTargetLanguageTag);
        Assert.True(restored.ImageTranslationPrivacyConsentAccepted);
    }

    [Fact]
    public void Consent_flag_is_committed_only_if_storage_succeeds()
    {
        var settings = new PluginSettings();
        var saves = 0;

        Assert.Throws<InvalidOperationException>(() =>
            CircleToSearch.CompositionRoot.SaveTranslationConsent(settings, true, () =>
            {
                saves++;
                Assert.True(settings.ImageTranslationPrivacyConsentAccepted);
                throw new InvalidOperationException("storage failed");
            }));
        Assert.False(settings.ImageTranslationPrivacyConsentAccepted);
        CircleToSearch.CompositionRoot.SaveTranslationConsent(settings, true, () => saves++);
        Assert.True(settings.ImageTranslationPrivacyConsentAccepted);
        Assert.Equal(2, saves);

        Assert.Throws<InvalidOperationException>(() =>
            CircleToSearch.CompositionRoot.SaveTranslationConsent(settings, false, () =>
            {
                saves++;
                Assert.False(settings.ImageTranslationPrivacyConsentAccepted);
                throw new InvalidOperationException("storage failed");
            }));
        Assert.True(settings.ImageTranslationPrivacyConsentAccepted);
        CircleToSearch.CompositionRoot.SaveTranslationConsent(settings, false, () => saves++);
        Assert.False(settings.ImageTranslationPrivacyConsentAccepted);
        Assert.Equal(4, saves);
    }

    [Fact]
    public void Catalog_validates_case_insensitively_and_falls_back_for_missing_pack()
    {
        var catalog = new OcrLanguageCatalog([new OcrLanguageOption("en-US", "English")]);

        Assert.Equal("en-US", catalog.Validate("EN-us"));
        Assert.Null(catalog.Validate("ru-RU"));
        Assert.Null(catalog.Validate(string.Empty));
    }
}

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
        var settings = new AppSettings
        {
            OcrLanguageTag = "ru-RU",
            ImageTranslationPrivacyConsentAccepted = true,
        };

        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;

        Assert.Equal("ru-RU", restored.OcrLanguageTag);
        Assert.True(restored.ImageTranslationPrivacyConsentAccepted);
    }

    [Fact]
    public void Consent_flag_is_committed_only_if_storage_succeeds()
    {
        var saves = 0;
        var fail = true;
        SettingsService? service = null;
        service = TestSettings.Create(save: candidate =>
        {
            saves++;
            Assert.NotEqual(candidate.ImageTranslationPrivacyConsentAccepted,
                service!.Snapshot.ImageTranslationPrivacyConsentAccepted);
            if (fail) throw new IOException("storage failed");
        });
        Assert.False(service.SetTranslationConsent(true).Success);
        Assert.False(service.Snapshot.ImageTranslationPrivacyConsentAccepted);
        fail = false;
        Assert.True(service.SetTranslationConsent(true).Success);
        Assert.True(service.Snapshot.ImageTranslationPrivacyConsentAccepted);
        fail = true;
        Assert.False(service.SetTranslationConsent(false).Success);
        Assert.True(service.Snapshot.ImageTranslationPrivacyConsentAccepted);
        fail = false;
        Assert.True(service.SetTranslationConsent(false).Success);
        Assert.False(service.Snapshot.ImageTranslationPrivacyConsentAccepted);
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

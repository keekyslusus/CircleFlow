namespace CircleToSearch.Ui;

using System.Globalization;

public sealed class UiStrings
{
    private readonly Func<string, string> _getTranslation;

    public UiStrings(Func<string, string> getTranslation)
    {
        _getTranslation = getTranslation;
    }

    public string TraceMoeProviderName => Get("plugin_circletosearch_trace_provider_name");
    public string TraceMoeProviderDescription => Get("plugin_circletosearch_trace_provider_description");
    public string TraceSearching => Get("plugin_circletosearch_trace_searching");
    public string TraceNoMatch => Get("plugin_circletosearch_trace_no_match");
    public string TraceRateLimited => Get("plugin_circletosearch_trace_rate_limited");
    public string TraceOpen => Get("plugin_circletosearch_trace_open");
    public string TraceCopy => Get("plugin_circletosearch_trace_copy");
    public string TraceEpisode => Get("plugin_circletosearch_trace_episode");
    public string PinterestProviderName => Get("plugin_circletosearch_pinterest_provider_name");
    public string PinterestProviderDescription => Get("plugin_circletosearch_pinterest_provider_description");
    public string PinterestSearching => Get("plugin_circletosearch_pinterest_searching");
    public string PinterestSummary(int count) => Get("plugin_circletosearch_pinterest_summary", count);
    public string PinterestNoMatch => Get("plugin_circletosearch_pinterest_no_match");
    public string PinterestOpen => Get("plugin_circletosearch_pinterest_open");
    public string PinterestShowAll => Get("plugin_circletosearch_pinterest_show_all");
    public string PinterestMore(int count) => Get("plugin_circletosearch_pinterest_more", count);
    public string PinterestBack => Get("plugin_circletosearch_pinterest_back");
    public string PluginTitle => Get("plugin_circletosearch_plugin_name");
    public string TrayOpen => Get("app_tray_open");
    public string TrayOpenWithHotkey(string gesture) => Get("app_tray_open_hotkey", gesture);
    public string TraySettings => Get("app_tray_settings");
    public string TraySupport => Get("app_tray_support");
    public string TrayExit => Get("app_tray_exit");
    public string SettingsWindowTitle => Get("app_settings_window_title");
    internal string SettingsPreviewText(string key) => Get("app_settings_" + key);
    internal string OnboardingText(string key) => Get("app_onboarding_" + key);
    internal string TestBrowserText(string key) => Get("app_test_browser_" + key);
    internal string OnboardingStep(int step, int count) => Get("app_onboarding_step", step, count);
    public string SettingsShortcutSaved => Get("app_settings_shortcut_saved");
    public string SettingsShortcutInvalid => Get("app_settings_invalid_shortcut");
    public string SettingsShortcutUnavailable(string gesture) => Get("app_settings_shortcut_unavailable", gesture);
    public string SettingsResetDone => Get("app_settings_reset_done");
    public string SettingsOpenFolderFailed => Get("app_settings_open_folder_failed");
    public string SettingsOpenLanguageSettingsFailed => Get("app_settings_open_language_settings_failed");
    public string SettingsRuntimeMissing => Get("app_settings_runtime_missing");
    public string SettingsVersion(string version) => Get("app_settings_version_value", version);
    internal string SettingsMusicHistoryNoMatch(string query) => Get("app_settings_history_no_match", query);
    public string StartupFailed => Get("app_startup_failed");
    public string StartupLanguageFailed => Get("app_startup_language_failed");
    public string StartupDataFailed(string path) => Get("app_startup_data_failed", path);
    public string StorageSaveFailed => Get("app_storage_save_failed");
    public string StorageLoadFailed => Get("app_storage_load_failed");
    public string StorageRecovered => Get("app_storage_recovered");
    public string StorageRecoveryFailed => Get("app_storage_recovery_failed");
    public string HotkeyConflict(string gesture) => Get("app_hotkey_conflict", gesture);
    public string HotkeyRollbackFailed => Get("app_hotkey_rollback_failed");
    public string ActivationFailed => Get("app_activation_failed");
    public string ShutdownFailed => Get("app_shutdown_failed");
    public string ShutdownTimedOut => Get("app_shutdown_timed_out");
    public string UpdateAvailableTitle => Get("app_update_available_title");
    public string UpdateAvailable(string version) => Get("app_update_available", version);
    public string UpdateInstall => Get("app_update_install");
    public string UpdateDownloadingTitle => Get("app_update_downloading_title");
    public string UpdateDownloading(string version) => Get("app_update_downloading", version);
    public string UpdateFailedTitle => Get("app_update_failed_title");
    public string UpdateFailed => Get("app_update_failed");
    public string WebViewRuntimeMissingTitle => Get("app_webview_runtime_missing_title");
    public string WebViewRuntimeMissing => Get("app_webview_runtime_missing");
    public string WebViewRuntimeDownload => Get("app_webview_runtime_download");
    public string BrowserProfileOutsideData => Get("app_browser_profile_outside_data");
    public string PluginDescription => Get("plugin_circletosearch_plugin_description");
    public string GoogleLensProviderName => Get("plugin_circletosearch_google_lens_provider_name");
    public string YandexImagesProviderName => Get("plugin_circletosearch_yandex_images_provider_name");
    public string SelectionPrompt => Get("plugin_circletosearch_selection_prompt");
    public string SelectionTooSmall => Get("plugin_circletosearch_selection_too_small");
    public string CancelKeyName => Get("plugin_circletosearch_cancel_key_name");
    public string CancelAction => Get("plugin_circletosearch_cancel_action");
    public string AltKeyName => Get("app_settings_alt");
    public string LeftMouseButton => Get("plugin_circletosearch_left_mouse_button");
    public string RightMouseButton => Get("plugin_circletosearch_right_mouse_button");
    public string SelectionHintSearch => Get("plugin_circletosearch_selection_hint_search");
    public string SelectionHintActions => Get("plugin_circletosearch_selection_hint_actions");
    public string SelectionHintSearchOverText => Get("plugin_circletosearch_selection_hint_search_over_text");
    public string MusicRecognitionAction => Get("plugin_circletosearch_music_recognition_action");
    public string CancelMusicRecognition => Get("plugin_circletosearch_music_cancel_action");
    public string GoogleProviderShortLabel => Get("plugin_circletosearch_google_provider_short_label");
    public string YandexProviderShortLabel => Get("plugin_circletosearch_yandex_provider_short_label");
    public string Listening => Get("plugin_circletosearch_music_listening");
    public string TryAgain => Get("plugin_circletosearch_music_try_again");
    public string Retry => Get("plugin_circletosearch_music_retry");
    public string CopyTrackInfo => Get("plugin_circletosearch_music_copy_track_info");
    public string Copied => Get("plugin_circletosearch_music_copied");
    public string CopyFailed => Get("plugin_circletosearch_copy_failed");
    public string ImageCopied => Get("plugin_circletosearch_image_copied");
    public string ImageSave => Get("plugin_circletosearch_image_save");
    public string ImageSaveTitle => Get("plugin_circletosearch_image_save_title");
    public string ImageSaveFilter => Get("plugin_circletosearch_image_save_filter");
    public string ImageFileName => Get("plugin_circletosearch_image_file_name");
    public string Ask => Get("plugin_circletosearch_image_ask");
    public string AskPlaceholder => Get("plugin_circletosearch_image_ask_placeholder");
    public string AskSend => Get("plugin_circletosearch_image_ask_send");
    public string Close => Get("plugin_circletosearch_close");
    public string MusicResultTitle => Get("plugin_circletosearch_music_result_title");
    public string MusicNoMatch => Get("plugin_circletosearch_music_no_match");
    public string MusicNoAudio => Get("plugin_circletosearch_music_no_audio");
    public string MusicRateLimited => Get("plugin_circletosearch_music_rate_limited");
    public string MusicNetworkError => Get("plugin_circletosearch_music_network_error");
    public string MusicDeviceError => Get("plugin_circletosearch_music_device_error");
    public string OpenInShazam => Get("plugin_circletosearch_music_open_in_shazam");
    public string DebugOverlayTitle => Get("plugin_circletosearch_debug_overlay_title");
    public string DebugMusicSection => Get("plugin_circletosearch_debug_music_section");
    public string DebugMusicLive => Get("plugin_circletosearch_debug_music_live");
    public string DebugMusicRippleSoft => Get("plugin_circletosearch_debug_music_ripple_soft");
    public string DebugMusicRippleMedium => Get("plugin_circletosearch_debug_music_ripple_medium");
    public string DebugMusicRippleStrong => Get("plugin_circletosearch_debug_music_ripple_strong");
    public string DebugMusicMatched => Get("plugin_circletosearch_debug_music_matched");
    public string DebugMusicNoMatch => Get("plugin_circletosearch_debug_music_no_match");
    public string DebugMusicNoAudio => Get("plugin_circletosearch_debug_music_no_audio");
    public string DebugMusicDeviceError => Get("plugin_circletosearch_debug_music_device_error");
    public string DebugMusicServiceError => Get("plugin_circletosearch_debug_music_service_error");
    public string DebugMusicRateLimited => Get("plugin_circletosearch_debug_music_rate_limited");
    public string DebugToastSection => Get("plugin_circletosearch_debug_toast_section");
    public string DebugToastNeutral => Get("plugin_circletosearch_debug_toast_neutral");
    public string DebugToastError => Get("plugin_circletosearch_debug_toast_error");
    public string DebugToastSuccess => Get("plugin_circletosearch_debug_toast_success");
    public string DebugTranslationSection => Get("plugin_circletosearch_debug_translation_section");
    public string DebugResetTranslationConsent => Get("plugin_circletosearch_debug_reset_translation_consent");
    public string DebugTranslationConsentReset => Get("plugin_circletosearch_debug_translation_consent_reset");
    public string DebugMusicTrackTitle => Get("plugin_circletosearch_debug_music_track_title");
    public string DebugMusicTrackArtist => Get("plugin_circletosearch_debug_music_track_artist");
    public string DebugMusicTrackAlbum => Get("plugin_circletosearch_debug_music_track_album");
    public string DebugMusicTrackGenre => Get("plugin_circletosearch_debug_music_track_genre");
    public string QueryTitle => Get("plugin_circletosearch_query_title");
    public string SettingsHotkeyLabel => Get("plugin_circletosearch_settings_hotkey_label");
    public string SettingsMaxImageSideLabel => Get("plugin_circletosearch_settings_max_image_side_label");
    public string SettingsApply => Get("plugin_circletosearch_settings_apply");
    public string HotkeyRegistered => Get("plugin_circletosearch_hotkey_registered");
    public string HotkeyRegistrationFailed => Get("plugin_circletosearch_hotkey_registration_failed");
    public string HotkeyStatusNone => Get("plugin_circletosearch_hotkey_status_none");
    public string SearchUnexpectedResponse => Get("plugin_circletosearch_search_unexpected_response");
    public string SearchUnexpectedResultsLocation => Get("plugin_circletosearch_search_unexpected_results_location");
    public string SearchTimedOut => Get("plugin_circletosearch_search_timed_out");
    public string SearchNetworkError => Get("plugin_circletosearch_search_network_error");
    public string SearchUploadFailed => Get("plugin_circletosearch_search_upload_failed");
    public string ResultsUrlOpenFailed => Get("plugin_circletosearch_results_url_open_failed");
    public string SearchBrowserWindowTitle(string providerName) =>
        Get("plugin_circletosearch_search_browser_window_title", providerName);

    public string SearchBrowserLoading(string providerName) =>
        Get("plugin_circletosearch_search_browser_loading", providerName);

    public string SearchBrowserShowFailed(string providerName) =>
        Get("plugin_circletosearch_search_browser_show_failed", providerName);
    public string TextCopy => Get("plugin_circletosearch_text_copy");
    public string TextSearch => Get("plugin_circletosearch_text_search");
    public string TextSearchTooLong => Get("plugin_circletosearch_text_search_too_long");
    public string TextSearchOpenFailed => Get("plugin_circletosearch_text_search_open_failed");
    public string Translate => Get("plugin_circletosearch_translate");
    public string Translating => Get("plugin_circletosearch_translating");
    public string ShowOriginal => Get("plugin_circletosearch_show_original");
    public string TranslationConsentTitle => Get("plugin_circletosearch_translation_consent_title");
    public string TranslationResultTitle => Get("plugin_circletosearch_translation_result_title");
    public string TranslatedTextPrompt => Get("plugin_circletosearch_translated_text_prompt");
    public string TranslationConsentMessage => Get("plugin_circletosearch_translation_consent_message");
    public string Continue => Get("plugin_circletosearch_continue");
    public string ConsentCancel => Get("plugin_circletosearch_consent_cancel");
    public string TranslationNetworkError => Get("plugin_circletosearch_translation_network_error");
    public string TranslationTimedOut => Get("plugin_circletosearch_translation_timed_out");
    public string TranslationRateLimited => Get("plugin_circletosearch_translation_rate_limited");
    public string TranslationFailed => Get("plugin_circletosearch_translation_failed");
    public string SettingsOcrLanguageLabel => Get("plugin_circletosearch_settings_ocr_language_label");
    public string SettingsTranslationTargetLabel => Get("plugin_circletosearch_settings_translation_target_label");
    public string SystemDefaultLanguage => Get("plugin_circletosearch_system_default_language");
    public string OcrLanguageChanged(string language) => Get("plugin_circletosearch_ocr_language_changed", language);
    public string OcrProcessing => Get("plugin_circletosearch_ocr_processing");
    public string OcrLanguageUnavailable(string language) => Get("plugin_circletosearch_ocr_language_unavailable", language);
    public string OcrUnknownLanguage => Get("plugin_circletosearch_ocr_unknown_language");
    public string OcrNoText => Get("plugin_circletosearch_ocr_no_text");
    public string OcrFailed => Get("plugin_circletosearch_ocr_failed");
    public string OcrPlatformUnavailable => Get("plugin_circletosearch_ocr_platform_unavailable");

    public string VisualSearchQuerySubtitle(string hotkeyStatus) =>
        Get("plugin_circletosearch_query_subtitle", hotkeyStatus);

    public string SearchProvider(string displayName) =>
        Get("plugin_circletosearch_search_provider", displayName);

    public string SelectSearchProvider(string displayName) =>
        Get("plugin_circletosearch_select_search_provider", displayName);

    public string SearchBrowserRuntime(string? version) => version is null
        ? Get("plugin_circletosearch_search_browser_runtime_not_detected")
        : Get("plugin_circletosearch_search_browser_runtime", version);

    public string SavingFailed(string detail) => Get("plugin_circletosearch_saving_failed", detail);

    public string HotkeyStatusActive(string gesture) =>
        Get("plugin_circletosearch_hotkey_status_active", gesture);

    public string HotkeyStatusUnavailable(string gesture) =>
        Get("plugin_circletosearch_hotkey_status_unavailable", gesture);

    public string SearchUnexpectedStatus(int? statusCode) =>
        Get("plugin_circletosearch_search_unexpected_status", statusCode);

    public string BrowserRuntimeRequired(string providerName) =>
        Get("plugin_circletosearch_browser_runtime_required", providerName);

    public string BrowserImageAttachmentFailed(string providerName) =>
        Get("plugin_circletosearch_browser_image_attachment_failed", providerName);

    public string StartingSelectionFailed(string detail) =>
        Get("plugin_circletosearch_starting_selection_failed", detail);

    public string SearchFailed(string detail) => Get("plugin_circletosearch_search_failed", detail);

    public string CopiedText(string preview) =>
        Get("plugin_circletosearch_copied_text", preview);

    public string MusicMatchSubtitle(string? album, string? genre)
    {
        if (!string.IsNullOrWhiteSpace(album) && !string.IsNullOrWhiteSpace(genre))
            return Get("plugin_circletosearch_music_match_album_genre", album, genre);
        if (!string.IsNullOrWhiteSpace(album))
            return Get("plugin_circletosearch_music_match_album", album);
        if (!string.IsNullOrWhiteSpace(genre))
            return Get("plugin_circletosearch_music_match_genre", genre);
        return Get("plugin_circletosearch_music_match");
    }

    private string Get(string key, params object?[] arguments)
    {
        var template = _getTranslation(key);
        return arguments.Length == 0
            ? template
            : string.Format(CultureInfo.CurrentCulture, template, arguments);
    }
}

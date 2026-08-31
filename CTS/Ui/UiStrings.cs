namespace CircleToSearch.Ui;

using System.Globalization;

public sealed class UiStrings
{
    private readonly Func<string, string> _getTranslation;

    public UiStrings(Func<string, string> getTranslation)
    {
        ArgumentNullException.ThrowIfNull(getTranslation);
        _getTranslation = getTranslation;
    }

    public string PluginTitle => Get("plugin_circletosearch_plugin_name");
    public string PluginDescription => Get("plugin_circletosearch_plugin_description");
    public string GoogleLensProviderName => Get("plugin_circletosearch_google_lens_provider_name");
    public string YandexImagesProviderName => Get("plugin_circletosearch_yandex_images_provider_name");
    public string SelectionPrompt => Get("plugin_circletosearch_selection_prompt");
    public string SelectionTooSmall => Get("plugin_circletosearch_selection_too_small");
    public string CancelKeyName => Get("plugin_circletosearch_cancel_key_name");
    public string CancelAction => Get("plugin_circletosearch_cancel_action");
    public string MusicRecognitionAction => Get("plugin_circletosearch_music_recognition_action");
    public string CancelMusicRecognition => Get("plugin_circletosearch_music_cancel_action");
    public string GoogleProviderShortLabel => Get("plugin_circletosearch_google_provider_short_label");
    public string YandexProviderShortLabel => Get("plugin_circletosearch_yandex_provider_short_label");
    public string Listening => Get("plugin_circletosearch_music_listening");
    public string TryAgain => Get("plugin_circletosearch_music_try_again");
    public string Retry => Get("plugin_circletosearch_music_retry");
    public string CopyTrackInfo => Get("plugin_circletosearch_music_copy_track_info");
    public string Copied => Get("plugin_circletosearch_music_copied");
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
    public string GoogleLensWindowTitle => Get("plugin_circletosearch_google_lens_window_title");
    public string GoogleLensLoading => Get("plugin_circletosearch_google_lens_loading");

    public string VisualSearchQuerySubtitle(string hotkeyStatus) =>
        Get("plugin_circletosearch_query_subtitle", hotkeyStatus);

    public string SearchProvider(string displayName) =>
        Get("plugin_circletosearch_search_provider", displayName);

    public string SelectSearchProvider(string displayName) =>
        Get("plugin_circletosearch_select_search_provider", displayName);

    public string GoogleLensRuntime(string? version) => version is null
        ? Get("plugin_circletosearch_google_lens_runtime_not_detected")
        : Get("plugin_circletosearch_google_lens_runtime", version);

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

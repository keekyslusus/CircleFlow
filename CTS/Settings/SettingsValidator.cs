using System.Globalization;
using CircleToSearch.Capture;
using CircleToSearch.Interop;
using CircleToSearch.Search;
using CircleToSearch.Trigger;

namespace CircleToSearch.Settings;

internal static class SettingsValidator
{
    public static AppSettings Normalize(AppSettings settings, out IReadOnlyList<string> invalidFields)
    {
        var invalid = new List<string>();
        var defaults = new AppSettings();
        int Range(int value, int min, int max, int fallback, string field)
        {
            if (value >= min && value <= max) return value;
            invalid.Add(field);
            return fallback;
        }
        string Language(string? value, string field)
        {
            if (value is not null)
            {
                if (string.IsNullOrWhiteSpace(value)) return string.Empty;
                try { return CultureInfo.GetCultureInfo(value.Trim().Replace('_', '-')).Name; }
                catch (CultureNotFoundException) { }
            }
            invalid.Add(field);
            return string.Empty;
        }

        var provider = new[] { SearchProviderIds.GoogleLens, SearchProviderIds.YandexImages, SearchProviderIds.TraceMoe }
            .FirstOrDefault(id => string.Equals(id, settings.SearchProviderId?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (provider is null) invalid.Add(nameof(AppSettings.SearchProviderId));
        var engine = settings.TextSearchEngineId switch
        {
            null => null,
            var id when string.IsNullOrWhiteSpace(id) => TextSearchEngines.MatchImageSearch,
            var id => TextSearchEngines.Find(id)?.Id,
        };
        if (engine is null) invalid.Add(nameof(AppSettings.TextSearchEngineId));
        if ((settings.HiddenToolbarActions & ~SelectionToolbarAction.All) != 0) invalid.Add(nameof(AppSettings.HiddenToolbarActions));
        var cleanupDays = settings.BrowserDataCleanupDays == BrowserDataCleanup.Never
            || BrowserDataCleanup.IntervalDays.Contains(settings.BrowserDataCleanupDays)
            ? settings.BrowserDataCleanupDays
            : (int?)null;
        if (cleanupDays is null) invalid.Add(nameof(AppSettings.BrowserDataCleanupDays));
        var gesture = defaults.HotkeyGesture;
        if (HotkeyGestureParser.TryParse(settings.HotkeyGesture, out var modifiers, out var key))
            gesture = HotkeyGestureParser.Format(modifiers, key);
        else invalid.Add(nameof(AppSettings.HotkeyGesture));
        var normalized = settings with
        {
            SearchProviderId = provider ?? defaults.SearchProviderId,
            TextSearchEngineId = engine ?? defaults.TextSearchEngineId,
            HotkeyGesture = gesture,
            HiddenToolbarActions = settings.HiddenToolbarActions & SelectionToolbarAction.All,
            BrowserDataCleanupDays = cleanupDays ?? defaults.BrowserDataCleanupDays,
            MaxLongSidePx = Range(settings.MaxLongSidePx, 256, 8000, defaults.MaxLongSidePx, nameof(AppSettings.MaxLongSidePx)),
            PaddingPx = Range(settings.PaddingPx, 0, 100, defaults.PaddingPx, nameof(AppSettings.PaddingPx)),
            HideDelayMilliseconds = Range(settings.HideDelayMilliseconds, 0, 2000, defaults.HideDelayMilliseconds, nameof(AppSettings.HideDelayMilliseconds)),
            LassoMinDiagonalPx = Range(settings.LassoMinDiagonalPx, 1, 1000, defaults.LassoMinDiagonalPx, nameof(AppSettings.LassoMinDiagonalPx)),
            OcrLanguageTag = Language(settings.OcrLanguageTag, nameof(AppSettings.OcrLanguageTag)),
        };
        invalidFields = invalid.AsReadOnly();
        return normalized;
    }
}

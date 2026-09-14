using System.IO;
using System.Text.Json;

namespace CircleToSearch.Settings;

internal sealed record SettingsLoadResult(AppSettings Settings, bool Recovered, IReadOnlyList<string> ResetFields);

internal sealed class SettingsStore(AppPaths paths)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly object _gate = new();

    public SettingsLoadResult Load()
    {
        lock (_gate)
        {
            if (!File.Exists(paths.SettingsFilePath))
            {
                var defaults = new AppSettings();
                Write(defaults, createBackup: false);
                return new(defaults, false, []);
            }
            AppSettings settings;
            IReadOnlyList<string> invalidTypes = [];
            var recovered = false;
            try { settings = Read(paths.SettingsFilePath, out invalidTypes); }
            catch (JsonException)
            {
                var corrupt = Path.Combine(paths.DataDirectory,
                    $"settings.corrupt-{DateTime.UtcNow:yyyyMMddTHHmmssfffffff}-{Guid.NewGuid():N}.json");
                File.Copy(paths.SettingsFilePath, corrupt, overwrite: false);
                settings = new AppSettings();
                if (File.Exists(paths.SettingsBackupFilePath))
                    try { settings = Read(paths.SettingsBackupFilePath, out invalidTypes); }
                    catch (JsonException) { }
                recovered = true;
            }
            var normalized = SettingsValidator.Normalize(settings, out var invalidFields);
            var resetFields = invalidTypes.Concat(invalidFields).Distinct().ToArray();
            if (recovered || resetFields.Length != 0 || normalized != settings)
                Write(normalized, createBackup: !recovered && resetFields.Length == 0);
            return new(normalized, recovered, Array.AsReadOnly(resetFields));
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var normalized = SettingsValidator.Normalize(settings, out var invalidFields);
        if (invalidFields.Count != 0) throw new ArgumentException("Settings contain invalid fields.", nameof(settings));
        lock (_gate) Write(normalized, createBackup: true);
    }

    private static AppSettings Read(string path, out IReadOnlyList<string> invalidFields)
    {
        using var stream = File.OpenRead(path);
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new JsonException("The settings document must be an object.");
        var invalid = new List<string>();
        int Number(string field, int fallback)
        {
            if (!root.TryGetProperty(field, out var value)) return fallback;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) return number;
            invalid.Add(field);
            return fallback;
        }
        string Text(string field, string fallback)
        {
            if (!root.TryGetProperty(field, out var value)) return fallback;
            if (value.ValueKind == JsonValueKind.String) return value.GetString()!;
            invalid.Add(field);
            return fallback;
        }
        var defaults = new AppSettings();
        var consent = false;
        if (root.TryGetProperty(nameof(AppSettings.ImageTranslationPrivacyConsentAccepted), out var value))
        {
            if (value.ValueKind is JsonValueKind.True or JsonValueKind.False) consent = value.GetBoolean();
            else invalid.Add(nameof(AppSettings.ImageTranslationPrivacyConsentAccepted));
        }
        var settings = defaults with
        {
            SearchProviderId = Text(nameof(AppSettings.SearchProviderId), defaults.SearchProviderId),
            HotkeyGesture = Text(nameof(AppSettings.HotkeyGesture), defaults.HotkeyGesture),
            MaxLongSidePx = Number(nameof(AppSettings.MaxLongSidePx), defaults.MaxLongSidePx),
            PaddingPx = Number(nameof(AppSettings.PaddingPx), defaults.PaddingPx),
            HideDelayMilliseconds = Number(nameof(AppSettings.HideDelayMilliseconds), defaults.HideDelayMilliseconds),
            LassoMinDiagonalPx = Number(nameof(AppSettings.LassoMinDiagonalPx), defaults.LassoMinDiagonalPx),
            OcrLanguageTag = Text(nameof(AppSettings.OcrLanguageTag), defaults.OcrLanguageTag),
            TranslationTargetLanguageTag = Text(nameof(AppSettings.TranslationTargetLanguageTag), defaults.TranslationTargetLanguageTag),
            ImageTranslationPrivacyConsentAccepted = consent,
        };
        invalidFields = invalid.AsReadOnly();
        return settings;
    }

    private void Write(AppSettings settings, bool createBackup)
    {
        var temporary = Path.Combine(paths.DataDirectory, $"settings-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, settings, JsonOptions);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(paths.SettingsFilePath))
                File.Replace(temporary, paths.SettingsFilePath, createBackup ? paths.SettingsBackupFilePath : null);
            else File.Move(temporary, paths.SettingsFilePath);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}

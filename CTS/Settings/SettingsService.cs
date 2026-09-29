using CircleToSearch.Capture;
using CircleToSearch.Trigger;

namespace CircleToSearch.Settings;

public sealed record SettingsEdits
{
    public int? MaxLongSidePx { get; init; }
    public int? PaddingPx { get; init; }
    public int? HideDelayMilliseconds { get; init; }
    public int? LassoMinDiagonalPx { get; init; }
    public string? OcrLanguageTag { get; init; }
}

public enum SettingsChangeStatus { Success, Invalid, SaveFailed, HotkeyUnavailable, HotkeyRollbackFailed }

public sealed record SettingsChangeResult(SettingsChangeStatus Status, HotkeyRegistrationStatus Hotkey,
    IReadOnlyList<string> InvalidFields)
{
    public bool Success => Status == SettingsChangeStatus.Success;
    public void ThrowIfFailed(string message)
    {
        if (!Success) throw new InvalidOperationException(message);
    }
}

public sealed class SettingsService
{
    private readonly Action<AppSettings> _save;
    private readonly Func<string?, HotkeyApplyResult> _applyHotkey;
    private readonly PluginLog _log;
    private readonly object _gate = new();
    private AppSettings _current;
    private HotkeyRegistrationStatus _hotkey;

    public SettingsService(AppSettings initial, Action<AppSettings> save,
        Func<string?, HotkeyApplyResult> applyHotkey, PluginLog log)
    {
        _current = SettingsValidator.Normalize(initial, out var invalid);
        if (invalid.Count != 0) throw new ArgumentException("Initial settings must be valid.", nameof(initial));
        _save = save ?? throw new ArgumentNullException(nameof(save));
        _applyHotkey = applyHotkey ?? throw new ArgumentNullException(nameof(applyHotkey));
        _log = log;
        _hotkey = new(_current.HotkeyGesture, false);
    }

    public AppSettings Snapshot { get { lock (_gate) return _current; } }
    public HotkeyRegistrationStatus HotkeyStatus { get { lock (_gate) return _hotkey; } }

    public SettingsChangeResult Apply(SettingsEdits edits)
    {
        ArgumentNullException.ThrowIfNull(edits);
        lock (_gate) return Commit(_current with
        {
            MaxLongSidePx = edits.MaxLongSidePx ?? _current.MaxLongSidePx,
            PaddingPx = edits.PaddingPx ?? _current.PaddingPx,
            HideDelayMilliseconds = edits.HideDelayMilliseconds ?? _current.HideDelayMilliseconds,
            LassoMinDiagonalPx = edits.LassoMinDiagonalPx ?? _current.LassoMinDiagonalPx,
            OcrLanguageTag = edits.OcrLanguageTag ?? _current.OcrLanguageTag,
        });
    }

    public SettingsChangeResult SetProvider(string providerId)
    {
        lock (_gate) return Commit(_current with { SearchProviderId = providerId });
    }

    public SettingsChangeResult SetTextSearchEngine(string engineId)
    {
        lock (_gate) return Commit(_current with { TextSearchEngineId = engineId });
    }

    public SettingsChangeResult SetTextSearchInBuiltInBrowser(bool builtIn)
    {
        lock (_gate) return Commit(_current with { TextSearchInBuiltInBrowser = builtIn });
    }

    public SettingsChangeResult SetIgnoreHotkeyInFullscreen(bool ignore)
    {
        lock (_gate) return Commit(_current with { IgnoreHotkeyInFullscreen = ignore });
    }

    public SettingsChangeResult SetHiddenToolbarActions(SelectionToolbarAction hidden)
    {
        lock (_gate) return Commit(_current with { HiddenToolbarActions = hidden });
    }

    public SettingsChangeResult SetBrowserDataCleanupDays(int days)
    {
        lock (_gate) return Commit(_current with { BrowserDataCleanupDays = days });
    }

    public SettingsChangeResult SetSaveMusicHistory(bool save)
    {
        lock (_gate) return Commit(_current with { SaveMusicHistory = save });
    }

    public SettingsChangeResult SetMusicHistoryRetentionDays(int days)
    {
        lock (_gate) return Commit(_current with { MusicHistoryRetentionDays = days });
    }

    public SettingsChangeResult SetAppLanguage(string languageTag)
    {
        lock (_gate) return Commit(_current with { AppLanguageTag = languageTag });
    }

    public SettingsChangeResult SetTranslationConsent(bool accepted)
    {
        lock (_gate) return Commit(_current with { ImageTranslationPrivacyConsentAccepted = accepted });
    }

    public SettingsChangeResult CompleteOnboarding()
    {
        lock (_gate) return Commit(_current with { OnboardingCompleted = true });
    }

    public SettingsChangeResult InitializeHotkey()
    {
        lock (_gate) return Result(AttemptHotkey(_current.HotkeyGesture).Success
            ? SettingsChangeStatus.Success : SettingsChangeStatus.HotkeyUnavailable);
    }

    public SettingsChangeResult ChangeHotkey(string gesture)
    {
        lock (_gate)
        {
            var candidate = SettingsValidator.Normalize(_current with { HotkeyGesture = gesture }, out var invalid);
            if (invalid.Count != 0) return Result(SettingsChangeStatus.Invalid, invalid);
            if (candidate == _current && _hotkey.IsActive && _hotkey.Gesture == candidate.HotkeyGesture)
                return Result(SettingsChangeStatus.Success);
            var previous = _hotkey;
            var applied = AttemptHotkey(candidate.HotkeyGesture);
            if (!applied.Success)
                return Result(previous.IsActive && _hotkey != previous
                    ? SettingsChangeStatus.HotkeyRollbackFailed : SettingsChangeStatus.HotkeyUnavailable);
            try
            {
                _save(candidate);
                _current = candidate;
                return Result(SettingsChangeStatus.Success);
            }
            catch (Exception exception)
            {
                _log.SafeError(nameof(SettingsService), "save-hotkey", exception);
                var restored = AttemptHotkey(previous.IsActive ? previous.Gesture : null);
                return Result(restored.Success && (previous.IsActive ? _hotkey == previous : !_hotkey.IsActive)
                    ? SettingsChangeStatus.SaveFailed : SettingsChangeStatus.HotkeyRollbackFailed);
            }
        }
    }

    private SettingsChangeResult Commit(AppSettings candidate)
    {
        candidate = SettingsValidator.Normalize(candidate, out var invalid);
        if (invalid.Count != 0) return Result(SettingsChangeStatus.Invalid, invalid);
        if (candidate == _current) return Result(SettingsChangeStatus.Success);
        try { _save(candidate); }
        catch (Exception exception)
        {
            _log.SafeError(nameof(SettingsService), "save-settings", exception);
            return Result(SettingsChangeStatus.SaveFailed);
        }
        _current = candidate;
        return Result(SettingsChangeStatus.Success);
    }

    private HotkeyApplyResult AttemptHotkey(string? gesture)
    {
        HotkeyApplyResult result;
        try { result = _applyHotkey(gesture); }
        catch (Exception exception)
        {
            _log.SafeError(nameof(SettingsService), "apply-hotkey", exception);
            result = new(false, new(gesture ?? string.Empty, false));
        }
        _hotkey = result.Status;
        return result;
    }

    private SettingsChangeResult Result(SettingsChangeStatus status, IReadOnlyList<string>? invalid = null) =>
        new(status, _hotkey, invalid ?? []);
}

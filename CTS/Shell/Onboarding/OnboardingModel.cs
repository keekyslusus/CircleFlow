using CircleToSearch.Settings;
using CircleToSearch.Ui;

namespace CircleToSearch.Shell.Onboarding;

internal sealed class OnboardingModel(SettingsService settings, WindowsStartupRegistration startup, UiStrings strings,
    Action openOverlay)
{
    public bool IsCompleted => settings.Snapshot.OnboardingCompleted;
    public string HotkeyGesture => settings.Snapshot.HotkeyGesture;
    public bool HotkeyActive => settings.HotkeyStatus is { IsActive: true } status && status.Gesture == HotkeyGesture;
    public bool LaunchAtStartup => startup.IsEnabled;

    public string ChangeHotkey(string gesture) =>
        ShortcutText.ChangeMessage(settings.ChangeHotkey(gesture), gesture, strings.SettingsShortcutSaved, strings);

    public bool SelectLaunchAtStartup(bool enabled) => enabled == startup.IsEnabled || startup.TrySet(enabled);

    public void OpenOverlay() => openOverlay();

    // A failed save is already logged; the worst outcome is seeing onboarding again on the next start.
    public void Complete() => settings.CompleteOnboarding();
}

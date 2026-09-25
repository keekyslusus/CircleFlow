using System.Windows;
using System.Text.Json;
using CircleToSearch.Settings;
using CircleToSearch.Interop;
using CircleToSearch.Trigger;
using Xunit;

namespace CircleToSearch.Tests;

[Trait("Category", "Slow")]
public sealed class AppStartupTests
{
    [Fact]
    public async Task Missing_tray_icon_after_runtime_start_releases_hotkey_pipe_and_mutex()
    {
        if (await IsolatedTestHost.RunAsync<AppStartupTests>()) return;
        var paths = new AppPaths(Path.Combine(TestOutputPaths.TempDirectory, "startup-runtime-rollback-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(paths.LanguagesDirectory);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Languages", "en.xaml"), Path.Combine(paths.LanguagesDirectory, "en.xaml"));
        AppDataDirectory.Initialize(paths);
        var log = new PluginLog(paths.LogsDirectory);
        using var probe = new HotkeyWindow(new StaDispatcher("CircleFlow startup cleanup test"), log);
        var registrar = new HotkeyRegistrar(probe, TestUiStrings.English, log);
        var gesture = Enumerable.Range(1, 11).Select(key => $"Ctrl+Alt+Shift+F{key}")
            .First(candidate => registrar.TryApply(candidate).Success);
        Assert.True(registrar.TryApply(null).Success);
        new SettingsStore(paths).Save(new AppSettings { HotkeyGesture = gesture });
        var name = "Local\\CircleFlow.StartupTests." + Guid.NewGuid().ToString("N");
        var result = RunOnSta(paths, name);
        Assert.Equal(1, result.ExitCode);
        Assert.Equal(TestUiStrings.English.StartupFailed, result.Message);
        Assert.True(registrar.TryApply(gesture).Success);
        using var replacement = Shell.SingleInstanceCoordinator.TryAcquire(name);
        Assert.NotNull(replacement);
        replacement.StartListening(() => true, _ => { });
    }

    [Fact]
    public async Task Saved_app_language_is_used_once_settings_are_loaded()
    {
        if (await IsolatedTestHost.RunAsync<AppStartupTests>()) return;
        StartWithSavedRussian(valid: true);
    }

    [Fact]
    public async Task A_broken_translation_falls_back_to_English_and_still_starts()
    {
        if (await IsolatedTestHost.RunAsync<AppStartupTests>()) return;
        StartWithSavedRussian(valid: false);
    }

    private static void StartWithSavedRussian(bool valid)
    {
        var paths = new AppPaths(Path.Combine(TestOutputPaths.TempDirectory, "startup-language-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(paths.LanguagesDirectory);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Languages", "en.xaml"), Path.Combine(paths.LanguagesDirectory, "en.xaml"));
        File.WriteAllText(Path.Combine(paths.LanguagesDirectory, "ru.xaml"), valid
            ? """
              <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                  xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                  xmlns:system="clr-namespace:System;assembly=mscorlib">
                  <system:String x:Key="app_startup_failed">Translated startup failure</system:String>
              </ResourceDictionary>
              """
            : "<ResourceDictionary broken");
        AppDataDirectory.Initialize(paths);
        var log = new PluginLog(paths.LogsDirectory);
        using var probe = new HotkeyWindow(new StaDispatcher("CircleFlow startup language test"), log);
        var registrar = new HotkeyRegistrar(probe, TestUiStrings.English, log);
        var gesture = Enumerable.Range(1, 11).Select(key => $"Ctrl+Alt+Shift+F{key}")
            .First(candidate => registrar.TryApply(candidate).Success);
        Assert.True(registrar.TryApply(null).Success);
        new SettingsStore(paths).Save(new AppSettings { HotkeyGesture = gesture, AppLanguageTag = "ru" });

        // The missing tray icon fails startup only after the runtime is built with the final strings.
        var result = RunOnSta(paths, "Local\\CircleFlow.StartupTests." + Guid.NewGuid().ToString("N"));
        Assert.Equal(1, result.ExitCode);
        Assert.Equal(valid ? "Translated startup failure" : TestUiStrings.English.StartupFailed, result.Message);
        if (!valid)
            Assert.Contains("ru.xaml was ignored", File.ReadAllText(Path.Combine(paths.LogsDirectory, "plugin.log")));
    }

    [Fact]
    public async Task Missing_English_reports_embedded_error_and_never_creates_Data()
    {
        if (await IsolatedTestHost.RunAsync<AppStartupTests>()) return;
        var paths = new AppPaths(Path.Combine(TestOutputPaths.TempDirectory, "missing-language-" + Guid.NewGuid().ToString("N")));
        var result = RunOnSta(paths, "Local\\CircleFlow.StartupTests." + Guid.NewGuid().ToString("N"));
        Assert.Equal(1, result.ExitCode);
        Assert.Equal(TestUiStrings.English.StartupLanguageFailed, result.Message);
        Assert.Equal(TestUiStrings.English.PluginTitle, result.Title);
        Assert.Equal(MessageBoxImage.Error, result.Icon);
        Assert.False(Directory.Exists(paths.RootDirectory));
    }

    [Fact]
    public async Task An_unreachable_instance_reports_failure_without_creating_its_Data()
    {
        if (await IsolatedTestHost.RunAsync<AppStartupTests>()) return;
        var paths = new AppPaths(Path.Combine(TestOutputPaths.TempDirectory, "second-instance-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(paths.LanguagesDirectory);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Languages", "en.xaml"), Path.Combine(paths.LanguagesDirectory, "en.xaml"));
        var instanceName = "Local\\CircleFlow.StartupTests." + Guid.NewGuid().ToString("N");
        using var owner = Shell.SingleInstanceCoordinator.TryAcquire(instanceName);
        Assert.NotNull(owner);
        var result = RunOnSta(paths, instanceName, activationTimeout: TimeSpan.FromMilliseconds(300));
        Assert.Equal(1, result.ExitCode);
        Assert.Equal(TestUiStrings.English.ActivationFailed, result.Message);
        Assert.Equal(MessageBoxImage.Error, result.Icon);
        Assert.False(Directory.Exists(paths.DataDirectory));
    }

    [Fact]
    public async Task A_second_launch_delivers_Open_and_exits_successfully_without_creating_Data()
    {
        if (await IsolatedTestHost.RunAsync<AppStartupTests>()) return;
        var paths = new AppPaths(Path.Combine(TestOutputPaths.TempDirectory, "activated-instance-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(paths.LanguagesDirectory);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Languages", "en.xaml"), Path.Combine(paths.LanguagesDirectory, "en.xaml"));
        var name = "Local\\CircleFlow.StartupTests." + Guid.NewGuid().ToString("N");
        using var owner = Shell.SingleInstanceCoordinator.TryAcquire(name);
        var opens = 0;
        owner!.StartListening(() => { Interlocked.Increment(ref opens); return true; }, exception => Assert.Fail(exception.ToString()));
        var result = RunOnSta(paths, name);
        Assert.Equal(0, result.ExitCode);
        Assert.Null(result.Message);
        Assert.Equal(1, opens);
        Assert.False(Directory.Exists(paths.DataDirectory));
    }

    [Fact]
    public async Task A_sign_in_launch_exits_quietly_without_opening_a_capture_in_the_running_instance()
    {
        if (await IsolatedTestHost.RunAsync<AppStartupTests>()) return;
        var paths = new AppPaths(Path.Combine(TestOutputPaths.TempDirectory, "autostart-instance-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(paths.LanguagesDirectory);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Languages", "en.xaml"), Path.Combine(paths.LanguagesDirectory, "en.xaml"));
        var name = "Local\\CircleFlow.StartupTests." + Guid.NewGuid().ToString("N");
        using var owner = Shell.SingleInstanceCoordinator.TryAcquire(name);
        var opens = 0;
        owner!.StartListening(() => { Interlocked.Increment(ref opens); return true; }, exception => Assert.Fail(exception.ToString()));
        var result = RunOnSta(paths, name, autostart: true);
        Assert.Equal(0, result.ExitCode);
        Assert.Null(result.Message);
        Assert.Equal(0, opens);
        Assert.False(Directory.Exists(paths.DataDirectory));
    }

    [Fact]
    public async Task Recovered_settings_produce_one_startup_notice_and_keep_the_valid_backup()
    {
        if (await IsolatedTestHost.RunAsync<AppStartupTests>()) return;
        var paths = new AppPaths(Path.Combine(TestOutputPaths.TempDirectory, "startup-recovery-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(Path.GetDirectoryName(paths.TrayIconPath)!);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Images", "app.ico"), paths.TrayIconPath);
        Directory.CreateDirectory(paths.LanguagesDirectory);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Languages", "en.xaml"), Path.Combine(paths.LanguagesDirectory, "en.xaml"));
        AppDataDirectory.Initialize(paths);
        File.WriteAllText(paths.SettingsFilePath, "corrupt file");
        var expected = new AppSettings { PaddingPx = 21, ImageTranslationPrivacyConsentAccepted = true };
        using var cancellation = new CancellationTokenSource();
        File.WriteAllText(paths.SettingsBackupFilePath, JsonSerializer.Serialize(expected));
        Exception? failure = null;
        var messages = new List<string>();
        var exitCode = -1;
        var thread = new Thread(() =>
        {
            try
            {
                exitCode = CompositionRoot.Run(paths, (text, _, _) =>
                {
                    messages.Add(text);
                    cancellation.Cancel();
                }, "Local\\CircleFlow.StartupTests." + Guid.NewGuid().ToString("N"), cancellation.Token);
            }
            catch (Exception exception) { failure = exception; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        Assert.Null(failure);
        Assert.Equal(0, exitCode);
        Assert.Equal(TestUiStrings.English.StorageRecovered, Assert.Single(messages));
        Assert.Equal(expected, new SettingsStore(paths).Load().Settings);
        Assert.Equal(expected, JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(paths.SettingsBackupFilePath)));
    }

    [Fact]
    public async Task Unwritable_Data_reports_the_location_and_releases_the_instance_mutex()
    {
        if (await IsolatedTestHost.RunAsync<AppStartupTests>()) return;
        var paths = new AppPaths(Path.Combine(TestOutputPaths.TempDirectory, "blocked-data-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(paths.LanguagesDirectory);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Languages", "en.xaml"), Path.Combine(paths.LanguagesDirectory, "en.xaml"));
        File.WriteAllText(paths.DataDirectory, "keep this file");
        var instanceName = "Local\\CircleFlow.StartupTests." + Guid.NewGuid().ToString("N");
        var result = RunOnSta(paths, instanceName);
        Assert.Equal(1, result.ExitCode);
        Assert.Equal(TestUiStrings.English.StartupDataFailed(paths.DataDirectory), result.Message);
        Assert.Equal("keep this file", File.ReadAllText(paths.DataDirectory));
        using var instance = Shell.SingleInstanceCoordinator.TryAcquire(instanceName);
        Assert.NotNull(instance);
    }

    private static (int ExitCode, string? Message, string? Title, MessageBoxImage Icon) RunOnSta(
        AppPaths paths, string instanceName, TimeSpan? activationTimeout = null, bool autostart = false)
    {
        var exitCode = -1;
        string? message = null, title = null;
        var icon = MessageBoxImage.None;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                exitCode = CompositionRoot.Run(paths, (text, caption, image) =>
                {
                    Assert.Null(message);
                    (message, title, icon) = (text, caption, image);
                }, instanceName, activationTimeout: activationTimeout, autostart: autostart);
            }
            catch (Exception exception) { failure = exception; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        Assert.Null(failure);
        return (exitCode, message, title, icon);
    }
}

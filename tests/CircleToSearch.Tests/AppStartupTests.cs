using System.Windows;
using System.Text.Json;
using CircleToSearch.Settings;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class AppStartupTests
{
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
    public async Task A_second_instance_reports_activation_without_creating_its_Data()
    {
        if (await IsolatedTestHost.RunAsync<AppStartupTests>()) return;
        var paths = new AppPaths(Path.Combine(TestOutputPaths.TempDirectory, "second-instance-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(paths.LanguagesDirectory);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Languages", "en.xaml"), Path.Combine(paths.LanguagesDirectory, "en.xaml"));
        var instanceName = "Local\\CircleFlow.StartupTests." + Guid.NewGuid().ToString("N");
        using var owner = Shell.SingleInstanceCoordinator.TryAcquire(instanceName);
        Assert.NotNull(owner);
        var result = RunOnSta(paths, instanceName);
        Assert.Equal(1, result.ExitCode);
        Assert.Equal(TestUiStrings.English.ActivationAlreadyRunning, result.Message);
        Assert.Equal(MessageBoxImage.Information, result.Icon);
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
                    Application.Current.Dispatcher.BeginInvoke(() => Application.Current.Shutdown());
                }, "Local\\CircleFlow.StartupTests." + Guid.NewGuid().ToString("N"));
            }
            catch (Exception exception) { failure = exception; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
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

    private static (int ExitCode, string? Message, string? Title, MessageBoxImage Icon) RunOnSta(AppPaths paths, string instanceName)
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
                }, instanceName);
            }
            catch (Exception exception) { failure = exception; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        Assert.Null(failure);
        return (exitCode, message, title, icon);
    }
}

using System.Globalization;
using CircleToSearch.Ui;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class UiLanguageTests
{
    [Fact]
    public void Problems_are_logged_once_from_the_first_apply_and_again_only_for_a_newly_loaded_translation()
    {
        var root = Path.Combine(TestOutputPaths.TempDirectory, "ui-language-" + Guid.NewGuid().ToString("N"));
        var languages = Path.Combine(root, "Languages");
        var logs = Path.Combine(root, "Logs");
        Directory.CreateDirectory(languages);
        File.Copy(Path.Combine(new AppPaths().LanguagesDirectory, "en.xaml"), Path.Combine(languages, "en.xaml"));
        File.WriteAllText(Path.Combine(languages, "de.xaml"), "<ResourceDictionary broken");
        File.WriteAllText(Path.Combine(languages, "ru.xaml"), """
            <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                xmlns:system="clr-namespace:System;assembly=mscorlib">
                <system:String x:Key="app_tray_open">Открыть</system:String>
                <system:String x:Key="app_tray_missing">Нет</system:String>
            </ResourceDictionary>
            """);
        string[] Warnings() => File.Exists(Path.Combine(logs, "plugin.log"))
            ? File.ReadAllLines(Path.Combine(logs, "plugin.log")).Where(line => line.Contains("[WARN]")).ToArray()
            : [];

        var language = new UiLanguage(LocalUiStrings.LoadEnglish(languages), new AppLanguageCatalog(languages),
            CultureInfo.GetCultureInfo("ru-RU"), new PluginLog(logs));
        Assert.Equal("Открыть", language.Get("app_tray_open"));
        Directory.CreateDirectory(logs);
        Assert.Empty(Warnings());

        language.Apply("ru");
        Assert.Equal(2, Warnings().Length);
        Assert.Contains(Warnings(), line => line.Contains("de.xaml was ignored", StringComparison.Ordinal));
        Assert.Contains(Warnings(), line => line.Contains("'app_tray_missing'", StringComparison.Ordinal));
        language.Apply("");
        Assert.Equal(2, Warnings().Length);

        language.Apply("en");
        Assert.Equal("Open", language.Get("app_tray_open"));
        language.Apply("ru");
        Assert.Equal("Открыть", language.Get("app_tray_open"));
        Assert.Equal(3, Warnings().Length);
    }
}

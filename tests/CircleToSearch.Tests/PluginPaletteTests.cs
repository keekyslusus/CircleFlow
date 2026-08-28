using CircleToSearch.Ui;
using System.Text.RegularExpressions;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class PluginPaletteTests
{
    [Fact]
    public void Dark_theme_exposes_window_and_selection_colors_from_one_palette()
    {
        var palette = PluginPalette.For(lightTheme: false);

        Assert.Equal("#FF202124", palette.WindowSurface.ToString());
        Assert.Equal("#FFE8EAED", palette.PrimaryText.ToString());
        Assert.Equal("#E6202124", palette.SelectionChip.Surface.ToString());
        Assert.Equal("#FFE8EAED", palette.SelectionChip.KeycapText.ToString());
    }

    [Fact]
    public void Light_theme_exposes_window_and_selection_colors_from_one_palette()
    {
        var palette = PluginPalette.For(lightTheme: true);

        Assert.Equal("#FFF7F9FC", palette.WindowSurface.ToString());
        Assert.Equal("#FF30343A", palette.PrimaryText.ToString());
        Assert.Equal("#F0FCFCFD", palette.SelectionChip.Surface.ToString());
        Assert.Equal("#FF3C4043", palette.SelectionChip.KeycapText.ToString());
    }

    [Fact]
    public void Shared_effect_and_brand_colors_are_centralized()
    {
        Assert.Equal("#59000000", PluginPalette.SelectionDim.ToString());
        Assert.Equal("#24FFFFFF", PluginPalette.SelectionSheen.ToString());
        Assert.Equal(
            new[] { "#FF4285F4", "#FFA142F4", "#FF0B57D0" },
            PluginPalette.GoogleLensLoadingDots.Select(color => color.ToString()));
    }

    [Fact]
    public void Production_ui_has_no_fixed_colors_outside_the_palette()
    {
        var sourceDirectory = Path.Combine(TestOutputPaths.RepoDirectory, "CTS");
        var fixedColor = new Regex(
            @"(?:Color|DrawingColor)\.From(?:Rgb|Argb)\(\s*(?:0x[0-9A-Fa-f]+|\d)|\b(?:Colors|Brushes)\.[A-Za-z]+|#[0-9A-Fa-f]{6,8}",
            RegexOptions.CultureInvariant);
        var violations = Directory
            .EnumerateFiles(sourceDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith("PluginPalette.cs", StringComparison.OrdinalIgnoreCase))
            .SelectMany(path => File.ReadLines(path)
                .Select((line, index) => new { path, line, number = index + 1 }))
            .Where(item => fixedColor.IsMatch(item.line))
            .Select(item => $"{Path.GetRelativePath(TestOutputPaths.RepoDirectory, item.path)}:{item.number}")
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Fixed colors must be declared in PluginPalette.cs: {string.Join(", ", violations)}");
    }
}

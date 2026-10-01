using CircleToSearch.Ui;
using System.Text.RegularExpressions;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class PluginPaletteTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Window_and_selection_text_meets_wcag_contrast_on_its_surface(bool lightTheme)
    {
        var palette = PluginPalette.For(lightTheme);
        var chip = palette.SelectionChip;
        var chipSurface = chip.Surface with { A = 255 };

        Assert.InRange(ContrastRatio(palette.PrimaryText, palette.WindowSurface), 4.5, 21);
        Assert.InRange(ContrastRatio(chip.Label, chipSurface), 4.5, 21);
        Assert.InRange(ContrastRatio(chip.KeycapText, PluginPalette.Composite(chipSurface, chip.KeycapBackground)), 4.5, 21);
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

    private static double ContrastRatio(System.Windows.Media.Color first, System.Windows.Media.Color second)
    {
        var a = RelativeLuminance(first);
        var b = RelativeLuminance(second);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    private static double RelativeLuminance(System.Windows.Media.Color color)
    {
        static double Linear(byte channel)
        {
            var value = channel / 255d;
            return value <= 0.03928 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
    }
}

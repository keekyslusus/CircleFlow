using System.Text.RegularExpressions;
using System.Windows.Media;
using CircleToSearch.Ui;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class SystemAccentColorTests
{
    [Fact]
    public void Registry_dword_is_decoded_as_aabbggrr()
    {
        // 0xFF4A16C8 stores RGB(200, 22, 74).
        var color = SystemAccentColor.FromDword(unchecked((int)0xFF4A16C8));

        Assert.Equal(Color.FromRgb(0xC8, 0x16, 0x4A), color);
    }

    [Fact]
    public void Read_methods_return_opaque_colors()
    {
        Assert.Equal(255, SystemAccentColor.ReadRaw().A);
        Assert.Equal(255, SystemAccentColor.Read().A);
    }

    [Theory]
    [InlineData(0x00, 0x00, 0xFF, 0xBE, 0xC2, 0xFF)]
    [InlineData(0xFF, 0x00, 0x00, 0xFF, 0xB4, 0xA8)]
    [InlineData(0x00, 0xFF, 0x00, 0xA0, 0xD4, 0x90)]
    [InlineData(0x8E, 0x8C, 0xD8, 0xC3, 0xC0, 0xFF)]
    public void Pastel_conversion_matches_material_hct_tonal_spot(
        byte red,
        byte green,
        byte blue,
        byte expectedRed,
        byte expectedGreen,
        byte expectedBlue)
    {
        var pastel = SystemAccentColor.ToPastel(Color.FromRgb(red, green, blue));

        Assert.Equal(Color.FromRgb(expectedRed, expectedGreen, expectedBlue), pastel);
        Assert.InRange(HctColorConverter.ToneOf(pastel), 79.8, 80.2);
    }

    [Fact]
    public void Exact_black_uses_chromium_seed_nudge()
    {
        var pastel = SystemAccentColor.ToPastel(Colors.Black);

        Assert.Equal(Color.FromRgb(0x74, 0xD5, 0xE4), pastel);
    }

    [Fact]
    public void Pastel_conversion_preserves_alpha()
    {
        var pastel = SystemAccentColor.ToPastel(Color.FromArgb(0x70, 0x42, 0x85, 0xF4));

        Assert.Equal(0x70, pastel.A);
    }

    // The raw accent is too saturated for overlay UI; every other route to it would skip the pastel conversion.
    [Fact]
    public void Production_ui_reads_the_accent_only_through_the_pastel_conversion()
    {
        var sourceDirectory = Path.Combine(TestOutputPaths.RepoDirectory, "CTS");
        var rawAccent = new Regex(
            @"SystemColors\.Accent|WindowGlass(?:Color|Brush)|UISettings|Explorer\\Accent|\\DWM\b|""AccentColor|ReadRaw\(",
            RegexOptions.CultureInvariant);
        var violations = Directory
            .EnumerateFiles(sourceDirectory, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                           path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.EndsWith("SystemAccentColor.cs", StringComparison.OrdinalIgnoreCase))
            .SelectMany(path => File.ReadLines(path)
                .Select((line, index) => new { path, line, number = index + 1 }))
            .Where(item => rawAccent.IsMatch(item.line))
            .Select(item => $"{Path.GetRelativePath(TestOutputPaths.RepoDirectory, item.path)}:{item.number}")
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Read the accent with SystemAccentColor.Read(): {string.Join(", ", violations)}");
    }
}

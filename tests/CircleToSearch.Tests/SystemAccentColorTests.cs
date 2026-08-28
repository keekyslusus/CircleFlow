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
    public void Read_returns_an_opaque_color()
    {
        Assert.Equal(255, SystemAccentColor.Read().A);
    }
}

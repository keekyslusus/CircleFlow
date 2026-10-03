namespace CircleToSearch.Ui;

using System.Windows;

// Pills and circles stay off this scale: their radius is half of their own height.
internal static class PluginShapes
{
    internal const double Small = 4;
    internal const double Medium = 8;
    internal const double Large = 16;
    internal const double ExtraLarge = 24;

    public static CornerRadius SmallCorners { get; } = new(Small);
    public static CornerRadius MediumCorners { get; } = new(Medium);
    public static CornerRadius LargeCorners { get; } = new(Large);
    public static CornerRadius ExtraLargeCorners { get; } = new(ExtraLarge);
}

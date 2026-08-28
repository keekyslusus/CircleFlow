namespace CircleToSearch.Ui;

using System.Windows;
using System.Windows.Media;

public static class UiPalette
{
    public static readonly Color SelectionGradientStart = Color.FromRgb(0x42, 0x85, 0xF4);
    public static readonly Color SelectionGradientEnd = Color.FromRgb(0xA1, 0x42, 0xF4);

    private static readonly Lazy<LinearGradientBrush> SelectionGradientLazy = new(CreateSelectionGradient);

    public static LinearGradientBrush SelectionGradient => SelectionGradientLazy.Value;

    private static LinearGradientBrush CreateSelectionGradient()
    {
        var brush = new LinearGradientBrush
        {
            // Bounding-box mapping lets one frozen brush work for lasso strokes of any shape.
            MappingMode = BrushMappingMode.RelativeToBoundingBox,
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
        };
        brush.GradientStops.Add(new GradientStop(SelectionGradientStart, 0.0));
        brush.GradientStops.Add(new GradientStop(SelectionGradientEnd, 1.0));
        brush.Freeze();
        return brush;
    }
}

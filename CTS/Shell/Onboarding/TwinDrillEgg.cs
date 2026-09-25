using System.Windows;
using System.Windows.Media;
using CircleToSearch.Shell.SettingsPreview;
using CircleToSearch.Ui;

namespace CircleToSearch.Shell.Onboarding;

internal static class TwinDrillEgg
{
    public static DrawingImage Create()
    {
        var colors = PluginPalette.TwinDrillEgg;
        var drawing = new DrawingGroup();
        void Add(string data, Color? fill, Color? stroke = null, double thickness = 1)
        {
            var pen = stroke is { } strokeColor
                ? new Pen(SettingsWindowTheme.Frozen(strokeColor), thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }
                : null;
            drawing.Children.Add(new GeometryDrawing(fill is { } fillColor ? SettingsWindowTheme.Frozen(fillColor) : null, pen, Geometry.Parse(data)));
        }

        Add("M82 0H8C3.58 0 0 3.58 0 8V82C0 86.42 3.58 90 8 90H82C86.42 90 90 86.42 90 82V8C90 3.58 86.42 0 82 0Z", colors.Backdrop);
        foreach (var curl in new[]
        {
            "M18 39C24.08 39 29 35.87 29 32C29 28.13 24.08 25 18 25C11.92 25 7 28.13 7 32C7 35.87 11.92 39 18 39Z",
            "M18.7 48C24.22 48 28.7 45.31 28.7 42C28.7 38.69 24.22 36 18.7 36C13.18 36 8.7 38.69 8.7 42C8.7 45.31 13.18 48 18.7 48Z",
            "M19.4 56C23.82 56 27.4 53.76 27.4 51C27.4 48.24 23.82 46 19.4 46C14.98 46 11.4 48.24 11.4 51C11.4 53.76 14.98 56 19.4 56Z",
            "M20.1 63C22.86 63 25.1 61.21 25.1 59C25.1 56.79 22.86 55 20.1 55C17.34 55 15.1 56.79 15.1 59C15.1 61.21 17.34 63 20.1 63Z",
            "M72 39C78.08 39 83 35.87 83 32C83 28.13 78.08 25 72 25C65.92 25 61 28.13 61 32C61 35.87 65.92 39 72 39Z",
            "M71.3 48C76.82 48 81.3 45.31 81.3 42C81.3 38.69 76.82 36 71.3 36C65.78 36 61.3 38.69 61.3 42C61.3 45.31 65.78 48 71.3 48Z",
            "M70.6 56C75.02 56 78.6 53.76 78.6 51C78.6 48.24 75.02 46 70.6 46C66.18 46 62.6 48.24 62.6 51C62.6 53.76 66.18 56 70.6 56Z",
            "M69.9 63C72.66 63 74.9 61.21 74.9 59C74.9 56.79 72.66 55 69.9 55C67.14 55 64.9 56.79 64.9 59C64.9 61.21 67.14 63 69.9 63Z",
        })
            Add(curl, colors.Hair, colors.HairOutline);
        Add("M29 90C30 76 37 69 45 69C53 69 60 76 61 90H29Z", colors.Clothes);
        Add("M42 70H48L45 78L42 70Z", colors.Hair);
        Add("M45 63C54.94 63 63 54.94 63 45C63 35.06 54.94 27 45 27C35.06 27 27 35.06 27 45C27 54.94 35.06 63 45 63Z", colors.Skin);
        Add("M26 47C24 30 33 21 45 21C57 21 66 30 64 47C61 41 58 38 55 37L52 44L48 36L43 44L39 36L36 43C32 41 29 43 26 47Z", colors.Bangs);
        Add("M45 21C42 13 46 8 53 10", null, colors.Bangs, 2.5);
        Add("M60 27C57 20 59 13 64 10C66 16 65 22 60 27Z", colors.Ribbon, colors.RibbonOutline, 0.8);
        Add("M60 27C64 21 70 19 75 21C72 25 66 28 60 27Z", colors.Ribbon, colors.RibbonOutline, 0.8);
        Add("M60.5 28.3C61.49 28.3 62.3 27.49 62.3 26.5C62.3 25.51 61.49 24.7 60.5 24.7C59.51 24.7 58.7 25.51 58.7 26.5C58.7 27.49 59.51 28.3 60.5 28.3Z",
            colors.Ribbon, colors.RibbonOutline, 0.8);
        drawing.ClipGeometry = new RectangleGeometry(new Rect(0, 0, 90, 90));
        var image = new DrawingImage(drawing);
        image.Freeze();
        return image;
    }
}

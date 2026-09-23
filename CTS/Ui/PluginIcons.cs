using System.Windows;
using System.Windows.Media;

namespace CircleToSearch.Ui;

internal static class PluginIcons
{
    public static Geometry CloseFilled { get; } = Frozen(
        "M19 6.41 17.59 5 12 10.59 6.41 5 5 6.41 10.59 12 5 17.59 6.41 19 12 13.41 17.59 19 19 17.59 13.41 12Z");

    public static Geometry CopyFilled { get; } = Frozen(
        "M16 1H4c-1.1 0-2 .9-2 2v14h2V3h12V1Zm3 4H8c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h10c1.1 0 2-.9 2-2V7c0-1.1-.9-2-2-2Zm0 16H8V7h10v14Z");

    public static Geometry CheckFilled { get; } = Frozen(
        "M9 16.17 4.83 12l-1.42 1.41L9 19 21 7l-1.41-1.41Z");

    public static Geometry NoSoundFilled { get; } = Frozen(
        "M611-323l-43-43 114-113-114-113 43-43 113 114 113-114 43 43-114 113 114 113-43 43-113-114-113 114ZM120-360v-240h160l200-200v640L280-360H120Zm300-288L307-540H180v120h127l113 109v-337ZM311-481Z");

    public static Geometry MusicOffFilled { get; } = Frozen(
        "M806-56 57-805l43-43L849-99l-43 43ZM546-487l-60-60v-293h234v135H546v218ZM396-120q-63 0-106.5-43.5T246-270q0-63 43.5-106.5T396-420q28 0 50.5 8t39.5 22v-72l60 60v132q0 63-43.5 106.5T396-120Z");

    public static Geometry TranslateFilled { get; } = Frozen(
        "M12.87 15.07l-2.54-2.51.03-.03c1.74-1.94 2.98-4.17 3.71-6.53H17V4h-7V2H8v2H1v1.99h11.17C11.5 7.92 10.44 9.75 9 11.35 8.07 10.32 7.3 9.19 6.69 8h-2c.73 1.63 1.73 3.17 2.98 4.56l-5.09 5.02L4 19l5-5 3.11 3.11.76-2.04zM18.5 10h-2L12 22h2l1.12-3h4.75L21 22h2l-4.5-12zm-2.62 7l1.62-4.33L19.12 17h-3.24z");

    public static Geometry ShowOriginalFilled { get; } = Frozen(
        "M259-200v-60h310q70 0 120.5-46.5T740-422q0-69-50.5-115.5T569-584H274l114 114-42 42-186-186 186-186 42 42-114 114h294q95 0 163.5 64T800-422q0 94-68.5 158T568-200H259Z");

    public static Geometry ChevronDownFilled { get; } = Frozen("M7 10l5 5 5-5Z");

    public static Geometry SelectAreaFilled { get; } = Frozen(
        "M439-120v-401h401v60H542l298 298-43 43-298-298v298h-60Z" +
        "m-154 0v-60h60v60h-60Z" +
        "M180-780h-60q0-24.75 17.63-42.38Q155.25-840 180-840v60Z" +
        "m105 0v-60h60v60h-60Z" +
        "m165 0v-60h60v60h-60Z" +
        "m165 0v-60h60v60h-60Z" +
        "m165 0v-60h60v60h-60Z" +
        "m165 0v-60q24.75 0 42.38 17.62Q840-804.75 840-780h-60Z" +
        "M180-180v60q-24.75 0-42.37-17.63Q120-155.25 120-180h60Z" +
        "m-60-105v-60h60v60h-60Z" +
        "m0-165v-60h60v60h-60Z" +
        "m0-165v-60h60v60h-60Z" +
        "m660 0v-60h60v60h-60Z");

    public static Geometry MusicFilled { get; } = Frozen("M12 3v10.55A4 4 0 1 0 14 17V7h4V3h-6Z");

    public static Geometry ChevronDownOutlined { get; } = Frozen("M0 0 L4 4 L8 0");

    public static Geometry CloseOutlined { get; } = Frozen("M 0,0 L 10,10 M 10,0 L 0,10");

    public static Geometry FolderOutlined { get; } = Group(
        Geometry.Parse("M3 7V5a2 2 0 0 1 2-2h5l2 3h7a2 2 0 0 1 2 2v11a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V7Z"));

    public static Geometry LogsOutlined { get; } = Group(
        Geometry.Parse("M14 3H6a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V9Z"),
        Geometry.Parse("M14 3v6h6M8 13h8m-8 4h8"));

    public static Geometry RestoreOutlined { get; } = Group(
        Geometry.Parse("M3 10a9 9 0 1 1 1 8M3 4v6h6"));

    public static Geometry MaintenanceOutlined { get; } = Group(
        Geometry.Parse("M14.7 6.3a5.5 5.5 0 0 0-7-3.1l3.4 3.4-4.5 4.5-3.4-3.4a5.5 5.5 0 0 0 7 7l5.9 5.9a3.2 3.2 0 0 0 4.5-4.5l-5.9-5.9a5.5 5.5 0 0 0 0-3.9Z"));

    public static Geometry CircleSearchOutlined { get; } = Group(
        Geometry.Parse("M9 3H6a3 3 0 0 0-3 3v3m12-6h3a3 3 0 0 1 3 3v3M3 15v3a3 3 0 0 0 3 3h3m6 0h3a3 3 0 0 0 3-3v-3"),
        new EllipseGeometry(new Point(12, 12), 4, 4));

    public static Geometry HomeOutlined { get; } = Group(
        Geometry.Parse("m3 10 9-7 9 7v10H3Z"),
        Geometry.Parse("M9 20v-7h6v7"));

    public static Geometry KeyboardOutlined { get; } = Group(
        new RectangleGeometry(new Rect(2, 5, 20, 14), 3, 3),
        Geometry.Parse("M6 9h.1M10 9h.1M14 9h.1M18 9h.1M6 12h.1M10 12h.1M14 12h.1M18 12h.1M7 15h10"));

    public static Geometry SearchOutlined { get; } = Group(
        new EllipseGeometry(new Point(10.5, 10.5), 6.5, 6.5),
        Geometry.Parse("m16 16 5 5"));

    public static Geometry TranslateOutlined { get; } = Group(
        Geometry.Parse("M3 5h12M9 3v2m4 0c0 7-4 10-9 12m1-9c1 4 4 7 8 8m1 5 4-11 4 11m-6-4h4"));

    public static Geometry MusicOutlined { get; } = Group(
        Geometry.Parse("M10 17V5l10-2v12M10 8l10-2"),
        new EllipseGeometry(new Point(7, 18), 3, 2),
        new EllipseGeometry(new Point(17, 16), 3, 2));

    public static Geometry InfoOutlined { get; } = Group(
        new EllipseGeometry(new Point(12, 12), 9, 9),
        Geometry.Parse("M12 11v6m0-10h.01"));

    public static Geometry SunOutlined { get; } = Group(
        new EllipseGeometry(new Point(12, 12), 4, 4),
        Geometry.Parse("M12 2v2m0 16v2M2 12h2m16 0h2M5 5l1 1m12 12 1 1M5 19l1-1M18 6l1-1"));

    public static Geometry MoonOutlined { get; } = Group(
        Geometry.Parse("M20 14A8 8 0 0 1 10 4 8 8 0 1 0 20 14Z"));

    public static Geometry EditOutlined { get; } = Group(
        Geometry.Parse("m14 5 5 5M4 20l5-1L21 7a2 2 0 0 0-5-5L4 14Z"));

    public static Geometry ChevronRightOutlined { get; } = Group(
        Geometry.Parse("m9 5 7 7-7 7"));

    public static Geometry ArrowUpOutlined { get; } = Group(
        Geometry.Parse("M12 19V5m-6 6 6-6 6 6"));

    public static Geometry CheckOutlined { get; } = Group(
        Geometry.Parse("m5 12 4 4L19 6"));

    public static Geometry PowerOutlined { get; } = Group(
        Geometry.Parse("M12 2v10m-6-7a9 9 0 1 0 12 0"));

    public static Geometry WindowOutlined { get; } = Group(
        new RectangleGeometry(new Rect(3, 4, 18, 16), 2, 2),
        Geometry.Parse("M3 9h18m-14-2h.01M10 7h.01"));

    public static Geometry ImageOutlined { get; } = Group(
        new RectangleGeometry(new Rect(3, 3, 18, 18), 3, 3),
        new EllipseGeometry(new Point(8, 8), 1.5, 1.5),
        Geometry.Parse("m3 17 6-5 4 3 4-5 4 4"));

    public static Geometry ShieldOutlined { get; } = Group(
        Geometry.Parse("M12 2 3 6v6c0 5 9 10 9 10s9-5 9-10V6Z"),
        Geometry.Parse("m8 12 3 3 5-6"));

    public static Geometry VolumeOutlined { get; } = Group(
        Geometry.Parse("M4 9v6h4l5 4V5L8 9Zm12-1c3 2 3 6 0 8m3-11c5 4 5 10 0 14"));

    public static Geometry LinkOutlined { get; } = Group(
        Geometry.Parse("M14 3h7v7m0-7L10 14m0-9H5a2 2 0 0 0-2 2v12a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-5"));

    public static Geometry CoffeeOutlined { get; } = Group(
        Geometry.Parse("M4 8h13v7a4 4 0 0 1-4 4H8a4 4 0 0 1-4-4ZM17 9h2a3 3 0 0 1 0 6h-2M3 22h16M7 3v2m4-3v3m4-2v2"));

    internal static Geometry AniListBlue { get; } = Frozen("M111.322,111.157 L111.322,41.029 C111.322,37.010 109.105,34.792 105.086,34.792 L91.365,34.792 C87.346,34.792 85.128,37.010 85.128,41.029 C85.128,41.029 85.128,56.337 85.128,74.333 C85.128,75.271 94.165,79.626 94.401,80.547 C101.286,107.449 95.897,128.980 89.370,129.985 C100.042,130.513 101.216,135.644 93.267,132.138 C94.483,117.784 99.228,117.812 112.869,131.610 C112.986,131.729 115.666,137.351 115.833,137.351 C131.170,137.351 148.050,137.351 148.050,137.351 C152.069,137.351 154.286,135.134 154.286,131.115 L154.286,117.394 C154.286,113.375 152.069,111.157 148.050,111.157 L111.322,111.157 Z");
    internal static Geometry AniListLetter { get; } = Frozen("M54.365,34.792 L18.331,137.351 L46.327,137.351 L52.425,119.611 L82.915,119.611 L88.875,137.351 L116.732,137.351 L80.836,34.792 L54.365,34.792 ZM58.800,96.882 L67.531,68.470 L77.094,96.882 L58.800,96.882 Z");

    private static Geometry Frozen(string data)
    {
        var geometry = Geometry.Parse(data);
        geometry.Freeze();
        return geometry;
    }

    private static Geometry Group(params Geometry[] children)
    {
        var geometry = new GeometryGroup();
        foreach (var child in children) geometry.Children.Add(child);
        geometry.Freeze();
        return geometry;
    }
}

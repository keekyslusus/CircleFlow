namespace CircleToSearch.AccentColorTool;

using System.Windows;
using System.Windows.Media;
using CircleToSearch.Ui;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        RefreshColors();
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => RefreshColors();

    private void RefreshColors()
    {
        var systemAccent = SystemAccentColor.ReadRaw();
        var pastelAccent = SystemAccentColor.ToPastel(systemAccent);

        SystemAccentSwatch.Background = Frozen(systemAccent);
        PastelAccentSwatch.Background = Frozen(pastelAccent);
        SystemAccentValue.Text = ToHex(systemAccent);
        PastelAccentValue.Text = ToHex(pastelAccent);
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}

namespace CircleToSearch.Capture;

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using CircleToSearch.Ui;

internal static class SelectionHintVisualPresenter
{
    private const double KeyGapDips = 4;
    private const byte PressedButtonTintAlpha = 0x8C;
    private static readonly TimeSpan FadeDuration = TimeSpan.FromMilliseconds(160);

    internal static void Show(SelectionHintVisual visual, SelectionHint hint, UiStrings strings, bool animate)
    {
        visual.Keys.Children.Clear();
        switch (hint)
        {
            case SelectionHint.RightDragActions:
                AddKey(visual, MouseKeycap(visual, PluginIcons.MouseRightButtonOutlined, strings.RightMouseButton));
                visual.Action.Text = strings.SelectionHintActions;
                break;
            case SelectionHint.LeftDragSearch:
                AddKey(visual, MouseKeycap(visual, PluginIcons.MouseLeftButtonOutlined, strings.LeftMouseButton));
                visual.Action.Text = strings.SelectionHintSearch;
                break;
            case SelectionHint.AltLeftDragOverText:
                AddKey(visual, TextKeycap(visual, strings.AltKeyName));
                AddKey(visual, MouseKeycap(visual, PluginIcons.MouseLeftButtonOutlined, strings.LeftMouseButton));
                visual.Action.Text = strings.SelectionHintSearchOverText;
                break;
            case SelectionHint.MiddleDragPan:
                AddKey(visual, MouseKeycap(visual, PluginIcons.MouseWheelButtonOutlined, strings.MiddleMouseButton,
                    PluginIcons.MouseWithWheelOutlined));
                visual.Action.Text = strings.SelectionHintPan;
                break;
            default:
                AddKey(visual, TextKeycap(visual, strings.CancelKeyName));
                visual.Action.Text = strings.CancelAction;
                break;
        }
        if (!animate)
        {
            visual.Content.BeginAnimation(UIElement.OpacityProperty, null);
            return;
        }
        visual.Content.BeginAnimation(UIElement.OpacityProperty, OverlayVisualResources.Animate(0, 1, FadeDuration));
    }

    private static void AddKey(SelectionHintVisual visual, Border keycap)
    {
        if (visual.Keys.Children.Count > 0) keycap.Margin = new Thickness(KeyGapDips, 0, 0, 0);
        visual.Keys.Children.Add(keycap);
    }

    private static Border TextKeycap(SelectionHintVisual visual, string text) => Keycap(
        visual,
        new Thickness(7, 6, 7, 6),
        new TextBlock
        {
            Text = text,
            FontFamily = PluginTypography.Font,
            FontSize = PluginTypography.Caption,
            FontWeight = FontWeights.SemiBold,
            Foreground = visual.KeycapText,
        });

    private static Border MouseKeycap(SelectionHintVisual visual, Geometry button, string name, Geometry? body = null)
    {
        var accent = SystemAccentColor.Read();
        var pressed = Stroke(button, OverlayVisualResources.Frozen(accent));
        // A stroke alone is too thin at keycap size to tell the left button from the right one.
        pressed.Fill = OverlayVisualResources.Frozen(Color.FromArgb(PressedButtonTintAlpha, accent.R, accent.G, accent.B));
        var canvas = new Canvas { Width = 24, Height = 24 };
        canvas.Children.Add(pressed);
        canvas.Children.Add(Stroke(body ?? PluginIcons.MouseOutlined, visual.KeycapText));
        var keycap = Keycap(
            visual,
            new Thickness(4, 5, 4, 5),
            new Viewbox { Width = 17, Height = 17, Child = canvas });
        AutomationProperties.SetName(keycap, name);
        return keycap;
    }

    private static Path Stroke(Geometry geometry, Brush brush) => new()
    {
        Data = geometry,
        Stroke = brush,
        StrokeThickness = 1.65,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
        StrokeLineJoin = PenLineJoin.Round,
    };

    private static Border Keycap(SelectionHintVisual visual, Thickness padding, UIElement child) => new()
    {
        Background = visual.KeycapBackground,
        BorderBrush = visual.KeycapBorder,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8),
        Padding = padding,
        VerticalAlignment = VerticalAlignment.Center,
        Child = child,
    };
}

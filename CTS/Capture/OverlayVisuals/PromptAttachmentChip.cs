namespace CircleToSearch.Capture;

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using CircleToSearch.Ui;

// Toggles whether the question takes the screen along: "+ Screen" adds it, "Whole screen x" leaves it out again.
public sealed class PromptAttachmentChip
{
    private const double ChipHeight = 24;
    // Not the 20px of an icon: the + and x are glyphs inside a 24px chip, sized like the close crosses of
    // notifications and the browser window.
    private const double GlyphSize = 10;
    private readonly FloatingToolbarPalette _palette;
    private readonly UiStrings _strings;
    private readonly TextBlock _label;
    private readonly Path _add;
    private readonly Path _remove;

    internal PromptAttachmentChip(FloatingToolbarPalette palette, UiStrings strings)
    {
        _palette = palette;
        _strings = strings;
        _add = Glyph(PluginIcons.AddOutlined, new Thickness(0, 0, 5, 0));
        _remove = Glyph(PluginIcons.CloseOutlined, new Thickness(6, 0, 0, 0));
        _label = new TextBlock
        {
            FontFamily = PluginTypography.Font,
            FontSize = PluginTypography.Caption,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(_add);
        row.Children.Add(_label);
        row.Children.Add(_remove);
        Root = new Button
        {
            Content = row,
            Height = ChipHeight,
            Padding = new Thickness(9, 0, 9, 0),
            Margin = new Thickness(0, 0, 4, 0),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
            Visibility = Visibility.Collapsed,
        };
        OverlayVisualResources.ApplyButtonTemplate(Root, ChipHeight / 2, palette.ButtonHover, palette.Text);
    }

    internal Button Root { get; }
    internal bool IsShown => Root.Visibility == Visibility.Visible;
    internal bool IsAttached { get; private set; }
    internal string Label => _label.Text;

    internal void Show(bool attached)
    {
        IsAttached = attached;
        _label.Text = attached ? _strings.AskWholeScreen : _strings.AskAddScreen;
        _add.Visibility = attached ? Visibility.Collapsed : Visibility.Visible;
        _remove.Visibility = attached ? Visibility.Visible : Visibility.Collapsed;
        var text = OverlayVisualResources.Frozen(attached ? _palette.TagText : _palette.Text);
        Root.Foreground = text;
        _add.Stroke = text;
        _remove.Stroke = text;
        Root.Background = OverlayVisualResources.Frozen(attached ? _palette.Tag : PluginPalette.Transparent);
        Root.BorderBrush = OverlayVisualResources.Frozen(attached ? _palette.Tag : _palette.Border);
        var hint = attached ? _strings.AskRemoveScreen : _strings.AskAddScreenHint;
        Root.ToolTip = hint;
        AutomationProperties.SetName(Root, hint);
        Root.Visibility = Visibility.Visible;
    }

    internal void Hide()
    {
        IsAttached = false;
        Root.Visibility = Visibility.Collapsed;
    }

    private static Path Glyph(Geometry geometry, Thickness margin) => new()
    {
        Data = geometry,
        Width = GlyphSize,
        Height = GlyphSize,
        Margin = margin,
        Stretch = Stretch.Uniform,
        StrokeThickness = 1.65,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
        VerticalAlignment = VerticalAlignment.Center,
        IsHitTestVisible = false,
    };
}

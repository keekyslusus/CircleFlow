namespace CircleToSearch.Capture;

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using CircleToSearch.Ui;

public sealed record FloatingToolbarPrompt(Grid Root, TextBox Input, Button SendButton);

public sealed class FloatingToolbar
{
    private const double ActionPadding = 12;
    private const double CompactActionPadding = 4;
    // A round icon edge reads as farther from the pill edge than a text stem at the same distance.
    private const double IconSidePaddingReduction = 2;
    private const double ActionHeight = 32;
    private const double ActionRadius = 16;
    private const double SurfacePadding = 4;
    private const double SurfaceBorder = 1;
    private const double PromptWidth = 380;
    private const double DisabledSendOpacity = 0.38;
    private readonly WrapPanel _actions;
    // Only actions with an icon get a separate label, so a label change keeps the icon.
    private readonly Dictionary<Button, TextBlock> _iconLabels = [];
    private readonly List<FrameworkElement> _dividers = [];
    private FloatingToolbarPrompt? _prompt;
    private readonly FloatingToolbarPalette _palette;
    private readonly Func<bool> _animationsEnabled;
    private CardTransitions.ExitHandle? _exit;
    private readonly Thickness _safeInsets;
    private readonly bool _preferBelow;
    private Rect _anchor;
    private Size _viewport;

    internal FloatingToolbar(
        FloatingToolbarPalette palette,
        Func<bool>? animationsEnabled = null,
        Thickness safeInsets = default,
        bool preferBelow = false)
    {
        _palette = palette;
        _safeInsets = safeInsets;
        _preferBelow = preferBelow;
        _animationsEnabled = animationsEnabled ?? OverlayVisualResources.AnimationsEnabled;
        _actions = new WrapPanel { Orientation = Orientation.Horizontal };
        Surface = new Border
        {
            Child = _actions,
            Background = OverlayVisualResources.Frozen(palette.Surface),
            BorderBrush = OverlayVisualResources.Frozen(palette.Border),
            BorderThickness = new Thickness(SurfaceBorder),
            CornerRadius = new CornerRadius(ActionHeight / 2 + SurfacePadding + SurfaceBorder),
            MinHeight = ActionHeight + 2 * (SurfacePadding + SurfaceBorder),
            Padding = new Thickness(SurfacePadding),
            Effect = OverlayVisualResources.DockShadow(4, palette.ShadowOpacity),
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
            IsEnabled = false,
        };
        Surface.SizeChanged += (_, _) => Surface.CornerRadius = new CornerRadius(Surface.ActualHeight / 2);
        Surface.Unloaded += (_, _) => Hide(animate: false);
        Layer = new Canvas { Background = null, IsHitTestVisible = true };
        Layer.Children.Add(Surface);
    }

    internal Canvas Layer { get; }
    internal Border Surface { get; }
    internal bool IsOpen { get; private set; }
    internal bool IsPromptOpen => _prompt is not null && ReferenceEquals(Surface.Child, _prompt.Root);

    internal FloatingToolbarPrompt AddPrompt(string placeholder, string sendLabel)
    {
        if (_prompt is not null) throw new InvalidOperationException("The toolbar already has a prompt.");
        var text = OverlayVisualResources.Frozen(_palette.Text);
        var input = new TextBox
        {
            Foreground = text,
            CaretBrush = text,
            Background = OverlayVisualResources.Frozen(PluginPalette.Transparent),
            BorderThickness = new Thickness(),
            Padding = new Thickness(8, 0, 8, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            FontFamily = OverlayVisualResources.Font,
            FontSize = 14,
            MinHeight = ActionHeight,
            MaxLength = 1000,
            FocusVisualStyle = null,
        };
        AutomationProperties.SetName(input, placeholder);
        var hint = new TextBlock
        {
            Text = placeholder,
            Foreground = text,
            Opacity = 0.6,
            Margin = new Thickness(12, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center,
            FontFamily = OverlayVisualResources.Font,
            FontSize = 14,
            TextTrimming = TextTrimming.CharacterEllipsis,
            IsHitTestVisible = false,
        };
        var send = new Button
        {
            Content = OutlinedIcon(PluginIcons.ArrowUpOutlined),
            Width = ActionHeight,
            Height = ActionHeight,
            Margin = new Thickness(4, 0, 0, 0),
            Foreground = text,
            Background = OverlayVisualResources.Frozen(_palette.Surface),
            BorderThickness = new Thickness(),
            Cursor = Cursors.Hand,
            ToolTip = sendLabel,
            IsEnabled = false,
            Opacity = DisabledSendOpacity,
        };
        OverlayVisualResources.ApplyButtonTemplate(send, ActionRadius, _palette.ButtonHover, _palette.Text);
        AutomationProperties.SetName(send, sendLabel);
        input.TextChanged += (_, _) =>
        {
            var hasText = !string.IsNullOrWhiteSpace(input.Text);
            hint.Visibility = input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            send.IsEnabled = hasText;
            send.Opacity = hasText ? 1 : DisabledSendOpacity;
        };

        var root = new Grid { VerticalAlignment = VerticalAlignment.Center };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.Children.Add(input);
        root.Children.Add(hint);
        Grid.SetColumn(send, 1);
        root.Children.Add(send);
        _prompt = new FloatingToolbarPrompt(root, input, send);
        return _prompt;
    }

    internal void SetPromptOpen(bool open)
    {
        if (_prompt is null) throw new InvalidOperationException("The toolbar has no prompt.");
        if (open == IsPromptOpen) return;
        Surface.Child = open ? _prompt.Root : _actions;
        if (IsOpen) UpdatePlacement();
        if (!open) return;
        Surface.UpdateLayout();
        _prompt.Input.Focus();
        _prompt.Input.CaretIndex = _prompt.Input.Text.Length;
    }

    internal Button AddAction(string label, Geometry icon) => AddAction(label, OutlinedIcon(icon));

    internal Button AddAction(string label, FrameworkElement? icon = null)
    {
        var button = new Button
        {
            Content = label,
            Foreground = OverlayVisualResources.Frozen(_palette.Text),
            Background = OverlayVisualResources.Frozen(_palette.Surface),
            BorderThickness = new Thickness(),
            MinHeight = ActionHeight,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
            FontFamily = OverlayVisualResources.Font,
            FontSize = 13,
            FontWeight = FontWeights.Medium,
        };
        OverlayVisualResources.ApplyButtonTemplate(button, ActionRadius, _palette.ButtonHover, _palette.Text);
        AutomationProperties.SetName(button, label);
        _actions.Children.Add(button);
        SetActionContent(button, label, icon);
        return button;
    }

    // Separates the search with the selected provider from the actions on the selection itself.
    internal void AddDivider()
    {
        var divider = new Border
        {
            Width = 1,
            Height = 18,
            Margin = new Thickness(2, 0, 2, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Background = OverlayVisualResources.Frozen(_palette.Divider),
            IsHitTestVisible = false,
        };
        _dividers.Add(divider);
        _actions.Children.Add(divider);
    }

    internal void SetActionLabel(Button button, string label)
    {
        if (!_actions.Children.Contains(button))
            throw new ArgumentException("The action does not belong to this toolbar.", nameof(button));
        if (_iconLabels.TryGetValue(button, out var text)) text.Text = label;
        else button.Content = label;
        AutomationProperties.SetName(button, label);
        if (IsOpen) UpdatePlacement();
    }

    internal void SetActionContent(Button button, string label, Geometry icon) =>
        SetActionContent(button, label, OutlinedIcon(icon));

    internal void SetActionContent(Button button, string label, FrameworkElement? icon = null)
    {
        if (!_actions.Children.Contains(button))
            throw new ArgumentException("The action does not belong to this toolbar.", nameof(button));
        if (icon is null)
        {
            button.Content = label;
            _iconLabels.Remove(button);
        }
        else
        {
            var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
            _iconLabels[button] = text;
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false,
            };
            row.Children.Add(new ContentControl
            {
                Content = icon,
                // Label glyphs sit about 1px below the center of their line box, so a centered icon looks raised.
                Margin = new Thickness(0, 1, 6, -1),
                VerticalAlignment = VerticalAlignment.Center,
                Focusable = false,
            });
            row.Children.Add(text);
            button.Content = row;
        }
        AutomationProperties.SetName(button, label);
        button.Padding = ActionPaddingFor(button, ActionPadding);
        if (IsOpen) UpdatePlacement();
    }

    private Thickness ActionPaddingFor(Button button, double side)
    {
        var leading = _iconLabels.ContainsKey(button)
            ? Math.Max(CompactActionPadding, side - IconSidePaddingReduction)
            : side;
        return new Thickness(leading, 0, side, 0);
    }

    internal void Show(Rect anchorDips, Size viewportDips)
    {
        _anchor = anchorDips;
        _viewport = viewportDips;
        var wasOpen = IsOpen;
        var wasVisible = Surface.Visibility == Visibility.Visible;
        _exit?.Dispose();
        _exit = null;
        IsOpen = true;
        Surface.IsEnabled = true;
        Surface.IsHitTestVisible = true;
        Surface.Visibility = Visibility.Visible;
        UpdatePlacement();
        if (!wasOpen)
            CardTransitions.BeginEntrance(
                Surface, _animationsEnabled(), TimeSpan.FromMilliseconds(220), 0.96,
                preserveCurrentValues: wasVisible);
    }

    private void UpdatePlacement()
    {
        UpdateDividers();
        MeasureWithin(FloatingToolbarLayout.AvailableWidth(_viewport.Width, _safeInsets));
        var placement = FloatingToolbarLayout.Place(_anchor, Surface.DesiredSize, _viewport, _safeInsets,
            preferBelow: _preferBelow);
        Canvas.SetLeft(Surface, placement.X);
        Canvas.SetTop(Surface, placement.Y);
    }

    // Hidden actions can leave a divider with nothing on one side of it.
    private void UpdateDividers()
    {
        var children = _actions.Children.OfType<FrameworkElement>().ToArray();
        foreach (var divider in _dividers)
        {
            var index = Array.IndexOf(children, divider);
            var visible = children[..index].Any(IsVisibleAction) && children[(index + 1)..].Any(IsVisibleAction);
            divider.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private static bool IsVisibleAction(FrameworkElement element) =>
        element is Button { Visibility: Visibility.Visible };

    private void MeasureWithin(double width)
    {
        if (IsPromptOpen)
        {
            var padding = Surface.Padding.Left + Surface.Padding.Right;
            _prompt!.Root.Width = Math.Max(0, Math.Min(PromptWidth, width - padding));
            Surface.MaxWidth = Math.Max(0, width);
            Surface.Measure(new Size(Surface.MaxWidth, double.PositiveInfinity));
            return;
        }
        var buttons = _actions.Children.OfType<Button>().ToArray();
        foreach (var button in buttons) button.Padding = ActionPaddingFor(button, ActionPadding);
        Surface.MaxWidth = double.PositiveInfinity;
        Surface.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var overflow = Surface.DesiredSize.Width - width;
        if (overflow > 0 && buttons.Length > 0)
        {
            var padding = Math.Max(CompactActionPadding, ActionPadding - overflow / (2 * buttons.Length));
            foreach (var button in buttons) button.Padding = ActionPaddingFor(button, padding);
        }
        Surface.MaxWidth = Math.Max(0, width);
        Surface.Measure(new Size(Surface.MaxWidth, double.PositiveInfinity));
    }

    internal void Hide(bool animate = true)
    {
        var wasOpen = IsOpen;
        IsOpen = false;
        Surface.IsHitTestVisible = false;
        Surface.IsEnabled = false;
        if (!animate || !_animationsEnabled())
        {
            CompleteExit();
            return;
        }
        if (!wasOpen) return;
        _exit = CardTransitions.BeginExit(
            Surface, animationsEnabled: true, CompleteExit, TimeSpan.FromMilliseconds(150));
    }

    private void CompleteExit()
    {
        _exit?.Dispose();
        _exit = null;
        CardTransitions.Settle(Surface);
        Surface.Visibility = Visibility.Collapsed;
    }

    private static Viewbox OutlinedIcon(Geometry geometry)
    {
        var path = new Path
        {
            Data = geometry,
            StrokeThickness = 1.65,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
        };
        path.SetBinding(Shape.StrokeProperty, new System.Windows.Data.Binding(nameof(Control.Foreground))
        {
            RelativeSource = new System.Windows.Data.RelativeSource(
                System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(Control), 1),
        });
        var canvas = new Canvas { Width = 24, Height = 24 };
        canvas.Children.Add(path);
        return new Viewbox
        {
            Width = 20,
            Height = 20,
            Child = canvas,
            IsHitTestVisible = false,
        };
    }
}

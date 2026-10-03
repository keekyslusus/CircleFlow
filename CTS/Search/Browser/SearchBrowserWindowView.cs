using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CircleToSearch.Interop;
using CircleToSearch.Ui;
using Ellipse = System.Windows.Shapes.Ellipse;

namespace CircleToSearch.Search.Browser;

internal sealed class SearchBrowserWindowView
{
    private readonly UiStrings _strings;
    private readonly FrameworkElement _browserContent;
    private readonly Grid _content;
    private readonly BottomResultsPanel _resultsPanel;
    private readonly Grid? _loadingOverlay;
    private readonly StackPanel? _loadingDots;
    private readonly TextBlock? _loadingText;
    private readonly TextBlock _titleText;
    private readonly Button _closeButton;
    private int _loadingGeneration;

    internal SearchBrowserWindowView(
        UiStrings strings,
        FrameworkElement browserContent,
        bool lightTheme,
        bool loadingOverlayEnabled,
        Func<Window, BottomResultsPanel> createResultsPanel)
    {
        _strings = strings;
        _browserContent = browserContent;
        var palette = PluginPalette.For(lightTheme);
        var background = Frozen(palette.Roles.Background);
        _content = new Grid { Background = background };
        _content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _content.RowDefinitions.Add(new RowDefinition());
        Grid.SetRow(browserContent, 1);
        _content.Children.Add(browserContent);
        browserContent.Visibility = loadingOverlayEnabled ? Visibility.Hidden : Visibility.Visible;

        if (loadingOverlayEnabled)
        {
            _loadingOverlay = CreateLoadingOverlay(palette, out var loadingText, out var loadingDots);
            _loadingText = loadingText;
            _loadingDots = loadingDots;
            Grid.SetRow(_loadingOverlay, 1);
            _content.Children.Add(_loadingOverlay);
        }

        Window = new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Background = background,
            Content = _content,
        };
        try
        {
            var header = new DockPanel { Margin = new Thickness(16, 8, 12, 8) };
            _closeButton = new Button
            {
                Width = 32,
                Height = 28,
                Padding = new Thickness(0),
                Background = background,
                Foreground = Frozen(palette.Roles.OnBackground),
                BorderThickness = new Thickness(0),
                Template = CreateCloseButtonTemplate(),
            };
            var closeIcon = new System.Windows.Shapes.Path
            {
                Data = PluginIcons.CloseOutlined,
                Width = 10,
                Height = 10,
                StrokeThickness = 1.5,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            closeIcon.SetBinding(
                System.Windows.Shapes.Shape.StrokeProperty,
                new Binding(nameof(Control.Foreground)) { Source = _closeButton });
            _closeButton.Content = closeIcon;
            _closeButton.Click += OnCloseClick;
            DockPanel.SetDock(_closeButton, Dock.Right);
            header.Children.Add(_closeButton);
            _titleText = new TextBlock
            {
                Foreground = Frozen(palette.Roles.OnBackground),
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 14,
            };
            header.Children.Add(_titleText);
            _content.Children.Add(header);
            _resultsPanel = createResultsPanel(Window);
            Window.SourceInitialized += OnSourceInitialized;
            Window.Closed += OnClosed;
            ApplyTheme(lightTheme);
        }
        catch
        {
            Window.Close();
            throw;
        }
    }

    internal Window Window { get; }
    internal bool IsClosed { get; private set; }

    // Runs for every search the reused window shows, so its text follows app language changes too.
    internal void SetProvider(SearchProviderDescriptor descriptor)
    {
        _closeButton.ToolTip = _strings.Close;
        AutomationProperties.SetName(_closeButton, _strings.Close);
        var title = _strings.SearchBrowserWindowTitle(descriptor.DisplayName);
        Window.Title = title;
        _titleText.Text = title;
        if (_loadingText is not null)
            _loadingText.Text = _strings.SearchBrowserLoading(descriptor.DisplayName);
    }

    internal void ApplyTheme(bool lightTheme)
    {
        _lightTheme = lightTheme;
        var palette = PluginPalette.For(lightTheme);
        var background = Frozen(palette.Roles.Background);
        Window.Background = background;
        _content.Background = background;
        if (_loadingOverlay is not null) _loadingOverlay.Background = background;
        if (_loadingText is not null) _loadingText.Foreground = Frozen(palette.Roles.OnBackground);
        _titleText.Foreground = Frozen(palette.Roles.OnBackground);
        _closeButton.Background = background;
        _closeButton.Foreground = Frozen(palette.Roles.OnBackground);
        _closeButton.BorderBrush = Frozen(SystemAccentColor.Read());
        ApplyWindowChromeTheme(Window, lightTheme);
    }

    internal void MoveTo(POINT anchor) => _resultsPanel.MoveTo(anchor);

    internal Task ShowAsync() => _resultsPanel.ShowAsync();

    internal void ShowHidden() => _resultsPanel.ShowHidden();

    internal void ShowLoading()
    {
        var overlay = _loadingOverlay;
        if (overlay is null || IsClosed) return;
        _loadingGeneration++;
        overlay.BeginAnimation(UIElement.OpacityProperty, null);
        overlay.Opacity = 1;
        overlay.Visibility = Visibility.Visible;
        _browserContent.Visibility = Visibility.Hidden;
    }

    internal void HideLoading()
    {
        var overlay = _loadingOverlay;
        if (overlay is null || IsClosed || overlay.Visibility != Visibility.Visible) return;
        var generation = _loadingGeneration;
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(180));
        fade.Completed += (_, _) =>
        {
            if (IsClosed || generation != _loadingGeneration) return;
            _browserContent.Visibility = Visibility.Visible;
            overlay.Visibility = Visibility.Collapsed;
            overlay.BeginAnimation(UIElement.OpacityProperty, null);
            overlay.Opacity = 1;
        };
        overlay.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    private void OnCloseClick(object sender, RoutedEventArgs args) => Window.Close();

    private static ControlTemplate CreateCloseButtonTemplate()
    {
        var root = new FrameworkElementFactory(typeof(Grid));
        root.SetValue(Panel.BackgroundProperty, Frozen(PluginPalette.Transparent));
        var hoverSurface = new FrameworkElementFactory(typeof(Ellipse), "HoverSurface");
        hoverSurface.SetValue(FrameworkElement.WidthProperty, 28d);
        hoverSurface.SetValue(FrameworkElement.HeightProperty, 28d);
        hoverSurface.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        hoverSurface.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        hoverSurface.SetValue(UIElement.OpacityProperty, 0d);
        hoverSurface.SetBinding(System.Windows.Shapes.Shape.FillProperty, new Binding(nameof(Control.BorderBrush))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent),
        });
        root.AppendChild(hoverSurface);
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        content.SetBinding(ContentPresenter.ContentProperty, new Binding(nameof(ContentControl.Content))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent),
        });
        root.AppendChild(content);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = root };
        var hover = new System.Windows.Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.EnterActions.Add(FadeCloseButtonHover(0.24));
        hover.ExitActions.Add(FadeCloseButtonHover(0));
        template.Triggers.Add(hover);
        return template;
    }

    private static BeginStoryboard FadeCloseButtonHover(double opacity)
    {
        var animation = new DoubleAnimation
        {
            To = opacity,
            Duration = UiAnimationPolicy.ToggleTransitionDuration,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTargetName(animation, "HoverSurface");
        Storyboard.SetTargetProperty(animation, new PropertyPath(UIElement.OpacityProperty));
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        return new BeginStoryboard { Storyboard = storyboard, HandoffBehavior = HandoffBehavior.SnapshotAndReplace };
    }

    private void OnSourceInitialized(object? sender, EventArgs args)
        => ApplyWindowChromeTheme(Window, _lightTheme);

    private bool _lightTheme;

    private void OnClosed(object? sender, EventArgs args)
    {
        IsClosed = true;
        _loadingGeneration++;
        _loadingOverlay?.BeginAnimation(UIElement.OpacityProperty, null);
        if (_loadingDots is not null)
        {
            foreach (var dot in _loadingDots.Children)
                if (dot is Ellipse ellipse) ellipse.BeginAnimation(UIElement.OpacityProperty, null);
        }
        _closeButton.Click -= OnCloseClick;
        Window.SourceInitialized -= OnSourceInitialized;
        Window.Closed -= OnClosed;
    }

    private static Grid CreateLoadingOverlay(
        PluginThemePalette palette,
        out TextBlock loadingText,
        out StackPanel dots)
    {
        dots = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        for (var index = 0; index < PluginPalette.SearchBrowserLoadingDots.Count; index++)
        {
            var dot = new Ellipse
            {
                Width = 10,
                Height = 10,
                Margin = new Thickness(5),
                Fill = Frozen(PluginPalette.SearchBrowserLoadingDots[index]),
            };
            dot.BeginAnimation(
                UIElement.OpacityProperty,
                new DoubleAnimation(0.25, 1, TimeSpan.FromMilliseconds(520))
                {
                    AutoReverse = true,
                    BeginTime = TimeSpan.FromMilliseconds(index * 140),
                    RepeatBehavior = RepeatBehavior.Forever,
                });
            dots.Children.Add(dot);
        }
        loadingText = new TextBlock
        {
            Margin = new Thickness(0, 18, 0, 0),
            FontFamily = new FontFamily("Segoe UI Variable Text"),
            FontSize = 16,
            Foreground = Frozen(palette.Roles.OnBackground),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        var center = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        center.Children.Add(dots);
        center.Children.Add(loadingText);
        var overlay = new Grid
        {
            Background = Frozen(palette.Roles.Background),
            IsHitTestVisible = true,
        };
        overlay.Children.Add(center);
        Panel.SetZIndex(overlay, 1);
        return overlay;
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static void ApplyWindowChromeTheme(Window window, bool lightTheme)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        var useDarkMode = lightTheme ? 0 : 1;
        var result = NativeMethods.DwmSetWindowAttribute(
            hwnd, NativeMethods.DwmwaUseImmersiveDarkMode, ref useDarkMode, sizeof(int));
        if (result != 0)
            NativeMethods.DwmSetWindowAttribute(
                hwnd, NativeMethods.DwmwaUseImmersiveDarkModeBefore20H1,
                ref useDarkMode, sizeof(int));
    }
}

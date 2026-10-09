using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using CircleToSearch.Ui;

namespace CircleToSearch.Shell.Notifications;

// The link is kept as text only: comparing contents finds a repeated offer, which a new delegate would never match.
internal sealed record NotificationContent(string? Title, string Message, string? ActionText, string? LinkText, bool IsError);

internal sealed record NotificationChrome(string AppName, string CloseText, ImageSource? Icon, Brush ErrorBrush, double ShadowOpacity);

internal sealed class NotificationCard
{
    internal const double CardWidth = 340;
    internal const double Gap = 10;
    private const double SlideOffset = 24;
    private readonly Border _card;
    private readonly TranslateTransform _slide = new();
    private readonly DispatcherTimer? _dismissTimer;
    private int _version;
    private bool _leaving;

    internal NotificationCard(FrameworkElement styles, NotificationContent content, NotificationChrome chrome,
        Action close, Action? act, Action? openLink, TimeSpan? autoDismiss)
    {
        Content = content;
        CloseButton = new Button
        {
            Style = (Style)styles.FindResource("NotificationClose"),
            Content = new Canvas
            {
                Width = 10, Height = 10,
                Children = { new Path { Data = PluginIcons.CloseOutlined, Style = (Style)styles.FindResource("OutlineIcon") } },
            },
        };
        AutomationProperties.SetName(CloseButton, chrome.CloseText);
        CloseButton.Click += (_, _) => close();

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(CloseButton, Dock.Right);
        header.Children.Add(CloseButton);
        if (chrome.Icon is not null)
            header.Children.Add(new Image
            {
                Source = chrome.Icon, Width = 16, Height = 16, Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });
        // The shared Muted style fixes a taller line height, which would lift the name above the icon's center.
        header.Children.Add(new TextBlock
        {
            Text = chrome.AppName, FontSize = PluginTypography.Caption, VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)styles.FindResource("SettingsMuted"),
        });

        // Long error text is cut off at the style's MaxHeight; the tooltip keeps all of it readable.
        MessageText = new TextBlock { Style = (Style)styles.FindResource("NotificationMessage"), ToolTip = content.Message };
        var linkStart = content.LinkText is { } linked && openLink is not null
            ? content.Message.IndexOf(linked, StringComparison.Ordinal)
            : -1;
        if (linkStart < 0) MessageText.Text = content.Message;
        else
        {
            var linkEnd = linkStart + content.LinkText!.Length;
            Link = new Hyperlink(new Run(content.LinkText)) { Foreground = (Brush)styles.FindResource("SettingsAccent") };
            Link.Click += (_, _) => openLink!();
            MessageText.Inlines.Add(new Run(content.Message[..linkStart]));
            MessageText.Inlines.Add(Link);
            MessageText.Inlines.Add(new Run(content.Message[linkEnd..]));
        }
        AutomationProperties.SetLiveSetting(MessageText, AutomationLiveSetting.Polite);
        var text = new StackPanel();
        if (content.Title is not null)
        {
            TitleText = new TextBlock { Text = content.Title, Style = (Style)styles.FindResource("NotificationTitle"), Margin = new Thickness(0, 0, 0, 2) };
            text.Children.Add(TitleText);
            MessageText.Foreground = (Brush)styles.FindResource("SettingsMuted");
        }
        text.Children.Add(MessageText);
        var body = new DockPanel { Margin = new Thickness(0, 0, 6, 0) };
        if (content.IsError)
        {
            var icon = new Viewbox
            {
                Width = 20, Height = 20, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Top,
                Child = new Canvas
                {
                    Width = 24, Height = 24,
                    Children =
                    {
                        new Path
                        {
                            Data = PluginIcons.ErrorOutlined, Style = (Style)styles.FindResource("OutlineIcon"),
                            Stroke = chrome.ErrorBrush,
                        },
                    },
                },
            };
            DockPanel.SetDock(icon, Dock.Left);
            body.Children.Add(icon);
        }
        body.Children.Add(text);

        var layout = new StackPanel();
        layout.Children.Add(header);
        layout.Children.Add(body);
        if (content.ActionText is not null && act is not null)
        {
            ActionButton = new Button { Content = content.ActionText, Style = (Style)styles.FindResource("PrimaryButton") };
            ActionButton.Click += (_, _) => act();
            var dismiss = new Button { Content = chrome.CloseText, Margin = new Thickness(8, 0, 0, 0) };
            dismiss.Click += (_, _) => close();
            var actions = new Grid { Margin = new Thickness(0, 14, 6, 0) };
            actions.ColumnDefinitions.Add(new ColumnDefinition());
            actions.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.SetColumn(dismiss, 1);
            actions.Children.Add(ActionButton);
            actions.Children.Add(dismiss);
            layout.Children.Add(actions);
        }

        _card = new Border
        {
            Style = (Style)styles.FindResource("NotificationCard"),
            Width = CardWidth,
            Child = layout,
            RenderTransform = _slide,
            Effect = new DropShadowEffect
            {
                Color = PluginPalette.OpaqueBlack, BlurRadius = 18, ShadowDepth = 3, Direction = 270,
                Opacity = chrome.ShadowOpacity,
            },
        };
        Canvas.SetTop(_card, Gap);
        // A canvas never clips or squeezes the card, so its height can open and close the gap in the stack.
        Root = new Canvas { Width = CardWidth, Children = { _card } };

        if (autoDismiss is { } delay)
        {
            _dismissTimer = new DispatcherTimer(DispatcherPriority.Background, styles.Dispatcher) { Interval = delay };
            _dismissTimer.Tick += (_, _) => close();
            // Reading a message should not race its timer.
            _card.MouseEnter += (_, _) => _dismissTimer.Stop();
            // Leaving disables hit testing, which raises MouseLeave on a card that must not start counting again.
            _card.MouseLeave += (_, _) => { if (!_leaving) _dismissTimer.Start(); };
        }
    }

    internal NotificationContent Content { get; }
    internal Canvas Root { get; }
    internal Button CloseButton { get; }
    internal Button? ActionButton { get; }
    internal Hyperlink? Link { get; }
    internal TextBlock? TitleText { get; }
    internal TextBlock MessageText { get; }
    internal Border Card => _card;
    internal bool IsCountingDown => _dismissTimer?.IsEnabled == true;

    internal void Enter()
    {
        var version = ++_version;
        _card.Measure(new Size(CardWidth, double.PositiveInfinity));
        var height = _card.DesiredSize.Height + Gap;
        _dismissTimer?.Start();
        if (!UiAnimationPolicy.Enabled || !Root.IsVisible)
        {
            SetValues(height, 0, 1);
            return;
        }
        SetValues(0, SlideOffset, 0);
        var duration = TimeSpan.FromMilliseconds(250);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        MotionValue.Animate(_slide, TranslateTransform.XProperty, 0, duration, easing);
        MotionValue.Animate(_card, UIElement.OpacityProperty, 1, duration, easing);
        MotionValue.Animate(Root, FrameworkElement.HeightProperty, height, duration, easing, () =>
        {
            if (version == _version) SetValues(height, 0, 1);
        });
    }

    internal void Exit(Action removed)
    {
        var version = ++_version;
        _leaving = true;
        _dismissTimer?.Stop();
        Root.IsHitTestVisible = false;
        if (!UiAnimationPolicy.Enabled || !Root.IsVisible)
        {
            removed();
            return;
        }
        var easing = new CubicEase { EasingMode = EasingMode.EaseIn };
        MotionValue.Animate(_slide, TranslateTransform.XProperty, SlideOffset, TimeSpan.FromMilliseconds(170), easing);
        MotionValue.Animate(_card, UIElement.OpacityProperty, 0, TimeSpan.FromMilliseconds(170), easing);
        MotionValue.Animate(Root, FrameworkElement.HeightProperty, 0, TimeSpan.FromMilliseconds(220), easing, () =>
        {
            if (version == _version) removed();
        });
    }

    internal void Stop()
    {
        _version++;
        _leaving = true;
        _dismissTimer?.Stop();
    }

    private void SetValues(double height, double x, double opacity)
    {
        Root.BeginAnimation(FrameworkElement.HeightProperty, null);
        _slide.BeginAnimation(TranslateTransform.XProperty, null);
        _card.BeginAnimation(UIElement.OpacityProperty, null);
        Root.Height = height;
        _slide.X = x;
        _card.Opacity = opacity;
    }
}

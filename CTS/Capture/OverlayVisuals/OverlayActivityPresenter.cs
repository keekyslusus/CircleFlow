namespace CircleToSearch.Capture;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Effects;
using CircleToSearch.Ui;

internal sealed class OverlayActivityPresenter : IDisposable
{
    private readonly Grid _host;
    private readonly Func<bool> _animationsEnabled;
    private readonly Grid _contentHost = new();
    private readonly TextBlock _label;
    private readonly Grid _panel;
    private readonly LoadingIndicatorVisual _loading = new() { Width = 80, Height = 80 };
    private ActivityPresentation? _current;
    private CardTransitions.ExitHandle? _exit;
    private long _generation;
    private bool _disposed;

    internal OverlayActivityPresenter(Grid host, Func<bool> animationsEnabled)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _animationsEnabled = animationsEnabled ?? throw new ArgumentNullException(nameof(animationsEnabled));
        _label = new TextBlock
        {
            Foreground = OverlayVisualResources.Frozen(PluginPalette.ListeningText),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            FontSize = 14,
            FontWeight = FontWeights.Medium,
            FontFamily = OverlayVisualResources.Font,
            Margin = new Thickness(0, 10, 0, 0),
            Effect = new DropShadowEffect
            {
                Color = PluginPalette.OpaqueBlack,
                BlurRadius = 10,
                ShadowDepth = 1,
                Opacity = 0.4,
            },
        };
        _label.SetBinding(FrameworkElement.MaxWidthProperty, new Binding(nameof(FrameworkElement.ActualWidth))
        {
            Source = _host,
            Mode = BindingMode.OneWay,
        });
        _panel = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            ClipToBounds = true,
        };
        _panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _panel.SetBinding(FrameworkElement.MaxWidthProperty, new Binding(nameof(FrameworkElement.ActualWidth))
        {
            Source = _host,
            Mode = BindingMode.OneWay,
        });
        _panel.SetBinding(FrameworkElement.MaxHeightProperty, new Binding(nameof(FrameworkElement.ActualHeight))
        {
            Source = _host,
            Mode = BindingMode.OneWay,
        });
        _panel.Children.Add(_contentHost);
        Grid.SetRow(_label, 1);
        _panel.Children.Add(_label);
    }

    internal LoadingIndicatorVisual LoadingIndicator => _loading;

    internal ActivityPresentation ShowLoading(string label, Brush fill)
    {
        ArgumentNullException.ThrowIfNull(fill);
        _loading.Fill = fill;
        return Show(label, _loading, ownsRendering: true);
    }

    internal ActivityPresentation ShowContent(FrameworkElement content, string label)
    {
        ArgumentNullException.ThrowIfNull(content);
        return Show(label, content, ownsRendering: false);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _host.Dispatcher.VerifyAccess();
        _disposed = true;
        FinishCurrent();
        _loading.Dispose();
        _host.Children.Clear();
    }

    private ActivityPresentation Show(string label, FrameworkElement content, bool ownsRendering)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(OverlayActivityPresenter));
        _host.Dispatcher.VerifyAccess();
        ArgumentNullException.ThrowIfNull(label);
        FinishCurrent();

        var presentation = new ActivityPresentation(this, ++_generation, ownsRendering);
        _current = presentation;
        _label.Text = label;
        _contentHost.Children.Add(content);
        if (!_host.Children.Contains(_panel)) _host.Children.Add(_panel);
        _host.Visibility = Visibility.Visible;
        if (ownsRendering) _loading.Start(_animationsEnabled());
        StateCardTransitions.BeginEntrance(_panel, _animationsEnabled());
        return presentation;
    }

    private Task HideAsync(ActivityPresentation presentation)
    {
        _host.Dispatcher.VerifyAccess();
        if (_disposed || !ReferenceEquals(_current, presentation))
        {
            presentation.Complete();
            return presentation.Completion;
        }
        if (_exit is not null) return presentation.Completion;

        if (presentation.OwnsRendering) _loading.BeginStop();
        var exit = StateCardTransitions.BeginExit(
            _panel,
            _animationsEnabled(),
            () => CompleteHide(presentation));
        if (!exit.IsCompleted && ReferenceEquals(_current, presentation)) _exit = exit;
        else exit.Dispose();
        return presentation.Completion;
    }

    private void Dispose(ActivityPresentation presentation)
    {
        _host.Dispatcher.VerifyAccess();
        if (ReferenceEquals(_current, presentation)) FinishCurrent();
        else presentation.Complete();
    }

    private void CompleteHide(ActivityPresentation presentation)
    {
        if (_disposed || !ReferenceEquals(_current, presentation))
        {
            presentation.Complete();
            return;
        }
        FinishCurrent();
    }

    private void FinishCurrent()
    {
        var presentation = _current;
        _current = null;
        _exit?.Dispose();
        _exit = null;
        if (presentation?.OwnsRendering == true) _loading.Stop();
        _contentHost.Children.Clear();
        _host.Children.Remove(_panel);
        _host.Visibility = Visibility.Collapsed;
        presentation?.Complete();
    }

    internal sealed class ActivityPresentation : IDisposable
    {
        private readonly OverlayActivityPresenter _presenter;
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _disposed;

        internal ActivityPresentation(OverlayActivityPresenter presenter, long generation, bool ownsRendering)
        {
            _presenter = presenter;
            Generation = generation;
            OwnsRendering = ownsRendering;
        }

        internal long Generation { get; }
        internal bool OwnsRendering { get; }
        internal Task Completion => _completion.Task;

        internal Task HideAsync() => _presenter.HideAsync(this);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _presenter.Dispose(this);
        }

        internal void Complete() => _completion.TrySetResult();
    }
}

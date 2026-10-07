using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CircleToSearch.Search;
using CircleToSearch.Ui;
using CircleToSearch.Ui.Effects;

namespace CircleToSearch.Capture;

internal interface IOverlayWidgetVisual : IDisposable
{
    void ShowResult(VisualSearchPreparationOutcome outcome);
    void DismissResult();
    bool TryGoBack();
}

internal sealed record OverlayWidgetContext(
    Grid Root,
    OverlayActivityPresenter ActivityPresenter,
    BottomOverlayVisual Bottom,
    OverlayEffectsVisual Effects,
    UiStrings Strings,
    bool LightTheme,
    Action<Uri> Open,
    Action Close,
    ClipboardCopyService ClipboardCopy);

// Owns the loading indicator, reveal and exit of a result card placed above the overlay's bottom controls.
internal sealed class OverlayWidgetCardHost : IDisposable
{
    private readonly Grid _root;
    private readonly OverlayActivityPresenter _activityPresenter;
    private readonly OverlayEffectsVisual _effects;
    private readonly BottomOverlayVisual _bottom;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Grid _resultHost = new()
    {
        Visibility = Visibility.Collapsed, Opacity = 0, IsHitTestVisible = false,
        HorizontalAlignment = HorizontalAlignment.Center,
    };
    private readonly List<IDisposable> _controlRipples = [];
    private OverlayActivityPresenter.ActivityPresentation? _activity;
    private Border? _card;
    private CardTransitions.ExitHandle? _exit;
    private DispatcherOperation? _ripple;
    private bool _disposed;
    private bool _closing;

    internal OverlayWidgetCardHost(OverlayWidgetContext context)
    {
        _root = context.Root;
        _activityPresenter = context.ActivityPresenter;
        _bottom = context.Bottom;
        _effects = context.Effects;
        _resultHost.Margin = context.Bottom.ResultSlot.Margin;
    }

    internal Task Presentation { get; private set; } = Task.CompletedTask;

    internal bool IsActive => !_disposed && !_closing;

    internal bool HasCard => _card is not null;

    internal double CardWidth => Math.Min(640, Math.Max(240, _root.ActualWidth - 32));

    internal void ShowLoading(string message, Color color) =>
        _activity = _activityPresenter.ShowLoading(message, OverlayVisualResources.Frozen(color));

    // The card stays hidden until readiness completes so its media does not pop in after the entrance.
    internal void Present(Border card, bool matched, Func<CancellationToken, Task>? readiness = null)
    {
        if (!IsActive || _card is not null) return;
        _card = card;
        _resultHost.Children.Add(card);
        _bottom.LayoutTransitions.Apply(() =>
        {
            _bottom.Stack.Children.Insert(_bottom.Stack.Children.IndexOf(_bottom.ResultSlot), _resultHost);
            _resultHost.Visibility = Visibility.Visible;
        }, OverlayVisualResources.AnimationsEnabled());
        Presentation = _root.Dispatcher.InvokeAsync(() => RevealWhenReadyAsync(matched, readiness)).Task.Unwrap();
    }

    internal void ChangeLayout(Action mutation)
    {
        if (!IsActive) return;
        _bottom.LayoutTransitions.Apply(mutation, OverlayVisualResources.AnimationsEnabled());
        // Applying the transition lays the card out, so content inside new scroll viewers is in the visual tree by now.
        foreach (var ripple in _controlRipples) ripple.Dispose();
        _controlRipples.Clear();
        _controlRipples.AddRange(OverlayVisualResources.AttachControlRipples(_resultHost));
    }

    private async Task RevealWhenReadyAsync(bool matched, Func<CancellationToken, Task>? readiness)
    {
        if (!IsActive) return;
        try
        {
            if (readiness is not null) await readiness(_lifetime.Token);
            if (!IsActive) return;
            var activity = _activity;
            if (activity is not null) await activity.HideAsync().WaitAsync(_lifetime.Token);
            if (!IsActive) return;
            if (ReferenceEquals(_activity, activity)) _activity = null;
            _resultHost.Opacity = 1;
            _resultHost.IsHitTestVisible = true;
            _controlRipples.AddRange(OverlayVisualResources.AttachControlRipples(_resultHost));
            StateCardTransitions.BeginEntrance(_card!, OverlayVisualResources.AnimationsEnabled());
            if (!matched || !OverlayVisualResources.AnimationsEnabled()) return;
            _ripple = _root.Dispatcher.BeginInvoke(() =>
            {
                _ripple = null;
                if (!IsActive) return;
                _resultHost.UpdateLayout();
                var origin = _card!.TranslatePoint(new Point(_card.ActualWidth / 2, _card.ActualHeight / 2), _root);
                _effects.SceneRipples.Emit(new SceneRippleRequest(origin, SceneRipplePreset.MusicMatch, 1));
            }, DispatcherPriority.Loaded);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
    }

    internal void Dismiss()
    {
        if (!IsActive) return;
        _closing = true;
        _lifetime.Cancel();
        _activity?.Dispose();
        _activity = null;
        _ripple?.Abort();
        _resultHost.IsHitTestVisible = false;
        if (_card is null)
        {
            Dispose();
            return;
        }
        _exit = StateCardTransitions.BeginExit(_card, OverlayVisualResources.AnimationsEnabled(),
            () => _bottom.LayoutTransitions.Apply(Dispose, OverlayVisualResources.AnimationsEnabled()));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _activity?.Dispose();
        _activity = null;
        _exit?.Dispose();
        _ripple?.Abort();
        foreach (var ripple in _controlRipples) ripple.Dispose();
        _bottom.Stack.Children.Remove(_resultHost);
        _lifetime.Dispose();
    }
}

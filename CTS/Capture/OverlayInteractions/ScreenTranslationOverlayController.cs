using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using CircleToSearch.Translation;
using CircleToSearch.Ui;
using CircleToSearch.Ui.Effects;

namespace CircleToSearch.Capture.OverlayInteractions;

internal sealed class ScreenTranslationOverlayController : IDisposable
{
    private readonly TranslationActionVisual _action;
    private readonly OverlayActivityPresenter _activityPresenter;
    private readonly TranslationOverlayVisual _overlay;
    private readonly BottomOverlayVisual _bottom;
    private readonly OverlayEffectsVisual _effects;
    private readonly FrameworkElement _coordinateRoot;
    private readonly UiStrings _strings;
    private readonly Func<bool> _consentAccepted;
    private readonly Action _acceptConsent;
    private readonly Func<string> _targetLanguageTag;
    private readonly Action<IOverlayCommand> _publish;
    private readonly Action<OverlayInteractionMode> _transition;
    private readonly Action<ToastNotification> _showToast;
    private readonly Func<bool> _animationsEnabled;
    private readonly bool _lightTheme;
    private Guid _requestId;
    private DispatcherOperation? _completionRippleOperation;
    private bool _disposed;
    private readonly Image _screenshot;
    private BitmapSource _scopeOriginal;
    private System.Drawing.Rectangle? _region;
    private readonly Action<BitmapSource, string?>? _imageChanged;
    private bool _imageShown;
    private BitmapSource? _cachedImage;
    private string? _cachedTarget;
    private string? _requestedTarget;
    private bool _closing;
    private readonly TranslationMemoryProfiler? _profiler;
    private readonly string _profileScope = Guid.NewGuid().ToString("N");
    private StateCardVisual? _stateCard;
    private TranslationCardKind _cardKind;
    private CardTransitions.ExitHandle? _pendingCardExit;
    private OverlayActivityPresenter.ActivityPresentation? _activity;
    private long _cardGeneration;
    private readonly List<IDisposable> _cardRipples = [];

    private enum TranslationCardKind { None, Consent, Failure }

    internal ScreenTranslationOverlayController(
        TranslationActionVisual action,
        OverlayActivityPresenter activityPresenter,
        TranslationOverlayVisual overlay,
        BottomOverlayVisual bottom,
        OverlayEffectsVisual effects,
        FrameworkElement coordinateRoot,
        UiStrings strings,
        Func<bool> consentAccepted,
        Action acceptConsent,
        Func<string> targetLanguageTag,
        Action<IOverlayCommand> publish,
        Action<OverlayInteractionMode> transition,
        Action<ToastNotification> showToast,
        Func<bool> animationsEnabled,
        bool lightTheme,
        Image screenshot,
        Action<BitmapSource, string?>? imageChanged = null,
        TranslationMemoryProfiler? profiler = null)
    {
        _action = action;
        _activityPresenter = activityPresenter;
        _overlay = overlay;
        _bottom = bottom;
        _effects = effects;
        _coordinateRoot = coordinateRoot;
        _strings = strings;
        _consentAccepted = consentAccepted;
        _acceptConsent = acceptConsent;
        _targetLanguageTag = targetLanguageTag;
        _publish = publish;
        _transition = transition;
        _showToast = showToast;
        _animationsEnabled = animationsEnabled;
        _lightTheme = lightTheme;
        _screenshot = screenshot;
        _scopeOriginal = screenshot.Source as BitmapSource
            ?? throw new InvalidOperationException("The overlay screenshot source must be a bitmap.");
        _imageChanged = imageChanged;
        _profiler = profiler;
        _profiler?.Mark("overlay_open", _profileScope);
        _action.Button.Click += OnTranslate;
    }

    internal bool IsConsentOpen => _cardKind == TranslationCardKind.Consent;
    internal bool IsTranslating => _requestId != Guid.Empty;
    internal bool IsTranslationShown => _imageShown;
    internal bool IsImageShown => _imageShown;

    internal void SetRegion(System.Drawing.Rectangle bounds)
    {
        _scopeOriginal = (BitmapSource)_screenshot.Source;
        _region = bounds;
        _cachedImage = null;
        _cachedTarget = null;
        _imageShown = false;
        SetActionVisual(_strings.Translate, PluginIcons.TranslateFilled);
    }

    internal void CommitVisibleImage()
    {
        _scopeOriginal = (BitmapSource)_screenshot.Source;
        _region = null;
        _cachedImage = null;
        _cachedTarget = null;
        _imageShown = false;
        AbortPendingCompletionRipple();
        SetActionVisual(_strings.Translate, PluginIcons.TranslateFilled);
    }

    internal bool HasPendingCompletionRipple =>
        _completionRippleOperation?.Status == DispatcherOperationStatus.Pending;

    internal void SetActionEnabled(bool enabled)
    {
        if (_disposed) return;
        _action.Button.IsEnabled = enabled;
    }

    internal void ShowResult(ScreenTranslationResult result)
    {
        if (_disposed || _closing || _requestId == Guid.Empty || result.RequestId != _requestId) return;
        var target = _requestedTarget
            ?? throw new InvalidOperationException("The translation target is missing for the active request.");
        AbortPendingCompletionRipple();
        _requestId = Guid.Empty;
        SetActionVisual(_strings.ShowOriginal, PluginIcons.ShowOriginalFilled);
        StopLoading();
        var bounds = _region ?? new System.Drawing.Rectangle(0, 0, _scopeOriginal.PixelWidth, _scopeOriginal.PixelHeight);
        var scaled = VisibleImage.ReplaceRegion(_scopeOriginal, result.Image, bounds);
        _cachedImage = scaled;
        _cachedTarget = target;
        DisplayImage(scaled, _cachedTarget);
        _transition(OverlayInteractionMode.TranslationShown);
        _profiler?.Mark("translation_shown", _profileScope);
        QueueCompletionRipple();
    }

    internal void ShowFailure(Guid requestId, TranslationFailure failure)
    {
        if (_disposed || _closing || _requestId == Guid.Empty || requestId != _requestId) return;
        _requestId = Guid.Empty;
        SetActionVisual(_strings.Translate, PluginIcons.TranslateFilled);
        StopLoading();
        if (failure == TranslationFailure.Canceled)
        {
            _transition(OverlayInteractionMode.Selecting);
            return;
        }
        var message = failure switch
        {
            TranslationFailure.Network => _strings.TranslationNetworkError,
            TranslationFailure.Timeout => _strings.TranslationTimedOut,
            TranslationFailure.RateLimited => _strings.TranslationRateLimited,
            _ => _strings.TranslationFailed,
        };
        var retry = failure == TranslationFailure.RateLimited
            ? null
            : new StateCardAction(_strings.Retry, RetryTranslation);
        ShowCard(new StateCardOptions(
            PluginIcons.TranslateFilled,
            message,
            _strings.TranslationResultTitle,
            _strings.Close,
            DismissFailure,
            retry), TranslationCardKind.Failure);
        _transition(OverlayInteractionMode.TranslationResult);
    }

    internal bool HandleEscape()
    {
        if (_cardKind == TranslationCardKind.Consent)
        {
            DismissConsent();
            return true;
        }
        if (_cardKind == TranslationCardKind.Failure)
        {
            DismissFailure();
            return true;
        }
        if (IsTranslating)
        {
            CancelTranslation();
            return true;
        }
        if (IsTranslationShown)
        {
            DismissTranslation();
            return true;
        }
        return false;
    }

    internal void CancelForClosing()
    {
        if (!_closing) _profiler?.Mark("overlay_closing", _profileScope);
        _closing = true;
        _cachedImage = null;
        _cachedTarget = null;
        _requestedTarget = null;
        if (_requestId != Guid.Empty) _publish(new CancelScreenTranslation(_requestId));
        _requestId = Guid.Empty;
        AbortPendingCompletionRipple();
        StopLoading(immediate: true);
        ClearCardImmediately();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _action.Button.Click -= OnTranslate;
        CancelForClosing();
        _action.LoadingIndicator.Dispose();
    }

    private void OnTranslate(object sender, RoutedEventArgs e)
    {
        Toggle();
        e.Handled = true;
    }

    internal void Toggle()
    {
        if (_disposed || _closing) return;
        if (IsTranslationShown) DismissTranslation();
        else if (IsTranslating) CancelTranslation();
        else if (_cardKind != TranslationCardKind.None) return;
        else if (!_consentAccepted()) ShowConsent();
        else BeginTranslation();
    }

    private void ShowConsent()
    {
        _coordinateRoot.UpdateLayout();
        var availableWidth = _coordinateRoot.ActualWidth;
        var availableHeight = _coordinateRoot.ActualHeight;
        var cardWidth = availableWidth > 0 ? Math.Clamp(availableWidth - 16, 160, 340) : 340;
        var cardMaxHeight = availableHeight > 0
            ? Math.Max(112, availableHeight - _bottom.Stack.Margin.Bottom -
                _bottom.ActionSlot.ActualHeight - _bottom.ResultSlot.Margin.Bottom - 8)
            : (double?)null;
        ShowCard(new StateCardOptions(
            PluginIcons.TranslateFilled,
            _strings.TranslationConsentMessage,
            _strings.TranslationConsentTitle,
            _strings.ConsentCancel,
            DismissConsent,
            new StateCardAction(_strings.Continue, ContinueConsent),
            _strings.TranslationConsentTitle,
            cardWidth,
            cardMaxHeight), TranslationCardKind.Consent);
        _transition(OverlayInteractionMode.TranslationConsent);
        _stateCard?.PrimaryActionButton?.Focus();
    }

    private void ContinueConsent()
    {
        if (_disposed || _closing || _cardKind != TranslationCardKind.Consent) return;
        try { _acceptConsent(); }
        catch (Exception exception)
        {
            DismissConsent();
            _showToast(new ToastNotification(_strings.SavingFailed(exception.Message), ToastTone.Error));
            return;
        }
        BeginCardExit();
        BeginTranslation();
    }

    private void DismissConsent()
    {
        if (_cardKind != TranslationCardKind.Consent) return;
        BeginCardExit();
        _transition(OverlayInteractionMode.Selecting);
    }

    private void BeginTranslation(string? retryTarget = null)
    {
        _profiler?.Mark("translate_clicked", _profileScope);
        var target = retryTarget ?? _targetLanguageTag();
        if (retryTarget is null && _cachedImage is not null && string.Equals(_cachedTarget, target, StringComparison.OrdinalIgnoreCase))
        {
            SetActionVisual(_strings.ShowOriginal, PluginIcons.ShowOriginalFilled);
            DisplayImage(_cachedImage, _cachedTarget);
            _transition(OverlayInteractionMode.TranslationShown);
            _profiler?.Mark("cached_translation_shown", _profileScope);
            return;
        }
        _requestedTarget = target;
        _requestId = Guid.NewGuid();
        SetActionVisual(_strings.Translating, PluginIcons.TranslateFilled);
        TranslationActionVisualPresenter.SetTranslatingState(
            _action, translating: true, _lightTheme, _animationsEnabled());
        _transition(OverlayInteractionMode.Translating);
        _activity?.Dispose();
        _activity = _activityPresenter.ShowLoading(
            _strings.Translating,
            OverlayVisualResources.Frozen(PluginPalette.For(_lightTheme).Roles.Primary));
        if (string.IsNullOrWhiteSpace(target)) ShowFailure(_requestId, TranslationFailure.Service);
        else _publish(new ScreenTranslationRequested(_requestId,
            _region is { } bounds ? VisibleImage.Crop(_scopeOriginal, bounds) : _scopeOriginal, target));
    }

    private void CancelTranslation()
    {
        if (_requestId != Guid.Empty) _publish(new CancelScreenTranslation(_requestId));
        _requestId = Guid.Empty;
        SetActionVisual(_strings.Translate, PluginIcons.TranslateFilled);
        StopLoading();
        _transition(OverlayInteractionMode.Selecting);
    }

    private void DismissTranslation()
    {
        AbortPendingCompletionRipple();
        if (_imageShown)
        {
            _imageShown = false;
            _screenshot.Source = _scopeOriginal;
            _imageChanged?.Invoke(_scopeOriginal, null);
        }
        SetActionVisual(_strings.Translate, PluginIcons.TranslateFilled);
        _transition(OverlayInteractionMode.Selecting);
        _profiler?.Mark("original_shown", _profileScope);
    }

    private void RetryTranslation()
    {
        if (_disposed || _closing || _cardKind != TranslationCardKind.Failure) return;
        var target = _requestedTarget;
        BeginCardExit();
        BeginTranslation(target);
    }

    private void DismissFailure()
    {
        if (_cardKind != TranslationCardKind.Failure) return;
        BeginCardExit();
        _transition(OverlayInteractionMode.Selecting);
    }

    internal void DismissStateCard()
    {
        if (_cardKind != TranslationCardKind.None) BeginCardExit();
    }

    private void ShowCard(StateCardOptions options, TranslationCardKind kind)
    {
        ClearCardImmediately();
        var card = StateCardVisualFactory.Create(options, PluginPalette.For(_lightTheme).Card);
        _cardKind = kind;
        _stateCard = card;
        var host = _overlay.StateHost;
        _bottom.LayoutTransitions.Apply(() =>
        {
            host.Children.Add(card.Card);
            host.Visibility = Visibility.Visible;
            host.Opacity = 1;
            host.IsHitTestVisible = true;
            host.Margin = _bottom.ResultSlot.Margin;
            _bottom.Stack.Children.Insert(_bottom.Stack.Children.IndexOf(_bottom.ResultSlot), host);
        }, _animationsEnabled());
        StateCardTransitions.BeginEntrance(card.Card, _animationsEnabled());
        _cardRipples.AddRange(OverlayVisualResources.AttachControlRipples(host));
    }

    private void BeginCardExit()
    {
        if (_cardKind == TranslationCardKind.None) return;
        _cardKind = TranslationCardKind.None;
        DisposeCardRipples();
        _overlay.StateHost.IsHitTestVisible = false;
        var card = _stateCard;
        if (card is null) return;
        var generation = ++_cardGeneration;
        var animationsEnabled = _animationsEnabled();
        var exit = StateCardTransitions.BeginExit(card.Card, animationsEnabled, () =>
        {
            if (_disposed || generation != _cardGeneration || !ReferenceEquals(_stateCard, card)) return;
            _pendingCardExit?.Dispose();
            _pendingCardExit = null;
            _bottom.LayoutTransitions.Apply(RemoveCardHost, animationsEnabled);
        });
        if (!exit.IsCompleted && generation == _cardGeneration && ReferenceEquals(_stateCard, card))
            _pendingCardExit = exit;
        else exit.Dispose();
    }

    private void ClearCardImmediately()
    {
        ++_cardGeneration;
        _pendingCardExit?.Dispose();
        _pendingCardExit = null;
        DisposeCardRipples();
        if (_bottom.Stack.Children.Contains(_overlay.StateHost))
            _bottom.LayoutTransitions.Apply(RemoveCardHost, _animationsEnabled());
        else RemoveCardHost();
    }

    private void RemoveCardHost()
    {
        var host = _overlay.StateHost;
        host.Children.Clear();
        host.Visibility = Visibility.Collapsed;
        host.Opacity = 0;
        host.IsHitTestVisible = false;
        _bottom.Stack.Children.Remove(host);
        _stateCard = null;
        _cardKind = TranslationCardKind.None;
    }

    private void DisposeCardRipples()
    {
        foreach (var ripple in _cardRipples) ripple.Dispose();
        _cardRipples.Clear();
    }

    private void StopLoading(bool immediate = false)
    {
        TranslationActionVisualPresenter.SetTranslatingState(
            _action, translating: false, _lightTheme, _animationsEnabled());
        if (_activity is null) return;
        if (immediate)
        {
            _activity.Dispose();
            _activity = null;
        }
        else
        {
            _ = HideActivityAsync(_activity);
        }
    }

    private async Task HideActivityAsync(OverlayActivityPresenter.ActivityPresentation activity)
    {
        await activity.HideAsync();
        if (ReferenceEquals(_activity, activity)) _activity = null;
    }

    private void DisplayImage(BitmapSource image, string? target)
    {
        _screenshot.Source = image;
        _imageShown = true;
        _imageChanged?.Invoke(image, target);
    }

    private void SetActionVisual(string name, System.Windows.Media.Geometry geometry)
    {
        _action.Icon.Data = geometry;
        _action.Button.ToolTip = name;
        AutomationProperties.SetName(_action.Button, name);
    }

    private void QueueCompletionRipple()
    {
        if (!_animationsEnabled()) return;
        _completionRippleOperation = _coordinateRoot.Dispatcher.BeginInvoke(() =>
        {
            _completionRippleOperation = null;
            if (_disposed || !IsTranslationShown) return;
            _coordinateRoot.UpdateLayout();
            var origin = _action.Button.TransformToAncestor(_coordinateRoot).Transform(
                new Point(_action.Button.ActualWidth / 2, _action.Button.ActualHeight / 2));
            _effects.SceneRipples.Emit(new SceneRippleRequest(
                origin,
                SceneRipplePreset.TranslationComplete,
                1));
        }, DispatcherPriority.Loaded);
    }

    private void AbortPendingCompletionRipple()
    {
        if (_completionRippleOperation?.Status == DispatcherOperationStatus.Pending)
            _completionRippleOperation.Abort();
        _completionRippleOperation = null;
    }
}

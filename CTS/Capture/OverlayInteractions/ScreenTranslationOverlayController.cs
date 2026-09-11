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
    private readonly TranslationOverlayVisual _overlay;
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
    private readonly BitmapSource _originalImage;
    private readonly Action<BitmapSource, string?>? _imageChanged;
    private bool _imageShown;
    private BitmapSource? _cachedImage;
    private string? _cachedTarget;
    private string? _requestedTarget;
    private bool _closing;
    private readonly TranslationMemoryProfiler? _profiler;
    private readonly string _profileScope = Guid.NewGuid().ToString("N");

    internal ScreenTranslationOverlayController(
        TranslationActionVisual action,
        TranslationOverlayVisual overlay,
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
        _overlay = overlay;
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
        _screenshot = screenshot ?? throw new ArgumentNullException(nameof(screenshot));
        _originalImage = screenshot.Source as BitmapSource
            ?? throw new InvalidOperationException("The overlay screenshot source must be a bitmap.");
        _imageChanged = imageChanged;
        _profiler = profiler;
        _profiler?.Mark("overlay_open", _profileScope);
        _action.Button.Click += OnTranslate;
        _overlay.ContinueButton.Click += OnContinue;
        _overlay.CancelButton.Click += OnCancelConsent;
    }

    internal bool IsConsentOpen => _overlay.ConsentCard.Visibility == Visibility.Visible;
    internal bool IsTranslating => _requestId != Guid.Empty;
    internal bool IsTranslationShown => _imageShown;
    internal bool IsImageShown => _imageShown;
    internal bool HasPendingCompletionRipple =>
        _completionRippleOperation?.Status == DispatcherOperationStatus.Pending;

    internal void ShowResult(ScreenTranslationResult result)
    {
        if (_disposed || _closing || _requestId == Guid.Empty || result.RequestId != _requestId) return;
        var target = _requestedTarget
            ?? throw new InvalidOperationException("The translation target is missing for the active request.");
        AbortPendingCompletionRipple();
        _requestId = Guid.Empty;
        SetActionVisual(_strings.ShowOriginal, TextTranslationVisualFactory.ShowOriginalIconGeometry);
        StopLoading();
        var scaled = new TransformedBitmap(result.Image, new ScaleTransform(
            (double)_originalImage.PixelWidth / result.Image.PixelWidth,
            (double)_originalImage.PixelHeight / result.Image.PixelHeight));
        scaled.Freeze();
        _cachedImage = scaled;
        _cachedTarget = target;
        DisplayImage(scaled, _cachedTarget);
        _transition(OverlayInteractionMode.TranslationShown);
        _profiler?.Mark("translation_shown", _profileScope);
        QueueCompletionRipple();
    }

    internal void ShowFailure(Guid requestId, TranslationFailure failure)
    {
        if (_disposed || requestId != _requestId) return;
        _requestId = Guid.Empty;
        SetActionVisual(_strings.Translate, TextTranslationVisualFactory.TranslateIconGeometry);
        StopLoading();
        _transition(OverlayInteractionMode.Selecting);
        var message = failure switch
        {
            TranslationFailure.Network => _strings.TranslationNetworkError,
            TranslationFailure.Timeout => _strings.TranslationTimedOut,
            TranslationFailure.RateLimited => _strings.TranslationRateLimited,
            TranslationFailure.Canceled => null,
            _ => _strings.TranslationFailed,
        };
        if (message is not null) _showToast(new ToastNotification(message, ToastTone.Error));
    }

    internal bool HandleEscape()
    {
        if (IsConsentOpen)
        {
            CloseConsent();
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
        StopLoading();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _action.Button.Click -= OnTranslate;
        _overlay.ContinueButton.Click -= OnContinue;
        _overlay.CancelButton.Click -= OnCancelConsent;
        CancelForClosing();
        _action.LoadingIndicator.Dispose();
    }

    private void OnTranslate(object sender, RoutedEventArgs e)
    {
        if (_disposed || _closing) return;
        if (IsTranslationShown) DismissTranslation();
        else if (IsTranslating) CancelTranslation();
        else if (!_consentAccepted())
        {
            _overlay.ConsentCard.Visibility = Visibility.Visible;
            _transition(OverlayInteractionMode.TranslationConsent);
            _overlay.ContinueButton.Focus();
        }
        else BeginTranslation();
        e.Handled = true;
    }

    private void OnContinue(object sender, RoutedEventArgs e)
    {
        try { _acceptConsent(); }
        catch (Exception exception)
        {
            _showToast(new ToastNotification(_strings.SavingFailed(exception.Message), ToastTone.Error));
            CloseConsent();
            e.Handled = true;
            return;
        }
        _overlay.ConsentCard.Visibility = Visibility.Collapsed;
        BeginTranslation();
        e.Handled = true;
    }

    private void OnCancelConsent(object sender, RoutedEventArgs e)
    {
        CloseConsent();
        e.Handled = true;
    }

    private void BeginTranslation()
    {
        _profiler?.Mark("translate_clicked", _profileScope);
        _overlay.ConsentCard.Visibility = Visibility.Collapsed;
        var target = _targetLanguageTag();
        if (_cachedImage is not null && string.Equals(_cachedTarget, target, StringComparison.OrdinalIgnoreCase))
        {
            SetActionVisual(_strings.ShowOriginal, TextTranslationVisualFactory.ShowOriginalIconGeometry);
            DisplayImage(_cachedImage, _cachedTarget);
            _transition(OverlayInteractionMode.TranslationShown);
            _profiler?.Mark("cached_translation_shown", _profileScope);
            return;
        }
        _requestedTarget = target;
        _requestId = Guid.NewGuid();
        SetActionVisual(_strings.Translating, TextTranslationVisualFactory.TranslateIconGeometry);
        TranslationActionVisualPresenter.SetTranslatingState(
            _action, translating: true, _lightTheme, _animationsEnabled());
        _transition(OverlayInteractionMode.Translating);
        if (string.IsNullOrWhiteSpace(target)) ShowFailure(_requestId, TranslationFailure.Service);
        else _publish(new ScreenTranslationRequested(_requestId, _originalImage, target));
    }

    private void CancelTranslation()
    {
        if (_requestId != Guid.Empty) _publish(new CancelScreenTranslation(_requestId));
        _requestId = Guid.Empty;
        SetActionVisual(_strings.Translate, TextTranslationVisualFactory.TranslateIconGeometry);
        StopLoading();
        _transition(OverlayInteractionMode.Selecting);
    }

    private void DismissTranslation()
    {
        AbortPendingCompletionRipple();
        if (_imageShown)
        {
            _imageShown = false;
            _screenshot.Source = _originalImage;
            _imageChanged?.Invoke(_originalImage, null);
        }
        SetActionVisual(_strings.Translate, TextTranslationVisualFactory.TranslateIconGeometry);
        _transition(OverlayInteractionMode.Selecting);
        _profiler?.Mark("original_shown", _profileScope);
    }

    private void CloseConsent()
    {
        _overlay.ConsentCard.Visibility = Visibility.Collapsed;
        _transition(OverlayInteractionMode.Selecting);
    }

    private void StopLoading()
    {
        TranslationActionVisualPresenter.SetTranslatingState(
            _action, translating: false, _lightTheme, _animationsEnabled());
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

using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CircleToSearch.TextRecognition;
using CircleToSearch.Ui;
using CircleToSearch.Ui.Effects;

namespace CircleToSearch.Capture.OverlayInteractions;

internal sealed class OverlayImageTextCoordinator : IDisposable
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(300);
    private readonly PointerGestureRouter _pointer;
    private readonly TextSelectionOverlayController _textSelection;
    private readonly OcrOverlayController _ocr;
    private readonly TextBlock? _prompt;
    private readonly UiStrings _strings;
    private readonly OcrLanguageCatalog? _catalog;
    private readonly ToastOverlayController? _toast;
    private readonly OverlayActivityPresenter? _activityPresenter;
    private readonly OverlayEffectsVisual? _effects;
    private readonly FrameworkElement? _root;
    private readonly bool _lightTheme;
    private readonly DispatcherTimer _timer;
    private readonly Func<DateTime> _now;
    private readonly Action<TimeSpan, Action>? _scheduleDelay;
    private readonly Action? _restoreActionTray;
    private BitmapSource? _image;
    private string? _inputTag;
    private string? _effectiveTag;
    private string? _requestedTag;
    private DateTime _dueAt;
    private long _revision;
    private long _runningRevision;
    private bool _available = true;
    private bool _switchFeedback;
    private bool _disposed;
    private OverlayActivityPresenter.ActivityPresentation? _activity;

    internal OverlayImageTextCoordinator(
        PointerGestureRouter pointer, TextSelectionOverlayController textSelection,
        OcrOverlayController ocr, TextBlock? prompt, UiStrings strings, string? initialInputTag,
        BitmapSource? initialImage = null, OcrLanguageCatalog? catalog = null,
        ToastOverlayController? toast = null, OverlayActivityPresenter? activityPresenter = null,
        OverlayEffectsVisual? effects = null, FrameworkElement? root = null, bool lightTheme = false,
        Func<DateTime>? now = null, Action<TimeSpan, Action>? scheduleDelay = null,
        Action? restoreActionTray = null)
    {
        _pointer = pointer;
        _textSelection = textSelection;
        _ocr = ocr;
        _prompt = prompt;
        _strings = strings;
        _inputTag = initialInputTag;
        _image = initialImage;
        _catalog = catalog;
        _toast = toast;
        _activityPresenter = activityPresenter;
        _effects = effects;
        _root = root;
        _lightTheme = lightTheme;
        _now = now ?? (() => DateTime.UtcNow);
        _scheduleDelay = scheduleDelay;
        _restoreActionTray = restoreActionTray;
        _effectiveTag = Resolve(initialInputTag);
        _requestedTag = initialInputTag;
        _timer = new DispatcherTimer(DispatcherPriority.Normal, root?.Dispatcher ?? Dispatcher.CurrentDispatcher);
        _timer.Tick += OnTimer;
    }

    internal void Start()
    {
        if (_disposed || _image is null) return;
        if (_effectiveTag is null) { ShowUnavailable(); return; }
        _runningRevision = _revision;
        _ocr.Restart(_image, _effectiveTag);
    }

    internal void SetInitialInputLanguage(string? tag)
    {
        if (_disposed || _revision != 0 || _inputTag is not null || tag is null) return;
        _inputTag = tag;
        _requestedTag = tag;
        _effectiveTag = Resolve(tag);
    }

    internal void OnInputLanguageChanged(string? tag)
    {
        if (_disposed) return;
        var effective = Resolve(tag);
        if (string.Equals(_effectiveTag, effective, StringComparison.OrdinalIgnoreCase) &&
            (effective is not null || string.Equals(_requestedTag, tag, StringComparison.OrdinalIgnoreCase)))
        {
            _inputTag = tag;
            return;
        }
        _inputTag = tag;
        _requestedTag = tag;
        _effectiveTag = effective;
        Invalidate();
        _switchFeedback = true;
        _dueAt = _now() + Debounce;
        if (!_available) return;
        if (effective is null) { ShowUnavailable(); return; }
        ShowLanguageFeedback();
        Schedule();
    }

    internal void OnImageChanged(BitmapSource image, string? language)
    {
        if (_disposed) return;
        _image = image;
        _requestedTag = language ?? _inputTag;
        _effectiveTag = Resolve(_requestedTag);
        Invalidate();
        _switchFeedback = false;
        if (_prompt is not null)
            _prompt.Text = language is null ? _strings.SelectionPrompt : _strings.TranslatedTextPrompt;
        if (!_available) return;
        if (_effectiveTag is null) ShowUnavailable();
        else
        {
            _runningRevision = _revision;
            _ocr.Restart(image, _effectiveTag);
        }
    }

    internal void SetAvailable(bool available)
    {
        if (_disposed || _available == available) return;
        _available = available;
        Invalidate();
        if (!available) return;
        if (_effectiveTag is null) { ShowUnavailable(); return; }
        if (_switchFeedback)
        {
            ShowLanguageFeedback();
            Schedule();
        }
        else if (_image is not null)
        {
            _runningRevision = _revision;
            _ocr.Restart(_image, _effectiveTag);
        }
    }

    internal void OnCompleted(OcrRecognitionOutcome outcome)
    {
        if (_disposed || !_available || _runningRevision != _revision) return;
        if (outcome.Status == OcrRecognitionStatus.Canceled) return;
        _textSelection.SetDocument(outcome.Status == OcrRecognitionStatus.Success ? outcome.Document : null);
        if (!_switchFeedback) return;
        var activity = _activity;
        _activity = null;
        _ = CompleteFeedbackAsync(outcome.Status, _revision, activity);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Invalidate();
        _timer.Tick -= OnTimer;
    }

    private void Invalidate()
    {
        ++_revision;
        _timer.Stop();
        _ocr.Invalidate();
        var canceledGesture = _pointer.Cancel();
        if (_available && !_disposed && canceledGesture == ActivePointerGesture.Lasso)
            _restoreActionTray?.Invoke();
        _textSelection.SetDocument(null);
        _activity?.Dispose();
        _activity = null;
    }

    private void Schedule()
    {
        if (_image is null || _effectiveTag is null) return;
        var remaining = _dueAt - _now();
        if (remaining <= TimeSpan.Zero) { RunPending(); return; }
        if (_scheduleDelay is not null)
        {
            var revision = _revision;
            _scheduleDelay(remaining, () =>
            {
                if (!_disposed && revision == _revision) RunPending();
            });
            return;
        }
        _timer.Interval = remaining;
        _timer.Start();
    }

    private void OnTimer(object? sender, EventArgs e)
    {
        _timer.Stop();
        RunPending();
    }

    private void RunPending()
    {
        if (_disposed || !_available || _image is null || _effectiveTag is null) return;
        _runningRevision = _revision;
        if (_switchFeedback && _activityPresenter is not null)
            _activity = _activityPresenter.ShowLoading(_strings.OcrProcessing,
                OverlayVisualResources.Frozen(PluginPalette.For(_lightTheme).MusicOverlay.Primary));
        _ocr.Restart(_image, _effectiveTag);
    }

    private async Task CompleteFeedbackAsync(OcrRecognitionStatus status, long revision,
        OverlayActivityPresenter.ActivityPresentation? activity)
    {
        try
        {
            if (activity is not null) await activity.HideAsync();
            if (_disposed || !_available || revision != _revision) return;
            _switchFeedback = false;
            switch (status)
            {
                case OcrRecognitionStatus.Success:
                    if (_root is not null && _effects is not null)
                        _effects.SceneRipples.Emit(new SceneRippleRequest(
                            new Point(_root.ActualWidth / 2, _root.ActualHeight / 2),
                            SceneRipplePreset.OcrComplete, 1));
                    break;
                case OcrRecognitionStatus.NoText:
                    _toast?.Show(new ToastNotification(_strings.OcrNoText, ToastTone.Neutral));
                    break;
                case OcrRecognitionStatus.LanguageUnavailable:
                    ShowUnavailable();
                    break;
                case OcrRecognitionStatus.PlatformUnavailable:
                    _toast?.Show(new ToastNotification(_strings.OcrPlatformUnavailable, ToastTone.Error));
                    break;
                default:
                    _toast?.Show(new ToastNotification(_strings.OcrFailed, ToastTone.Error));
                    break;
            }
        }
        finally { activity?.Dispose(); }
    }

    private void ShowUnavailable()
    {
        if (!_available) return;
        var message = string.IsNullOrWhiteSpace(_requestedTag)
            ? _strings.OcrUnknownLanguage
            : _strings.OcrLanguageUnavailable(DisplayName(_requestedTag));
        _toast?.ShowOrUpdate("ocr-language", new ToastNotification(message, ToastTone.Error));
    }

    private void ShowLanguageFeedback()
    {
        if (_effectiveTag is null) return;
        _toast?.ShowOrUpdate("ocr-language", new ToastNotification(
            _strings.OcrLanguageChanged(DisplayName(_effectiveTag)), ToastTone.Neutral));
    }

    private string? Resolve(string? tag) => _catalog is null ? tag : _catalog.Resolve(tag)?.Tag;

    private string DisplayName(string tag)
    {
        var option = _catalog?.AvailableLanguages.FirstOrDefault(language =>
            string.Equals(language.Tag, tag, StringComparison.OrdinalIgnoreCase));
        if (option is not null) return option.DisplayName;
        try { return CultureInfo.GetCultureInfo(tag).NativeName; }
        catch (CultureNotFoundException) { return tag; }
    }
}

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using CircleToSearch.TextRecognition;
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
    private readonly OverlayCoordinateMapper _mapper;
    private readonly UiStrings _strings;
    private readonly Func<bool> _consentAccepted;
    private readonly Action _acceptConsent;
    private readonly Func<string> _targetLanguageTag;
    private readonly Action<IOverlayCommand> _publish;
    private readonly Action<OverlayInteractionMode> _transition;
    private readonly Action<ToastNotification> _showToast;
    private readonly Func<bool> _animationsEnabled;
    private readonly bool _lightTheme;
    private OcrRecognitionOutcome? _ocr;
    private Guid _requestId;
    private bool _waitingForOcr;
    private DispatcherOperation? _completionRippleOperation;
    private bool _disposed;
    private readonly Image? _screenshot;
    private readonly BitmapSource? _originalImage;
    private readonly Action<BitmapSource, string?>? _imageChanged;
    private bool _imageShown;

    internal ScreenTranslationOverlayController(
        TranslationActionVisual action,
        TranslationOverlayVisual overlay,
        OverlayEffectsVisual effects,
        FrameworkElement coordinateRoot,
        OverlayCoordinateMapper mapper,
        UiStrings strings,
        Func<bool> consentAccepted,
        Action acceptConsent,
        Func<string> targetLanguageTag,
        Action<IOverlayCommand> publish,
        Action<OverlayInteractionMode> transition,
        Action<ToastNotification> showToast,
        Func<bool> animationsEnabled,
        bool lightTheme,
        Image? screenshot = null,
        Action<BitmapSource, string?>? imageChanged = null)
    {
        _action = action;
        _overlay = overlay;
        _effects = effects;
        _coordinateRoot = coordinateRoot;
        _mapper = mapper;
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
        _originalImage = screenshot?.Source as BitmapSource;
        _imageChanged = imageChanged;
        _action.Button.Click += OnTranslate;
        _overlay.ContinueButton.Click += OnContinue;
        _overlay.CancelButton.Click += OnCancelConsent;
    }

    internal bool IsConsentOpen => _overlay.ConsentCard.Visibility == Visibility.Visible;
    internal bool IsTranslating => _requestId != Guid.Empty;
    internal bool IsTranslationShown => _imageShown || (_overlay.CardsLayer.Visibility == Visibility.Visible && _overlay.CardsLayer.Children.Count > 0);
    internal bool IsImageShown => _imageShown;
    internal bool HasPendingCompletionRipple =>
        _completionRippleOperation?.Status == DispatcherOperationStatus.Pending;

    internal void SetOcrOutcome(OcrRecognitionOutcome outcome)
    {
        _ocr = outcome;
        if (_waitingForOcr) TryPublishTranslation();
    }

    internal void ShowResult(ScreenTranslationResult result)
    {
        if (_disposed || result.RequestId != _requestId) return;
        AbortPendingCompletionRipple();
        _requestId = Guid.Empty;
        _waitingForOcr = false;
        SetActionVisual(_strings.ShowOriginal, TextTranslationVisualFactory.ShowOriginalIconGeometry);
        StopLoading();
        if (result.Image is { } image && _screenshot is not null && _originalImage is not null)
        {
            var scaled = new TransformedBitmap(image, new ScaleTransform(
                (double)_originalImage.PixelWidth / image.PixelWidth, (double)_originalImage.PixelHeight / image.PixelHeight));
            scaled.Freeze();
            _screenshot.Source = scaled;
            _imageShown = true;
            _imageChanged?.Invoke(scaled, result.TargetLanguageTag);
        }
        else Render(result);
        _transition(OverlayInteractionMode.TranslationShown);
        QueueCompletionRipple();
        if (result.IsPartial)
            _showToast(new ToastNotification(_strings.TranslationPartial, ToastTone.Error));
    }

    internal void ShowFailure(Guid requestId, TranslationFailure failure)
    {
        if (_disposed || requestId != _requestId) return;
        _requestId = Guid.Empty;
        _waitingForOcr = false;
        SetActionVisual(_strings.Translate, TextTranslationVisualFactory.TranslateIconGeometry);
        StopLoading();
        _transition(OverlayInteractionMode.Selecting);
        var message = failure switch
        {
            TranslationFailure.SameLanguage => _strings.ScreenAlreadyTargetLanguage,
            TranslationFailure.Network => _strings.TranslationNetworkError,
            TranslationFailure.Timeout => _strings.TranslationTimedOut,
            TranslationFailure.RateLimited => _strings.TranslationRateLimited,
            TranslationFailure.NoText => _strings.OcrNoText,
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
        if (_requestId != Guid.Empty) _publish(new CancelScreenTranslation(_requestId));
        _requestId = Guid.Empty;
        _waitingForOcr = false;
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
        _overlay.CardsLayer.Children.Clear();
    }

    private void OnTranslate(object sender, RoutedEventArgs e)
    {
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
        _overlay.ConsentCard.Visibility = Visibility.Collapsed;
        _requestId = Guid.NewGuid();
        _waitingForOcr = true;
        SetActionVisual(_strings.Translating, TextTranslationVisualFactory.TranslateIconGeometry);
        TranslationActionVisualPresenter.SetTranslatingState(
            _action, translating: true, _lightTheme, _animationsEnabled());
        _transition(OverlayInteractionMode.Translating);
        if (_originalImage is not null)
        {
            _waitingForOcr = false;
            var target = _targetLanguageTag();
            if (string.IsNullOrWhiteSpace(target)) ShowFailure(_requestId, TranslationFailure.Service);
            else _publish(new ScreenTranslationRequested(_requestId, _originalImage, target));
            return;
        }
        TryPublishTranslation();
    }

    private void TryPublishTranslation()
    {
        if (!_waitingForOcr || _requestId == Guid.Empty || _ocr is null) return;
        _waitingForOcr = false;
        if (_ocr.Status == OcrRecognitionStatus.Success && _ocr.Document is { } document)
        {
            var targetLanguage = _targetLanguageTag();
            if (string.IsNullOrWhiteSpace(targetLanguage))
            {
                _requestId = Guid.Empty;
                SetActionVisual(_strings.Translate, TextTranslationVisualFactory.TranslateIconGeometry);
                StopLoading();
                _transition(OverlayInteractionMode.Selecting);
                _showToast(new ToastNotification(_strings.TranslationFailed, ToastTone.Error));
                return;
            }
            _publish(new ScreenTranslationRequested(_requestId, document, targetLanguage));
            return;
        }
        var message = _ocr.Status switch
        {
            OcrRecognitionStatus.NoText => _strings.OcrNoText,
            OcrRecognitionStatus.LanguageUnavailable => _strings.OcrLanguageUnavailable,
            _ => _strings.OcrFailed,
        };
        _requestId = Guid.Empty;
        SetActionVisual(_strings.Translate, TextTranslationVisualFactory.TranslateIconGeometry);
        StopLoading();
        _transition(OverlayInteractionMode.Selecting);
        _showToast(new ToastNotification(message, ToastTone.Error));
    }

    private void CancelTranslation()
    {
        if (_requestId != Guid.Empty) _publish(new CancelScreenTranslation(_requestId));
        _requestId = Guid.Empty;
        _waitingForOcr = false;
        SetActionVisual(_strings.Translate, TextTranslationVisualFactory.TranslateIconGeometry);
        StopLoading();
        _transition(OverlayInteractionMode.Selecting);
    }

    private void DismissTranslation()
    {
        AbortPendingCompletionRipple();
        if (_imageShown && _screenshot is not null && _originalImage is not null)
        {
            _imageShown = false;
            _screenshot.Source = _originalImage;
            _imageChanged?.Invoke(_originalImage, null);
        }
        _overlay.CardsLayer.Children.Clear();
        _overlay.CardsLayer.Visibility = Visibility.Collapsed;
        SetActionVisual(_strings.Translate, TextTranslationVisualFactory.TranslateIconGeometry);
        _transition(OverlayInteractionMode.Selecting);
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

    private void Render(ScreenTranslationResult result)
    {
        _overlay.CardsLayer.Children.Clear();
        var palette = PluginPalette.For(_lightTheme).Translation;
        var cards = new List<(Border Card, Rect Source, Size Desired)>();
        foreach (var line in result.Lines.OrderBy(line => line.SourceBoundsPx.Top).ThenBy(line => line.SourceBoundsPx.Left))
        {
            var source = _mapper.ToDips(line.SourceBoundsPx);
            var dim = new Rectangle
            {
                Width = source.Width,
                Height = source.Height,
                Fill = OverlayVisualResources.Frozen(palette.SourceDim),
            };
            Canvas.SetLeft(dim, source.Left);
            Canvas.SetTop(dim, source.Top);
            _overlay.CardsLayer.Children.Add(dim);

            var text = new TextBlock
            {
                Text = line.TranslatedText,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = OverlayVisualResources.Font,
                FontSize = 14,
                Foreground = OverlayVisualResources.Frozen(palette.CardText),
                MaxWidth = Math.Max(80, Math.Min(Math.Max(source.Width, 320), _coordinateRoot.ActualWidth - 32)),
            };
            var card = new Border
            {
                Child = text,
                Background = OverlayVisualResources.Frozen(palette.CardSurface),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(6, 4, 6, 4),
            };
            card.Measure(new Size(text.MaxWidth + 12, double.PositiveInfinity));
            cards.Add((card, source, card.DesiredSize));
        }
        var placements = TranslationCardLayout.Place(
            cards.Select(card => (card.Source, card.Desired)).ToArray(),
            new Size(_coordinateRoot.ActualWidth, _coordinateRoot.ActualHeight));
        for (var index = 0; index < cards.Count; index++)
        {
            var placement = placements[index];
            cards[index].Card.Width = placement.Width;
            Canvas.SetLeft(cards[index].Card, placement.Left);
            Canvas.SetTop(cards[index].Card, placement.Top);
            _overlay.CardsLayer.Children.Add(cards[index].Card);
        }
        _overlay.CardsLayer.Visibility = Visibility.Visible;
    }
}

namespace CircleToSearch.Capture.OverlayInteractions;

using System.Windows;
using System.Windows.Media.Animation;
using CircleToSearch.Ocr;
using CircleToSearch.Translation;
using CircleToSearch.Ui;
using GdiBitmap = System.Drawing.Bitmap;

internal sealed class TranslationOverlayController : IDisposable
{
    private readonly TranslationOverlayVisual _visual;
    private readonly GdiBitmap _frame;
    private readonly double _scale;
    private readonly ITranslationService _translationService;
    private readonly UiStrings _strings;
    private readonly Action<string> _setClipboard;
    private readonly Action<ToastNotification> _showToast;
    private readonly Func<OverlayInteractionMode> _getMode;
    private readonly Action<OverlayInteractionMode> _setMode;

    private OcrScreenSnapshot? _snapshot;
    private Task<OcrScreenSnapshot>? _pendingOcrTask;
    private CancellationTokenSource? _translationCts;
    private bool _isTranslating;
    private bool _isTranslationActive;
    private bool _showingOriginal;
    private bool _disposed;

    public bool IsActive => _isTranslationActive || _isTranslating;

    public TranslationOverlayController(
        TranslationOverlayVisual visual,
        GdiBitmap frame,
        double scale,
        ITranslationService translationService,
        UiStrings strings,
        Action<string> setClipboard,
        Action<ToastNotification> showToast,
        Func<OverlayInteractionMode> getMode,
        Action<OverlayInteractionMode> setMode)
    {
        _visual = visual ?? throw new ArgumentNullException(nameof(visual));
        _frame = frame ?? throw new ArgumentNullException(nameof(frame));
        _scale = scale;
        _translationService = translationService ?? throw new ArgumentNullException(nameof(translationService));
        _strings = strings ?? throw new ArgumentNullException(nameof(strings));
        _setClipboard = setClipboard ?? throw new ArgumentNullException(nameof(setClipboard));
        _showToast = showToast ?? throw new ArgumentNullException(nameof(showToast));
        _getMode = getMode ?? throw new ArgumentNullException(nameof(getMode));
        _setMode = setMode ?? throw new ArgumentNullException(nameof(setMode));

        _visual.TranslateButton.Click += OnTranslateButtonClicked;
        _visual.ToggleButton.Click += OnToggleButtonClicked;
        _visual.ClosePillButton.Click += OnClosePillButtonClicked;
    }

    public void SetSnapshot(OcrScreenSnapshot snapshot)
    {
        _snapshot = snapshot;
    }

    public void SetPendingOcrTask(Task<OcrScreenSnapshot> task)
    {
        _pendingOcrTask = task;
    }

    public void ToggleTranslation()
    {
        if (_isTranslationActive)
        {
            ExitTranslation();
        }
        else
        {
            _ = StartTranslationAsync();
        }
    }

    public async Task StartTranslationAsync()
    {
        if (_disposed || _isTranslating) return;
        _isTranslating = true;
        _visual.SetLoading(true);

        _translationCts?.Cancel();
        _translationCts?.Dispose();
        _translationCts = new CancellationTokenSource();
        var ct = _translationCts.Token;

        try
        {
            if (_snapshot is null && _pendingOcrTask is not null)
            {
                _snapshot = await _pendingOcrTask.ConfigureAwait(true);
            }

            if (_snapshot is null || _snapshot.Lines.Count == 0)
            {
                if (!_disposed)
                {
                    _visual.SetLoading(false);
                    _showToast(new ToastNotification(_strings.NoTextDetected, ToastTone.Neutral));
                }
                _isTranslating = false;
                return;
            }

            var blocks = await _translationService.TranslateScreenAsync(
                _snapshot.Lines,
                _frame,
                _scale,
                string.Empty,
                ct).ConfigureAwait(true);

            if (_disposed || ct.IsCancellationRequested) return;

            if (blocks.Count == 0)
            {
                _visual.SetLoading(false);
                _showToast(new ToastNotification(_strings.TranslationFailed, ToastTone.Error));
                _isTranslating = false;
                return;
            }

            _visual.PopulateCards(blocks, text =>
            {
                try
                {
                    _setClipboard(text);
                    _showToast(new ToastNotification(_strings.TextCopied, ToastTone.Success));
                }
                catch
                {
                }
            });

            _visual.CardsLayer.Visibility = Visibility.Visible;
            if (OverlayVisualResources.AnimationsEnabled())
            {
                var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                };
                _visual.CardsLayer.BeginAnimation(UIElement.OpacityProperty, fade);
            }
            else
            {
                _visual.CardsLayer.Opacity = 1;
            }

            _visual.TogglePill.Visibility = Visibility.Visible;
            _visual.ToggleButtonText.Text = _strings.ShowOriginal;
            _isTranslationActive = true;
            _showingOriginal = false;
            _setMode(OverlayInteractionMode.Translation);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            if (!_disposed)
            {
                _showToast(new ToastNotification(_strings.TranslationFailed, ToastTone.Error));
            }
        }
        finally
        {
            if (!_disposed)
            {
                _visual.SetLoading(false);
            }
            _isTranslating = false;
        }
    }

    public void ToggleOriginal()
    {
        if (!_isTranslationActive || _disposed) return;
        _showingOriginal = !_showingOriginal;

        var targetOpacity = _showingOriginal ? 0d : 1d;
        _visual.ToggleButtonText.Text = _showingOriginal ? _strings.ShowTranslation : _strings.ShowOriginal;

        if (OverlayVisualResources.AnimationsEnabled())
        {
            var anim = new DoubleAnimation(_visual.CardsLayer.Opacity, targetOpacity, TimeSpan.FromMilliseconds(120))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            _visual.CardsLayer.BeginAnimation(UIElement.OpacityProperty, anim);
        }
        else
        {
            _visual.CardsLayer.Opacity = targetOpacity;
        }
    }

    public void ExitTranslation()
    {
        if (_disposed) return;
        _translationCts?.Cancel();
        _isTranslating = false;
        _isTranslationActive = false;
        _showingOriginal = false;
        _visual.SetLoading(false);
        _visual.CardsLayer.BeginAnimation(UIElement.OpacityProperty, null);
        _visual.CardsLayer.Opacity = 0;
        _visual.CardsLayer.Visibility = Visibility.Collapsed;
        _visual.TogglePill.Visibility = Visibility.Collapsed;
        if (_getMode() == OverlayInteractionMode.Translation)
        {
            _setMode(OverlayInteractionMode.Selecting);
        }
    }

    private void OnTranslateButtonClicked(object sender, RoutedEventArgs e) => ToggleTranslation();

    private void OnToggleButtonClicked(object sender, RoutedEventArgs e) => ToggleOriginal();

    private void OnClosePillButtonClicked(object sender, RoutedEventArgs e) => ExitTranslation();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _visual.TranslateButton.Click -= OnTranslateButtonClicked;
        _visual.ToggleButton.Click -= OnToggleButtonClicked;
        _visual.ClosePillButton.Click -= OnClosePillButtonClicked;
        ExitTranslation();
        _translationCts?.Dispose();
    }
}

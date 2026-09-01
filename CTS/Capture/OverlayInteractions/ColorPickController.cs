namespace CircleToSearch.Capture.OverlayInteractions;

using System.Windows.Threading;
using CircleToSearch.Ui;
using GdiBitmap = System.Drawing.Bitmap;
using GdiPoint = System.Drawing.Point;

internal sealed class ColorPickController : IDisposable
{
    internal static readonly TimeSpan ConfirmationDuration = TimeSpan.FromMilliseconds(800);

    private readonly GdiBitmap _frame;
    private readonly Action<string> _setClipboard;
    private readonly UiStrings _strings;
    private readonly Action<ToastNotification> _showToast;
    private readonly Action _confirmationStarted;
    private readonly Action _pickFailed;
    private readonly Action _confirmationCompleted;
    private DispatcherTimer? _confirmationTimer;
    private bool _confirmationActive;
    private bool _disposed;

    internal ColorPickController(
        GdiBitmap frame,
        Action<string> setClipboard,
        UiStrings strings,
        Action<ToastNotification> showToast,
        Action confirmationStarted,
        Action pickFailed,
        Action confirmationCompleted)
    {
        _frame = frame ?? throw new ArgumentNullException(nameof(frame));
        _setClipboard = setClipboard ?? throw new ArgumentNullException(nameof(setClipboard));
        _strings = strings ?? throw new ArgumentNullException(nameof(strings));
        _showToast = showToast ?? throw new ArgumentNullException(nameof(showToast));
        _confirmationStarted = confirmationStarted ?? throw new ArgumentNullException(nameof(confirmationStarted));
        _pickFailed = pickFailed ?? throw new ArgumentNullException(nameof(pickFailed));
        _confirmationCompleted = confirmationCompleted ?? throw new ArgumentNullException(nameof(confirmationCompleted));
    }

    internal bool HasPendingConfirmation => _confirmationTimer is not null;

    internal void Pick(GdiPoint point)
    {
        if (_disposed || _confirmationActive) return;

        ToastColorSample sample;
        try
        {
            var x = Math.Clamp(point.X, 0, _frame.Width - 1);
            var y = Math.Clamp(point.Y, 0, _frame.Height - 1);
            var color = _frame.GetPixel(x, y);
            sample = new ToastColorSample(color.R, color.G, color.B);
            _setClipboard(sample.HexCode);
        }
        catch (Exception)
        {
            _pickFailed();
            if (!_disposed)
                _showToast(new ToastNotification(_strings.ColorCopyFailed, ToastTone.Error));
            return;
        }

        _confirmationActive = true;
        _confirmationStarted();
        if (_disposed) return;
        _showToast(new ToastNotification(_strings.ColorCopied, ToastTone.Success, sample));
        StartConfirmationTimer();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _confirmationActive = false;
        StopConfirmationTimer();
    }

    private void StartConfirmationTimer()
    {
        StopConfirmationTimer();
        _confirmationTimer = new DispatcherTimer { Interval = ConfirmationDuration };
        _confirmationTimer.Tick += OnConfirmationElapsed;
        _confirmationTimer.Start();
    }

    private void OnConfirmationElapsed(object? sender, EventArgs e)
    {
        StopConfirmationTimer();
        if (_disposed || !_confirmationActive) return;
        _confirmationActive = false;
        _confirmationCompleted();
    }

    private void StopConfirmationTimer()
    {
        if (_confirmationTimer is null) return;
        _confirmationTimer.Stop();
        _confirmationTimer.Tick -= OnConfirmationElapsed;
        _confirmationTimer = null;
    }
}

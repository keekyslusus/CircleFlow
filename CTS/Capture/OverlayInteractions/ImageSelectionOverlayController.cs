using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CircleToSearch.Ui;
using CircleToSearch.Ui.Effects;
using GdiRectangle = System.Drawing.Rectangle;

namespace CircleToSearch.Capture.OverlayInteractions;

internal sealed class ImageSelectionOverlayController : IDisposable
{
    internal static readonly TimeSpan CopyFeedbackDuration = TimeSpan.FromMilliseconds(800);
    private readonly ImageSelectionVisual _visual;
    private readonly FrameworkElement _root;
    private readonly OverlayCoordinateMapper _mapper;
    private readonly SelectionOverlayController _selection;
    private readonly ScreenTranslationOverlayController _translation;
    private readonly ClipboardCopyService _clipboard;
    private readonly Func<GdiRectangle, BitmapSource> _cropImage;
    private readonly Func<GdiRectangle, SelectionOutcome> _createSelection;
    private readonly Action<GdiRectangle> _search;
    private readonly Action<IOverlayCommand> _publish;
    private readonly Action _close;
    private readonly UiStrings _strings;
    private readonly DispatcherTimer _copyTimer;
    private readonly List<IDisposable> _ripples;
    private readonly Action? _askPromptOpened;
    private readonly Action? _askPromptClosed;
    private readonly Func<Rect, Rect> _toViewport;
    private readonly Action? _wholeScreenSelected;
    private bool _askDraftStarted;
    private bool _askImageAttached;
    private bool _disposed;

    internal ImageSelectionOverlayController(ImageSelectionVisual visual, FrameworkElement root,
        OverlayCoordinateMapper mapper, SelectionOverlayController selection,
        ScreenTranslationOverlayController translation, ClipboardCopyService clipboard,
        Func<GdiRectangle, BitmapSource> cropImage, Func<GdiRectangle, SelectionOutcome> createSelection,
        Action<GdiRectangle> search, Action<IOverlayCommand> publish, Action close, UiStrings strings,
        SelectionToolbarAction hiddenActions = SelectionToolbarAction.None,
        Action? askPromptOpened = null, Action? askPromptClosed = null, Func<Rect, Rect>? toViewport = null,
        Action? wholeScreenSelected = null)
    {
        _wholeScreenSelected = wholeScreenSelected;
        _toViewport = toViewport ?? (rect => rect);
        _askPromptOpened = askPromptOpened;
        _askPromptClosed = askPromptClosed;
        _visual = visual;
        _root = root;
        _mapper = mapper;
        _selection = selection;
        _translation = translation;
        _clipboard = clipboard;
        _cropImage = cropImage;
        _createSelection = createSelection;
        _search = search;
        _publish = publish;
        _close = close;
        _strings = strings;
        _copyTimer = new DispatcherTimer(DispatcherPriority.Normal, root.Dispatcher)
        { Interval = CopyFeedbackDuration };
        _copyTimer.Tick += OnCopyFeedbackCompleted;
        foreach (var (action, button) in new[]
        {
            (SelectionToolbarAction.Ask, visual.AskButton), (SelectionToolbarAction.Copy, visual.CopyButton),
            (SelectionToolbarAction.Save, visual.SaveButton), (SelectionToolbarAction.Translate, visual.TranslateButton),
        })
            button.Visibility = hiddenActions.HasFlag(action) ? Visibility.Collapsed : Visibility.Visible;
        visual.SearchButton.Click += OnSearch;
        visual.CopyButton.Click += OnCopy;
        visual.SaveButton.Click += OnSave;
        visual.TranslateButton.Click += OnTranslate;
        visual.AskButton.Click += OnAsk;
        visual.AskPrompt.SendButton.Click += OnSend;
        visual.AskPrompt.Input.KeyDown += OnPromptKeyDown;
        visual.AskPrompt.Input.TextChanged += OnPromptTextChanged;
        if (visual.AskPrompt.Attachment is { } attachment) attachment.Root.Click += OnToggleScreen;
        _ripples = [.. OverlayVisualResources.AttachControlRipples(visual.Toolbar.Layer),
            ControlRippleHost.Attach(visual.AskPrompt.SendButton)];
    }

    internal GdiRectangle? Bounds { get; private set; }
    internal bool IsCompleting { get; private set; }
    internal bool IsPromptOpen => _visual.Toolbar.IsPromptOpen;
    internal bool CanAsk => !_disposed && _visual.AskButton.Visibility == Visibility.Visible;
    // The question was typed over the bare screen, so it goes alone until the user adds the screen to it.
    internal bool IsBareQuestion { get; private set; }

    // Opens the question without a selection, where the actions of a whole-screen selection would stand.
    internal bool TryStartQuestion(string text)
    {
        if (!CanAsk || IsCompleting || Bounds is not null || _visual.Toolbar.IsOpen || !CanType(text)) return false;
        IsBareQuestion = true;
        ShowToolbar(_mapper.CaptureBounds);
        OpenPrompt();
        return TryTypeQuestion(text);
    }

    internal void Select(GdiRectangle bounds)
    {
        if (_disposed) return;
        Bounds = bounds;
        IsCompleting = false;
        _translation.SetRegion(bounds);
        Refresh(OverlayInteractionMode.Selecting);
    }

    // The whole monitor, taskbar included: a fullscreen game covers the taskbar without shrinking the work area,
    // so the work area would cut a strip off the game.
    internal void SelectWholeScreen()
    {
        if (_disposed) return;
        Select(_mapper.CaptureBounds);
        _wholeScreenSelected?.Invoke();
    }

    internal void Refresh(OverlayInteractionMode mode)
    {
        if (_disposed || Bounds is not { } bounds) return;
        if (mode is not (OverlayInteractionMode.Selecting or OverlayInteractionMode.TranslationShown))
        {
            _visual.Toolbar.Hide();
            return;
        }
        _selection.ShowSelectionFrame(bounds, hold: false);
        _visual.Toolbar.SetActionLabel(_visual.TranslateButton,
            _translation.IsTranslationShown ? _strings.ShowOriginal : _strings.Translate);
        ShowToolbar(bounds);
        _visual.Toolbar.Surface.IsEnabled = !IsCompleting;
    }

    // Keeps the actions beside the selection while the screen under them is zoomed or panned.
    internal void FollowView()
    {
        if (!_disposed && _visual.Toolbar.IsOpen && QuestionAnchor is { } anchor) ShowToolbar(anchor);
    }

    private GdiRectangle? QuestionAnchor => Bounds ?? (IsBareQuestion ? _mapper.CaptureBounds : null);

    private void ShowToolbar(GdiRectangle bounds) =>
        _visual.Toolbar.Show(_toViewport(_mapper.ToDips(bounds)), new Size(_root.ActualWidth, _root.ActualHeight));

    internal void Dismiss()
    {
        if (Bounds is not null) _translation.CommitVisibleImage();
        Bounds = null;
        _copyTimer.Stop();
        IsCompleting = false;
        _visual.Toolbar.Hide();
        ResetAskPrompt();
    }

    internal bool CloseAskPrompt()
    {
        if (_disposed || !_visual.Toolbar.IsPromptOpen) return false;
        ResetAskPrompt();
        return true;
    }

    internal bool TryHandleShortcut(Key key, ModifierKeys modifiers)
    {
        if (!CanAct || _visual.Toolbar.IsPromptOpen) return false;
        if (OverlayShortcuts.Search.Matches(key, modifiers)) return KeyboardShortcut.Press(_visual.SearchButton);
        if (OverlayShortcuts.Copy.Matches(key, modifiers)) return KeyboardShortcut.Press(_visual.CopyButton);
        if (OverlayShortcuts.Save.Matches(key, modifiers)) return KeyboardShortcut.Press(_visual.SaveButton);
        return OverlayShortcuts.Translate.Matches(key, modifiers) && KeyboardShortcut.Press(_visual.TranslateButton);
    }

    // Typing over the actions starts a question with what was typed, so the Ask button is optional. Typing into an
    // open prompt that lost focus continues the question instead of being dropped.
    internal bool TryTypeQuestion(string text)
    {
        if (!CanAct || !CanType(text)) return false;
        if (!_visual.Toolbar.IsPromptOpen &&
            (string.IsNullOrWhiteSpace(text) || !KeyboardShortcut.Press(_visual.AskButton) ||
             !_visual.Toolbar.IsPromptOpen)) return false;
        var input = _visual.AskPrompt.Input;
        var room = input.MaxLength == 0 ? text.Length : Math.Max(0, input.MaxLength - input.Text.Length);
        input.Focus();
        input.AppendText(text[..Math.Min(room, text.Length)]);
        input.CaretIndex = input.Text.Length;
        return true;
    }

    private static bool CanType(string text) => text.Length > 0 && !text.Any(char.IsControl);

    internal void SetInputLanguage(string? tag)
    {
        if (_disposed) return;
        _visual.Toolbar.SetPromptLanguage(tag);
    }

    private void ResetAskPrompt()
    {
        // A submitted question keeps the prompt shown until the overlay moves on, so closing is the one place
        // where every opened prompt ends, whether it was sent or not.
        var closed = _visual.Toolbar.IsPromptOpen && !_disposed;
        var canceled = _askDraftStarted && !_disposed;
        _askDraftStarted = false;
        _askImageAttached = false;
        IsBareQuestion = false;
        _visual.Toolbar.SetPromptOpen(false);
        _visual.AskPrompt.Input.Clear();
        UpdateScreenChip();
        if (closed) _askPromptClosed?.Invoke();
        if (canceled) _publish(new AskDraftCanceled());
    }

    private bool CanAct =>
        !_disposed && !IsCompleting && (Bounds is not null || IsBareQuestion) && _visual.Toolbar.IsOpen;

    private bool CanActOnSelection => CanAct && Bounds is not null;

    private void OnSearch(object sender, RoutedEventArgs e)
    {
        if (!CanActOnSelection) return;
        IsCompleting = true;
        _search(Bounds!.Value);
        e.Handled = true;
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        if (!CanActOnSelection) return;
        if (_clipboard.TryCopyImage(_cropImage(Bounds!.Value)))
        {
            IsCompleting = true;
            _visual.Toolbar.Surface.IsEnabled = false;
            _copyTimer.Start();
        }
        e.Handled = true;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (!CanActOnSelection) return;
        var image = _cropImage(Bounds!.Value);
        IsCompleting = true;
        _visual.Toolbar.Hide();
        _publish(new SaveSelectedImage(image));
        e.Handled = true;
    }

    private void OnTranslate(object sender, RoutedEventArgs e)
    {
        if (!CanActOnSelection) return;
        _translation.Toggle();
        e.Handled = true;
    }

    private void OnAsk(object sender, RoutedEventArgs e)
    {
        if (!CanActOnSelection) return;
        OpenPrompt();
        e.Handled = true;
    }

    private void OpenPrompt()
    {
        UpdateScreenChip();
        _visual.Toolbar.SetPromptOpen(true);
        _askPromptOpened?.Invoke();
        // Warming the browser now hides its startup behind typing; the image waits for the first character.
        _askDraftStarted = true;
        _publish(new AskDraftStarted());
    }

    private void OnPromptTextChanged(object sender, TextChangedEventArgs e) => AttachImageOnceTyped();

    // A screen added to a bare question waits for Send: once pasted, the page uploads it at once, and removing it
    // again could not take it back from Google.
    private void AttachImageOnceTyped()
    {
        if (!_askDraftStarted || _askImageAttached || IsBareQuestion || _visual.AskPrompt.Input.Text.Length == 0 ||
            !CanAct || Bounds is not { } bounds) return;
        var selection = _createSelection(bounds);
        _askImageAttached = true;
        try { _publish(new AskImageAttached(selection)); }
        catch
        {
            selection.Dispose();
            throw;
        }
    }

    private void OnToggleScreen(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (!CanAct || !IsBareQuestion) return;
        if (Bounds is null) SelectWholeScreen();
        else RemoveScreen();
        UpdateScreenChip();
        var input = _visual.AskPrompt.Input;
        input.Focus();
        input.CaretIndex = input.Text.Length;
    }

    private void RemoveScreen()
    {
        _translation.CommitVisibleImage();
        Bounds = null;
        _selection.Cancel();
    }

    private void UpdateScreenChip()
    {
        var withoutScreen = IsBareQuestion && Bounds is null;
        _visual.Toolbar.SetPromptPlaceholder(withoutScreen ? _strings.AskPlaceholderTextOnly : _strings.AskPlaceholder);
        if (_visual.AskPrompt.Attachment is not { } chip) return;
        if (IsBareQuestion) chip.Show(attached: Bounds is not null);
        else chip.Hide();
    }

    private void OnSend(object sender, RoutedEventArgs e)
    {
        SubmitQuestion();
        e.Handled = true;
    }

    private void OnPromptKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        SubmitQuestion();
        e.Handled = true;
    }

    private void SubmitQuestion()
    {
        var question = _visual.AskPrompt.Input.Text.Trim();
        if (!CanAct || question.Length == 0) return;
        var bounds = Bounds;
        var selection = _askImageAttached || bounds is null ? null : _createSelection(bounds.Value);
        IsCompleting = true;
        _askDraftStarted = false;
        _visual.Toolbar.Hide();
        try
        {
            _publish(bounds is null
                ? AskAboutSelection.WithoutImage(question)
                : new AskAboutSelection(selection, question));
        }
        catch
        {
            selection?.Dispose();
            throw;
        }
    }

    private void OnCopyFeedbackCompleted(object? sender, EventArgs e)
    {
        _copyTimer.Stop();
        if (!_disposed) _close();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Dismiss();
        _visual.Toolbar.Hide(animate: false);
        _copyTimer.Tick -= OnCopyFeedbackCompleted;
        _visual.SearchButton.Click -= OnSearch;
        _visual.CopyButton.Click -= OnCopy;
        _visual.SaveButton.Click -= OnSave;
        _visual.TranslateButton.Click -= OnTranslate;
        _visual.AskButton.Click -= OnAsk;
        _visual.AskPrompt.SendButton.Click -= OnSend;
        _visual.AskPrompt.Input.KeyDown -= OnPromptKeyDown;
        _visual.AskPrompt.Input.TextChanged -= OnPromptTextChanged;
        if (_visual.AskPrompt.Attachment is { } attachment) attachment.Root.Click -= OnToggleScreen;
        foreach (var ripple in _ripples) ripple.Dispose();
    }
}

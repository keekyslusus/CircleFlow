using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CircleToSearch.Capture;
using CircleToSearch.Search;
using CircleToSearch.TextRecognition;
using Xunit;
using GdiBitmap = System.Drawing.Bitmap;
using GdiRectangle = System.Drawing.Rectangle;
using GdiSize = System.Drawing.Size;

namespace CircleToSearch.Tests;

[Trait("Category", "Slow")]
public sealed class TextLinkOverlayTests
{
    private static readonly GdiRectangle FirstWord = new(100, 200, 60, 20);
    private static readonly GdiRectangle SecondWord = new(170, 200, 160, 20);
    private static readonly Point OnFirstWord = new(130, 210);
    private static readonly Point OnSecondWord = new(250, 210);

    [Fact]
    public void Link_selection_adds_open_between_search_and_copy() => Run(() =>
    {
        using var h = new Harness("example.com/docs");

        h.SelectText(OnFirstWord, OnFirstWord);

        Assert.True(h.Text.Toolbar.IsOpen);
        Assert.Equal(new[] { "Search", "Open example.com", "Copy" }, h.VisibleActions().Select(AutomationProperties.GetName));
        Assert.Equal("Open example.com", h.Text.OpenLinkButton.ToolTip);
        Assert.Contains("example.com", h.Labels(h.Text.OpenLinkButton));
    });

    [Fact]
    public void Open_publishes_the_link_once_and_disables_search() => Run(() =>
    {
        using var h = new Harness("https://example.com/docs.");
        h.SelectText(OnFirstWord, OnFirstWord);

        Click(h.Text.OpenLinkButton);
        Click(h.Text.OpenLinkButton);
        Click(h.Text.SearchButton);

        var open = Assert.IsType<OpenLink>(Assert.Single(h.Commands, command => command is not TextSelectionStarted));
        Assert.Equal("https://example.com/docs", open.Url.AbsoluteUri);
        Assert.False(h.Text.SearchButton.IsEnabled);
    });

    [Fact]
    public void Search_and_copy_keep_the_text_as_read() => Run(() =>
    {
        using var h = new Harness("examp1e.com");
        h.SelectText(OnFirstWord, OnFirstWord);

        Assert.Equal("Open examp1e.com", AutomationProperties.GetName(h.Text.OpenLinkButton));
        Click(h.Text.CopyButton);
        Click(h.Text.SearchButton);
        Click(h.Text.OpenLinkButton);

        Assert.Equal(["examp1e.com"], h.Copied);
        var search = Assert.IsType<SearchSelectedText>(Assert.Single(h.Commands, command => command is not TextSelectionStarted));
        Assert.Equal("examp1e.com", search.Text);
    });

    [Fact]
    public void Address_selection_opens_mail() => Run(() =>
    {
        using var h = new Harness("hello@example.com");
        h.SelectText(OnFirstWord, OnFirstWord);

        Assert.Equal("Open hello@example.com", AutomationProperties.GetName(h.Text.OpenLinkButton));
        Click(h.Text.OpenLinkButton);

        var open = Assert.IsType<OpenLink>(h.Commands.Last());
        Assert.Equal("mailto:hello@example.com", open.Url.OriginalString);
    });

    [Fact]
    public void Open_is_offered_only_while_the_whole_selection_is_the_link() => Run(() =>
    {
        using var h = new Harness("Visit", "example.com");

        h.SelectText(OnFirstWord, OnSecondWord);
        Assert.True(h.Text.Toolbar.IsOpen);
        Assert.Equal(new[] { "Search", "Copy" }, h.VisibleActions().Select(AutomationProperties.GetName));

        h.SelectText(OnSecondWord, OnSecondWord);
        Assert.Equal(new[] { "Search", "Open example.com", "Copy" }, h.VisibleActions().Select(AutomationProperties.GetName));
        Assert.True(h.Text.SearchButton.IsEnabled);
        Assert.True(h.Text.OpenLinkButton.IsEnabled);

        h.SelectText(OnFirstWord, OnFirstWord);
        Assert.Equal(Visibility.Collapsed, h.Text.OpenLinkButton.Visibility);
        Click(h.Text.OpenLinkButton);
        Assert.DoesNotContain(h.Commands, command => command is OpenLink);
    });

    [Fact]
    public void File_name_selection_is_not_offered_as_a_link() => Run(() =>
    {
        using var h = new Harness("README.md");

        h.SelectText(OnFirstWord, OnFirstWord);

        Assert.True(h.Text.Toolbar.IsOpen);
        Assert.Equal(Visibility.Collapsed, h.Text.OpenLinkButton.Visibility);
    });

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static void Run(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { error = exception; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)));
        Assert.Null(error);
    }

    private sealed class ImmediateRecognizer(OcrDocument document) : IOcrRecognizer
    {
        public Task<OcrRecognitionOutcome> RecognizeAsync(
            BitmapSource source, string? requestedLanguageTag, CancellationToken cancellationToken) =>
            Task.FromResult(OcrRecognitionOutcome.Success(document));
    }

    private sealed class Harness : IDisposable
    {
        private readonly GdiBitmap _frame = new(640, 400);
        private Point _point;

        internal Harness(string firstWord, string? secondWord = null)
        {
            using (var graphics = System.Drawing.Graphics.FromImage(_frame)) graphics.Clear(System.Drawing.Color.Black);
            OcrWord[] words = secondWord is null
                ? [new OcrWord(0, 0, 0, firstWord, FirstWord)]
                : [new OcrWord(0, 0, 0, firstWord, FirstWord), new OcrWord(1, 0, 1, secondWord, SecondWord)];
            var line = GdiRectangle.Union(FirstWord, secondWord is null ? FirstWord : SecondWord);
            var document = new OcrDocument("en", new GdiSize(640, 400), [new OcrLine(0, 0, line, words)]);
            var monitor = new GdiRectangle(0, 0, 640, 400);
            Window = new OverlayWindow(_frame, monitor, monitor, 1,
                new OverlayLaunchOptions(new OverlayOptions(0, 12), TestUiStrings.English,
                    [new(SearchProviderIds.GoogleLens, SearchProviderIds.GoogleLens)], SearchProviderIds.GoogleLens,
                    new SearchSessionOptions()),
                Commands.Add,
                TestOverlayControllers.CreateFactory(
                    setClipboard: Copied.Add,
                    animationsEnabled: () => false,
                    pointerPosition: _ => _point,
                    ocrRecognizer: new ImmediateRecognizer(document)),
                overscan: false);
            Window.Show();
            Window.UpdateLayout();
            WpfUi.PumpUntil(() => HoverShowsText(OnFirstWord), "OCR delivery timed out.");
        }

        internal OverlayWindow Window { get; }
        internal TextSelectionVisual Text => Window.VisualState.TextSelection;
        internal List<IOverlayCommand> Commands { get; } = [];
        internal List<string> Copied { get; } = [];

        internal IEnumerable<Button> VisibleActions() =>
            Assert.IsType<WrapPanel>(Text.Toolbar.Surface.Child).Children.OfType<Button>()
                .Where(button => button.Visibility == Visibility.Visible);

        internal IEnumerable<string> Labels(Button button) =>
            Assert.IsType<StackPanel>(button.Content).Children.OfType<TextBlock>().Select(text => text.Text);

        internal void SelectText(Point start, Point end)
        {
            var input = Window.VisualState.Selection.InputSurface;
            _point = start;
            Raise(UIElement.MouseLeftButtonDownEvent);
            _point = end;
            input.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.MouseMoveEvent, Source = input });
            Raise(UIElement.MouseLeftButtonUpEvent);
            Window.UpdateLayout();
        }

        private bool HoverShowsText(Point point)
        {
            var input = Window.VisualState.Selection.InputSurface;
            _point = point;
            input.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.MouseMoveEvent, Source = input });
            return Window.Cursor == Cursors.IBeam;
        }

        private void Raise(RoutedEvent routedEvent)
        {
            var input = Window.VisualState.Selection.InputSurface;
            input.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            { RoutedEvent = routedEvent, Source = input });
        }

        public void Dispose()
        {
            Window.CloseFromSession();
            foreach (var command in Commands) OverlayCommandOwnership.DisposePayload(command);
            _frame.Dispose();
        }
    }
}

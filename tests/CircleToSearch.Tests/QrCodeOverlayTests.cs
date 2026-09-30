using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CircleToSearch.Capture;
using CircleToSearch.QrCodes;
using CircleToSearch.Search;
using Xunit;
using GdiBitmap = System.Drawing.Bitmap;
using GdiRectangle = System.Drawing.Rectangle;

namespace CircleToSearch.Tests;

[Trait("Category", "Slow")]
public sealed class QrCodeOverlayTests
{
    private static readonly GdiRectangle TopCode = new(100, 100, 80, 80);

    [Fact]
    public void Found_code_gets_a_highlight_and_a_chip_below_it() => Run(() =>
    {
        using var h = new Harness([new("https://www.example.com/menu", TopCode)]);

        var chip = h.WaitForChip();

        Assert.Equal(new[] { "Open example.com", "Copy" }, chip.Buttons.Select(AutomationProperties.GetName));
        Assert.True(Canvas.GetTop(chip.Surface) >= TopCode.Bottom);
        var corners = h.Viewfinder();
        Assert.Equal(1, corners.Opacity);
        Assert.Equal(QrCodeViewfinder.Thickness, h.ViewfinderThickness());
        Assert.Equal(new Rect(94, 94, 92, 92),
            new Rect(Canvas.GetLeft(corners), Canvas.GetTop(corners), corners.Width, corners.Height));
    });

    [Fact]
    public void Chip_goes_above_a_code_with_no_room_below() => Run(() =>
    {
        var code = new GdiRectangle(300, 300, 80, 80);
        using var h = new Harness([new("https://example.com", code)]);

        var chip = h.WaitForChip();

        Assert.True(Canvas.GetTop(chip.Surface) + chip.Surface.ActualHeight <= code.Top);
    });

    [Fact]
    public void Open_publishes_the_link_and_hides_the_chips() => Run(() =>
    {
        using var h = new Harness([new("https://example.com/menu", TopCode)]);
        var chip = h.WaitForChip();

        Click(chip.Buttons[0]);
        Click(chip.Buttons[0]);

        var open = Assert.IsType<OpenLink>(Assert.Single(h.Commands));
        Assert.Equal("https://example.com/menu", open.Url.AbsoluteUri);
        Assert.Equal(Visibility.Collapsed, chip.Surface.Visibility);
    });

    [Fact]
    public void Copy_copies_the_link_as_scanned() => Run(() =>
    {
        using var h = new Harness([new("https://example.com/menu", TopCode)]);

        Click(h.WaitForChip().Buttons[1]);

        Assert.Equal("https://example.com/menu", Assert.Single(h.Copied));
        Assert.Empty(h.Commands);
    });

    [Fact]
    public void Wifi_chip_copies_the_password_without_showing_it() => Run(() =>
    {
        using var h = new Harness([new("WIFI:T:WPA;S:Home;P:secret;;", TopCode)]);
        var chip = h.WaitForChip();

        Assert.Equal("Wi-Fi network Home", AutomationProperties.GetName(Assert.Single(chip.Buttons)));
        Click(chip.Buttons[0]);

        Assert.Equal("secret", Assert.Single(h.Copied));
        Assert.Equal(TestUiStrings.English.QrPasswordCopied, h.ToastText());
    });

    [Fact]
    public void Other_schemes_are_only_copied() => Run(() =>
    {
        using var h = new Harness([new("tg://login?token=abc", TopCode)]);

        Click(Assert.Single(h.WaitForChip().Buttons));

        Assert.Equal("tg://login?token=abc", Assert.Single(h.Copied));
        Assert.Empty(h.Commands);
    });

    [Fact]
    public void Two_factor_code_is_copied_without_showing_its_key() => Run(() =>
    {
        const string code = "otpauth://totp/Example:me?secret=JBSWY3DPEHPK3PXP";
        using var h = new Harness([new(code, TopCode)]);
        var button = Assert.Single(h.WaitForChip().Buttons);

        Assert.Equal("Example:me", AutomationProperties.GetName(button));
        Click(button);

        Assert.Equal(code, Assert.Single(h.Copied));
        Assert.Equal(TestUiStrings.English.Copied, h.ToastText());
    });

    [Fact]
    public void Selection_hides_the_chips_until_it_is_dismissed() => Run(() =>
    {
        using var h = new Harness([new("https://example.com", TopCode)]);
        var chip = h.WaitForChip();

        h.Select();
        Assert.Equal(Visibility.Collapsed, chip.Surface.Visibility);
        Assert.Equal(0, h.Viewfinder().Opacity);

        h.Escape();
        Assert.Equal(Visibility.Visible, chip.Surface.Visibility);
    });

    [Fact]
    public void Scan_logging_leaves_out_the_code_content() => Run(() =>
    {
        var directory = Path.Combine(TestOutputPaths.TempDirectory, "qr-log-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        using (var h = new Harness([new("WIFI:T:WPA;S:HomeNet;P:hunter2;;", TopCode)], log: new PluginLog(directory)))
            h.WaitForChip();
        var contents = File.ReadAllText(Path.Combine(directory, "plugin.log"));

        Assert.Contains("1 code(s)", contents);
        Assert.DoesNotContain("hunter2", contents);
        Assert.DoesNotContain("HomeNet", contents);
    });

    [Fact]
    public void Viewfinder_snaps_in_from_a_larger_frame() => Run(() =>
    {
        using var time = ManualAnimationClock.Install();
        using var h = new Harness([new("https://example.com", TopCode)], animations: true);

        Assert.True(time.AdvanceUntil(() => h.Window.VisualState.QrCodes.Viewfinders.Children.Count == 1));
        time.Advance(16);
        Assert.True(h.ViewfinderScale() > 1.1);
        time.Advance(400);
        Assert.Equal(1, h.ViewfinderScale(), 3);
        Assert.Equal(1, h.Viewfinder().Opacity, 3);
    });

    [Fact]
    public void Hovering_a_chip_thickens_its_viewfinder() => Run(() =>
    {
        using var time = ManualAnimationClock.Install();
        using var h = new Harness([new("https://example.com", TopCode)], animations: true);
        Assert.True(time.AdvanceUntil(() => h.Window.VisualState.QrCodes.Viewfinders.Children.Count == 1));
        var surface = Chips(h).Surface;

        surface.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });
        time.Advance(200);
        Assert.Equal(QrCodeViewfinder.EmphasizedThickness, h.ViewfinderThickness(), 3);

        surface.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });
        time.Advance(200);
        Assert.Equal(QrCodeViewfinder.Thickness, h.ViewfinderThickness(), 3);
    });

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Viewfinder_pulses_only_after_a_successful_copy(bool copyFails, bool pulses) => Run(() =>
    {
        using var time = ManualAnimationClock.Install();
        using var h = new Harness([new("https://example.com", TopCode)], animations: true, copyFails: copyFails);
        Assert.True(time.AdvanceUntil(() => h.Window.VisualState.QrCodes.Viewfinders.Children.Count == 1));
        time.Advance(500);

        Click(Chips(h).Buttons[1]);
        time.Advance(110);

        Assert.Equal(pulses, h.ViewfinderScale() > 1.05);
        time.Advance(200);
        Assert.Equal(1, h.ViewfinderScale(), 3);
    });

    [Fact]
    public void Nothing_is_scanned_when_the_setting_is_off() => Run(() =>
    {
        using var h = new Harness([new("https://example.com", TopCode)], scan: false);

        DispatcherPump.For(100);

        Assert.Equal(0, h.ScanCalls);
        Assert.Empty(h.Window.VisualState.QrCodes.Viewfinders.Children);
    });

    private static Chip Chips(Harness h) => h.WaitForChip();

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

    private sealed record Chip(Border Surface, Button[] Buttons);

    private sealed class Harness : IDisposable
    {
        private readonly GdiBitmap _frame = new(640, 400);
        private Point _point;
        private int _scanCalls;

        internal Harness(IReadOnlyList<QrCodeMatch> matches, bool scan = true, PluginLog? log = null,
            bool animations = false, bool copyFails = false)
        {
            using (var graphics = System.Drawing.Graphics.FromImage(_frame)) graphics.Clear(System.Drawing.Color.Black);
            var monitor = new GdiRectangle(0, 0, 640, 400);
            Window = new OverlayWindow(_frame, monitor, monitor, 1,
                new OverlayLaunchOptions(new OverlayOptions(0, 12), TestUiStrings.English,
                    [new(SearchProviderIds.GoogleLens, SearchProviderIds.GoogleLens)], SearchProviderIds.GoogleLens,
                    new SearchSessionOptions(ScanQrCodes: scan)),
                Commands.Add,
                TestOverlayControllers.CreateFactory(
                    setClipboard: text =>
                    {
                        if (copyFails) throw new InvalidOperationException("clipboard busy");
                        Copied.Add(text);
                    },
                    animationsEnabled: () => animations,
                    pointerPosition: _ => _point,
                    log: log,
                    scanQrCodes: (_, _) =>
                    {
                        Interlocked.Increment(ref _scanCalls);
                        return matches;
                    }),
                overscan: false);
            Window.Show();
            Window.UpdateLayout();
        }

        internal OverlayWindow Window { get; }
        internal List<IOverlayCommand> Commands { get; } = [];
        internal List<string> Copied { get; } = [];
        internal int ScanCalls => Volatile.Read(ref _scanCalls);

        internal Chip WaitForChip()
        {
            Chip? chip = null;
            Assert.True(DispatcherPump.Until(() =>
            {
                chip = Chips().FirstOrDefault();
                return chip?.Surface.Visibility == Visibility.Visible;
            }));
            Window.UpdateLayout();
            return chip!;
        }

        internal Canvas Viewfinder() =>
            Assert.IsType<Canvas>(Assert.Single(Window.VisualState.QrCodes.Viewfinders.Children));

        internal double ViewfinderThickness() =>
            Viewfinder().Children.OfType<System.Windows.Shapes.Path>().Last().StrokeThickness;

        internal double ViewfinderScale() =>
            Assert.IsType<ScaleTransform>(Viewfinder().RenderTransform).ScaleX;

        private IEnumerable<Chip> Chips() =>
            Window.VisualState.QrCodes.Layer.Children.OfType<Canvas>()
                .Where(layer => !ReferenceEquals(layer, Window.VisualState.QrCodes.Viewfinders))
                .Select(layer => Assert.IsType<Border>(Assert.Single(layer.Children)))
                .Select(surface => new Chip(surface,
                    Assert.IsType<WrapPanel>(surface.Child).Children.OfType<Button>().ToArray()));

        internal void Select()
        {
            _point = new Point(400, 50);
            Raise(UIElement.MouseRightButtonDownEvent);
            _point = new Point(500, 150);
            var input = Window.VisualState.Selection.InputSurface;
            input.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.MouseMoveEvent, Source = input });
            Raise(UIElement.MouseRightButtonUpEvent);
            Window.UpdateLayout();
        }

        internal void Escape() =>
            Window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(Window)!, 0, Key.Escape)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent });

        internal string ToastText()
        {
            var toastSlot = Assert.IsType<Grid>(Window.VisualState.Bottom.Stack.Children[0]);
            return Assert.IsType<TextBlock>(Assert.IsType<Border>(Assert.Single(toastSlot.Children)).Child).Text;
        }

        private void Raise(RoutedEvent routedEvent)
        {
            var input = Window.VisualState.Selection.InputSurface;
            input.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right)
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

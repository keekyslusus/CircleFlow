using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using CircleToSearch.Interop;
using CircleToSearch.Search;
using CircleToSearch.Search.Browser;
using CircleToSearch.Ui;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class SearchBrowserWindowViewTests
{
    [Fact]
    public void Provider_and_theme_update_window_controls_without_browser_content()
    {
        using var dispatcher = new StaDispatcher("Search browser view test");
        SearchBrowserWindowView? view = null;
        try
        {
            dispatcher.Send(() =>
            {
                var browser = new Grid();
                view = CreateView(browser, false);
                var first = new SearchProviderDescriptor("first", "First");
                view.SetProvider(first);
                var content = (Grid)view.Window.Content;
                var header = (DockPanel)content.Children[1];
                var title = (TextBlock)header.Children[1];
                var close = (Button)header.Children[0];
                Assert.Equal(TestUiStrings.English.SearchBrowserWindowTitle("First"), view.Window.Title);
                Assert.Equal(view.Window.Title, title.Text);
                Assert.Equal(Visibility.Visible, browser.Visibility);
                Assert.Equal(2, content.Children.Count);

                foreach (var light in new[] { true, false })
                {
                    view.ApplyTheme(light);
                    var palette = PluginPalette.For(light);
                    Assert.Equal(palette.WindowSurface, ((SolidColorBrush)view.Window.Background).Color);
                    Assert.Equal(palette.WindowSurface, ((SolidColorBrush)content.Background).Color);
                    Assert.Equal(palette.PrimaryText, ((SolidColorBrush)title.Foreground).Color);
                    Assert.Equal(palette.PrimaryText, ((SolidColorBrush)close.Foreground).Color);
                }
                view.ShowLoading();
                view.HideLoading();
                Assert.Equal(Visibility.Visible, browser.Visibility);
            });
        }
        finally { dispatcher.Send(() => view?.Window.Close()); }
    }

    [Fact]
    public async Task Enabled_loading_overlay_ignores_stale_fade_and_close_button_closes_view()
    {
        using var dispatcher = new StaDispatcher("Search browser loading test");
        SearchBrowserWindowView? first = null;
        SearchBrowserWindowView? second = null;
        Grid? firstBrowser = null;
        Grid? secondBrowser = null;
        try
        {
            Task? entrance = null;
            dispatcher.Send(() =>
            {
                firstBrowser = new Grid();
                first = CreateView(firstBrowser, true);
                first.SetProvider(new SearchProviderDescriptor("first", "First"));
                var content = (Grid)first.Window.Content;
                var overlay = (Grid)content.Children[1];
                var loading = (TextBlock)((StackPanel)overlay.Children[0]).Children[1];
                Assert.Equal(TestUiStrings.English.SearchBrowserLoading("First"), loading.Text);
                entrance = first.ShowAsync();
            });
            await entrance!.WaitAsync(TimeSpan.FromSeconds(5));
            dispatcher.Send(() =>
            {
                first!.ShowLoading();
                Assert.Equal(Visibility.Hidden, firstBrowser!.Visibility);
                first.HideLoading();
                first.ShowLoading();
            });
            await Task.Delay(350);
            dispatcher.Send(() => Assert.Equal(Visibility.Hidden, firstBrowser!.Visibility));
            dispatcher.Send(() => first!.HideLoading());
            await Task.Delay(350);
            dispatcher.Send(() => Assert.Equal(Visibility.Visible, firstBrowser!.Visibility));

            dispatcher.Send(() =>
            {
                first!.ShowLoading();
                first.HideLoading();
                secondBrowser = new Grid();
                second = CreateView(secondBrowser, true);
                second.ShowLoading();
                var header = (DockPanel)((Grid)first.Window.Content).Children[2];
                var close = (Button)header.Children[0];
                close.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Assert.True(first.IsClosed);
            });
            await Task.Delay(350);
            dispatcher.Send(() =>
            {
                Assert.False(second!.IsClosed);
                Assert.Equal(Visibility.Hidden, secondBrowser!.Visibility);
            });
        }
        finally
        {
            dispatcher.Send(() =>
            {
                if (first is { IsClosed: false }) first.Window.Close();
                if (second is { IsClosed: false }) second.Window.Close();
            });
        }
    }

    private static SearchBrowserWindowView CreateView(Grid browser, bool loading) => new(
        TestUiStrings.English,
        browser,
        lightTheme: false,
        loading,
        window => new BottomResultsPanel(window, new POINT()));
}

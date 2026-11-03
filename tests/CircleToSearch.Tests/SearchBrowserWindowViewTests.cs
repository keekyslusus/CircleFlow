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

[Trait("Category", "Slow")]
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
                view = CreateView(browser);
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
                    Assert.Equal(palette.Roles.Background, ((SolidColorBrush)view.Window.Background).Color);
                    Assert.Equal(palette.Roles.Background, ((SolidColorBrush)content.Background).Color);
                    Assert.Equal(palette.Roles.OnBackground, ((SolidColorBrush)title.Foreground).Color);
                    Assert.Equal(palette.Roles.OnBackground, ((SolidColorBrush)close.Foreground).Color);
                }
            });
        }
        finally { dispatcher.Send(() => view?.Window.Close()); }
    }

    [Fact]
    public async Task Close_button_closes_only_its_own_view()
    {
        using var dispatcher = new StaDispatcher("Search browser close test");
        SearchBrowserWindowView? first = null;
        SearchBrowserWindowView? second = null;
        try
        {
            Task? entrance = null;
            dispatcher.Send(() =>
            {
                first = CreateView(new Grid());
                first.SetProvider(new SearchProviderDescriptor("first", "First"));
                entrance = first.ShowAsync();
            });
            await entrance!.WaitAsync(TimeSpan.FromSeconds(5));
            dispatcher.Send(() =>
            {
                second = CreateView(new Grid());
                var header = (DockPanel)((Grid)first!.Window.Content).Children[1];
                var close = (Button)header.Children[0];
                close.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Assert.True(first.IsClosed);
                Assert.False(second.IsClosed);
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

    [Fact]
    public void Reused_window_takes_labels_and_provider_name_from_the_current_app_language()
    {
        using var dispatcher = new StaDispatcher("Search browser language test");
        SearchBrowserWindowView? view = null;
        try
        {
            dispatcher.Send(() =>
            {
                var prefix = string.Empty;
                var strings = new UiStrings(key => prefix + TestUiStrings.EnglishValues[key]);
                view = CreateView(new Grid(), strings);
                var provider = new SearchProviderDescriptor(SearchProviderIds.YandexImages, () => strings.YandexImagesProviderName);
                var close = (Button)((DockPanel)((Grid)view.Window.Content).Children[1]).Children[0];
                view.SetProvider(provider);
                Assert.Equal("Close", close.ToolTip);

                prefix = "ru:";
                view.SetProvider(provider);
                Assert.Equal("ru:Close", close.ToolTip);
                Assert.Equal("ru:Close", System.Windows.Automation.AutomationProperties.GetName(close));
                Assert.Equal("ru:CircleFlow: ru:Yandex Images", view.Window.Title);
            });
        }
        finally { dispatcher.Send(() => view?.Window.Close()); }
    }

    private static SearchBrowserWindowView CreateView(Grid browser, UiStrings? strings = null) => new(
        strings ?? TestUiStrings.English,
        browser,
        lightTheme: false,
        window => new BottomResultsPanel(window, new POINT()));
}

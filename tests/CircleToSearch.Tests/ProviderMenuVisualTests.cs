using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using CircleToSearch.Capture;
using CircleToSearch.Search;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class ProviderMenuVisualTests
{
    [Fact]
    public void Updating_provider_rebuilds_items_and_excludes_the_new_selection()
    {
        var failure = RunOnSta(() =>
        {
            SearchProviderDescriptor[] providers =
            [
                new(SearchProviderIds.GoogleLens, "Google Lens"),
                new(SearchProviderIds.YandexImages, "Yandex Images"),
            ];
            var root = OverlayVisualFactory.CreateRoot(
                null,
                new Size(640, 400),
                32,
                lightTheme: false,
                TestUiStrings.English,
                providers,
                SearchProviderIds.GoogleLens);

            var provider = Assert.IsType<ProviderMenuVisual>(root.Provider);
            ProviderMenuVisualPresenter.UpdateProvider(
                provider,
                providers,
                SearchProviderIds.YandexImages,
                TestUiStrings.English,
                lightTheme: false);

            var item = Assert.Single(
                ((StackPanel)provider.Menu.Child).Children.OfType<Button>());
            Assert.Equal(SearchProviderIds.GoogleLens, item.Tag);
            Assert.Equal(
                TestUiStrings.English.SelectSearchProvider("Yandex Images"),
                AutomationProperties.GetName(provider.Button));
            root.Music.Waveform.Dispose();
            root.Effects.SceneRipples.Dispose();
        });

        Assert.Null(failure);
    }

    private static Exception? RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(30));
        Assert.False(thread.IsAlive, "the STA thread did not finish in time");
        return failure;
    }
}

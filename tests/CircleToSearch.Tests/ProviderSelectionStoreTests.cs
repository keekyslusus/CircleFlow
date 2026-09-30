using CircleToSearch.Search;
using CircleToSearch.Settings;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class ProviderSelectionStoreTests
{
    [Fact]
    public void Valid_provider_is_stored_and_saved_once()
    {
        using var harness = new Harness();

        harness.Store.Save(SearchProviderIds.YandexImages);

        Assert.Equal(SearchProviderIds.YandexImages, harness.Settings.SearchProviderId);
        Assert.Equal(1, harness.SaveCalls);
        Assert.Empty(harness.Notifier.Errors);
    }

    [Fact]
    public void Unknown_provider_is_normalized_to_router_default()
    {
        using var harness = new Harness();

        harness.Store.Save("missing");

        Assert.Equal(SearchProviderIds.GoogleLens, harness.Settings.SearchProviderId);
        Assert.Equal(0, harness.SaveCalls);
    }

    [Fact]
    public void Effective_selection_does_not_mutate_or_instantiate_for_unknown_saved_setting()
    {
        using var harness = new Harness("missing");

        var effective = harness.Store.GetEffectiveSelection();

        Assert.Equal(SearchProviderIds.GoogleLens, effective.Id);
        Assert.Equal(SearchProviderIds.GoogleLens, harness.Settings.SearchProviderId);
        Assert.Equal(0, harness.SaveCalls);
        Assert.Equal(0, harness.ProviderFactoryCalls);
    }

    [Fact]
    public void Save_exception_keeps_previous_value_notifies_once_and_does_not_throw()
    {
        using var harness = new Harness(saveThrows: true);

        harness.Store.Save(SearchProviderIds.YandexImages);

        Assert.Equal(SearchProviderIds.GoogleLens, harness.Settings.SearchProviderId);
        Assert.Equal(1, harness.SaveCalls);
        Assert.Equal(TestUiStrings.English.StorageSaveFailed, Assert.Single(harness.Notifier.Errors).Message);
    }

    [Fact]
    public void Yandex_is_hidden_from_the_menu_by_default_unless_it_is_the_selected_provider()
    {
        using var harness = new Harness();

        Assert.False(harness.Store.IsShownInMenu(SearchProviderIds.YandexImages));
        Assert.Equal([SearchProviderIds.GoogleLens, SearchProviderIds.TraceMoe],
            harness.Store.MenuProviders(SearchProviderIds.GoogleLens).Select(provider => provider.Id));
        Assert.Equal([SearchProviderIds.GoogleLens, SearchProviderIds.YandexImages, SearchProviderIds.TraceMoe],
            harness.Store.MenuProviders(SearchProviderIds.YandexImages).Select(provider => provider.Id));
    }

    [Fact]
    public void Providers_are_shown_and_hidden_in_the_menu_but_Google_Lens_always_stays()
    {
        using var harness = new Harness();

        Assert.True(harness.Store.ShowInMenu(SearchProviderIds.YandexImages, shown: true));
        Assert.True(harness.Store.ShowInMenu(SearchProviderIds.TraceMoe, shown: false));
        Assert.Equal(SearchProviderIds.TraceMoe, harness.Settings.HiddenSearchProviderIds);
        Assert.Equal([SearchProviderIds.GoogleLens, SearchProviderIds.YandexImages],
            harness.Store.MenuProviders(SearchProviderIds.GoogleLens).Select(provider => provider.Id));

        Assert.False(ProviderSelectionStore.CanHideFromMenu(SearchProviderIds.GoogleLens));
        Assert.False(harness.Store.ShowInMenu(SearchProviderIds.GoogleLens, shown: false));
        Assert.True(harness.Store.IsShownInMenu(SearchProviderIds.GoogleLens));
        Assert.Equal(2, harness.SaveCalls);
    }

    [Fact]
    public void A_failed_save_while_hiding_the_selected_provider_keeps_it_selected_and_visible()
    {
        using var harness = new Harness(SearchProviderIds.TraceMoe, saveThrows: true);

        Assert.False(harness.Store.ShowInMenu(SearchProviderIds.TraceMoe, shown: false));

        Assert.Equal(SearchProviderIds.TraceMoe, harness.Settings.SearchProviderId);
        Assert.True(harness.Store.IsShownInMenu(SearchProviderIds.TraceMoe));
    }

    [Fact]
    public void Hiding_the_selected_provider_switches_the_selection_to_Google_Lens()
    {
        using var harness = new Harness(SearchProviderIds.TraceMoe);

        Assert.True(harness.Store.ShowInMenu(SearchProviderIds.TraceMoe, shown: false));

        Assert.Equal(SearchProviderIds.GoogleLens, harness.Settings.SearchProviderId);
        Assert.Equal("yandex-images,trace-moe", harness.Settings.HiddenSearchProviderIds);
        Assert.Equal([SearchProviderIds.GoogleLens], harness.Store.ShownInMenu.Select(provider => provider.Id));
    }

    private sealed class Harness : IDisposable
    {
        private readonly VisualSearchProviderRouter _router;

        public Harness(string providerId = SearchProviderIds.GoogleLens, bool saveThrows = false)
        {
            var path = Path.Combine(TestOutputPaths.TempDirectory, "CircleToSearch.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            var log = new PluginLog(path);
            Service = TestSettings.Create(SettingsValidator.Normalize(new AppSettings { SearchProviderId = providerId }, out _), _ =>
            {
                SaveCalls++;
                if (saveThrows) throw new IOException("disk unavailable");
            });
            _router = new VisualSearchProviderRouter(
                [
                    new VisualSearchProviderRegistration(
                        new SearchProviderDescriptor(SearchProviderIds.GoogleLens, "Google Lens"),
                        () => { ProviderFactoryCalls++; return new FakeProvider(); }),
                    new VisualSearchProviderRegistration(
                        new SearchProviderDescriptor(SearchProviderIds.YandexImages, "Yandex Images"),
                        () => { ProviderFactoryCalls++; return new FakeProvider(); }),
                    new VisualSearchProviderRegistration(
                        new SearchProviderDescriptor(SearchProviderIds.TraceMoe, "trace.moe"),
                        () => { ProviderFactoryCalls++; return new FakeProvider(); }),
                ],
                SearchProviderIds.GoogleLens,
                log);
            Store = new ProviderSelectionStore(
                _router,
                Service,
                Notifier,
                TestUiStrings.English,
                log);
        }

        public ProviderSelectionStore Store { get; }
        public SettingsService Service { get; }
        public AppSettings Settings => Service.Snapshot;
        public TestPluginNotifier Notifier { get; } = new();
        public int SaveCalls { get; private set; }
        public int ProviderFactoryCalls { get; private set; }
        public void Dispose() => _router.Dispose();
    }

    private sealed class FakeProvider : IVisualSearchProvider
    {
        public Task<VisualSearchPreparationOutcome> PrepareAsync(byte[] png, CancellationToken cancel) =>
            Task.FromResult(VisualSearchPreparationOutcome.Ready(
                PreparedVisualSearch.ForUrl(new Uri("https://example.com/results"), null)));
    }
}

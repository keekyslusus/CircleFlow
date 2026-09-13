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

    private sealed class Harness : IDisposable
    {
        private readonly VisualSearchProviderRouter _router;

        public Harness(string providerId = SearchProviderIds.GoogleLens, bool saveThrows = false)
        {
            var path = Path.Combine(Path.GetTempPath(), "CircleToSearch.Tests", Guid.NewGuid().ToString("N"));
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

namespace CircleToSearch.Search;

public sealed class VisualSearchProviderRouter : IDisposable
{
    private readonly IReadOnlyList<Entry> _entries;
    private readonly Dictionary<string, Entry> _entriesById;
    private readonly Entry _defaultEntry;
    private readonly PluginLog _log;
    private readonly object _gate = new();
    private int _activePreparations;
    private bool _disposeStarted;
    private bool _disposeCompleted;

    public VisualSearchProviderRouter(
        IEnumerable<VisualSearchProviderRegistration> registrations,
        string defaultProviderId,
        PluginLog log)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultProviderId);
        ArgumentNullException.ThrowIfNull(log);
        _log = log;

        var entries = new List<Entry>();
        _entriesById = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        foreach (var registration in registrations)
        {
            ArgumentNullException.ThrowIfNull(registration);
            Validate(registration);
            var entry = new Entry(registration);
            if (!_entriesById.TryAdd(registration.Descriptor.Id, entry))
                throw new ArgumentException(
                    $"A visual search provider with ID '{registration.Descriptor.Id}' is already registered.",
                    nameof(registrations));
            entries.Add(entry);
        }

        if (!_entriesById.TryGetValue(defaultProviderId, out _defaultEntry!))
            throw new ArgumentException(
                $"The default visual search provider '{defaultProviderId}' is not registered.",
                nameof(defaultProviderId));

        _entries = entries;
        Providers = Array.AsReadOnly(entries.Select(entry => entry.Descriptor).ToArray());
    }

    public IReadOnlyList<SearchProviderDescriptor> Providers { get; }

    public SearchProviderDescriptor GetEffectiveDescriptor(string? requestedProviderId)
        => Resolve(requestedProviderId).Entry.Descriptor;

    public async Task<RoutedVisualSearchPreparation> PrepareAsync(
        string? requestedProviderId,
        byte[] jpeg,
        CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(jpeg);

        var resolution = Resolve(requestedProviderId);
        IVisualSearchProvider provider;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposeStarted, this);
            provider = resolution.Entry.Provider.Value;
            _activePreparations++;
        }

        try
        {
            if (resolution.UsedFallback)
            {
                var requested = string.IsNullOrWhiteSpace(requestedProviderId)
                    ? "<empty>"
                    : requestedProviderId;
                _log.Warn(
                    nameof(VisualSearchProviderRouter),
                    $"visual search provider '{requested}' is unavailable; using '{resolution.Entry.Descriptor.Id}'");
            }

            var outcome = await provider.PrepareAsync(jpeg, cancel).ConfigureAwait(false);
            return new RoutedVisualSearchPreparation(
                resolution.Entry.Descriptor.Id,
                resolution.Entry.Descriptor.DisplayName,
                outcome,
                resolution.UsedFallback);
        }
        finally
        {
            lock (_gate)
            {
                _activePreparations--;
                if (_activePreparations == 0) Monitor.PulseAll(_gate);
            }
        }
    }

    public void Dispose()
    {
        List<(SearchProviderDescriptor Descriptor, IDisposable Provider)> providers;
        lock (_gate)
        {
            if (_disposeCompleted) return;
            if (_disposeStarted)
            {
                while (!_disposeCompleted) Monitor.Wait(_gate);
                return;
            }

            _disposeStarted = true;
            while (_activePreparations > 0) Monitor.Wait(_gate);

            providers = [];
            foreach (var entry in _entries)
            {
                if (!entry.Provider.IsValueCreated || entry.Provider.Value is not IDisposable disposable)
                    continue;
                providers.Add((entry.Descriptor, disposable));
            }
        }

        try
        {
            foreach (var provider in providers)
            {
                try
                {
                    provider.Provider.Dispose();
                }
                catch (Exception exception)
                {
                    _log.Error(
                        nameof(VisualSearchProviderRouter),
                        $"disposing visual search provider '{provider.Descriptor.Id}' failed",
                        exception);
                }
            }
        }
        finally
        {
            lock (_gate)
            {
                _disposeCompleted = true;
                Monitor.PulseAll(_gate);
            }
        }
    }

    private Resolution Resolve(string? requestedProviderId)
    {
        if (!string.IsNullOrWhiteSpace(requestedProviderId)
            && _entriesById.TryGetValue(requestedProviderId, out var entry))
        {
            return new Resolution(entry, UsedFallback: false);
        }

        return new Resolution(_defaultEntry, UsedFallback: true);
    }

    private static void Validate(VisualSearchProviderRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration.Descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(registration.Descriptor.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(registration.Descriptor.DisplayName);
        ArgumentNullException.ThrowIfNull(registration.Factory);
    }

    private sealed class Entry
    {
        public Entry(VisualSearchProviderRegistration registration)
        {
            Descriptor = registration.Descriptor;
            Provider = new Lazy<IVisualSearchProvider>(
                () => registration.Factory()
                      ?? throw new InvalidOperationException(
                          $"Visual search provider factory '{registration.Descriptor.Id}' returned null."),
                LazyThreadSafetyMode.ExecutionAndPublication);
        }

        public SearchProviderDescriptor Descriptor { get; }

        public Lazy<IVisualSearchProvider> Provider { get; }
    }

    private sealed record Resolution(Entry Entry, bool UsedFallback);
}

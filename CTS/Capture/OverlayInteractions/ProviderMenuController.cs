using System.Windows;
using System.Windows.Controls;
using CircleToSearch.Search;
using CircleToSearch.Ui;

namespace CircleToSearch.Capture.OverlayInteractions;

internal sealed class ProviderMenuController : IDisposable
{
    private readonly ProviderMenuVisual? _visual;
    private readonly Grid _coordinateRoot;
    private readonly IReadOnlyList<SearchProviderDescriptor> _providers;
    private readonly UiStrings _strings;
    private readonly bool _lightTheme;
    private readonly Func<bool> _canInteract;
    private readonly Action<string> _providerSelected;
    private readonly Action<Action> _changeTrayLayout;
    private readonly List<Button> _menuItems = [];
    private bool _disposed;

    internal ProviderMenuController(
        ProviderMenuVisual? visual,
        Grid coordinateRoot,
        IReadOnlyList<SearchProviderDescriptor> providers,
        string selectedProviderId,
        UiStrings strings,
        bool lightTheme,
        Func<bool> canInteract,
        Action<string> providerSelected,
        Action<Action> changeTrayLayout)
    {
        _visual = visual;
        _coordinateRoot = coordinateRoot;
        _providers = providers;
        _strings = strings;
        _lightTheme = lightTheme;
        _canInteract = canInteract;
        _providerSelected = providerSelected;
        _changeTrayLayout = changeTrayLayout;
        SelectedProviderId = selectedProviderId;

        if (_visual is null) return;
        _visual.Button.Click += OnProviderButtonClick;
        AttachMenuItemHandlers();
    }

    internal string SelectedProviderId { get; private set; }

    internal bool IsOpen { get; private set; }

    internal void SetEnabled(bool enabled)
    {
        if (_disposed || _visual is null) return;
        _visual.Button.IsEnabled = enabled;
    }

    internal void SetOpen(bool open)
    {
        if (_disposed || _visual is null) return;
        IsOpen = open;
        ProviderMenuVisualPresenter.SetOpen(_visual, _coordinateRoot, open);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_visual is not null) _visual.Button.Click -= OnProviderButtonClick;
        DetachMenuItemHandlers();
    }

    private void OnProviderButtonClick(object sender, RoutedEventArgs e)
    {
        // Every other provider can be hidden from the menu, leaving nothing to switch to.
        if (_disposed || !_canInteract() || _menuItems.Count == 0) return;
        SetOpen(!IsOpen);
        e.Handled = true;
    }

    private void OnProviderMenuItemClick(object sender, RoutedEventArgs e)
    {
        if (_disposed || !_canInteract() || sender is not Button { Tag: string providerId }) return;
        var descriptor = _providers.FirstOrDefault(provider =>
            string.Equals(provider.Id, providerId, StringComparison.OrdinalIgnoreCase));
        if (descriptor is null || _visual is null) return;

        SelectedProviderId = descriptor.Id;
        DetachMenuItemHandlers();
        _changeTrayLayout(() => ProviderMenuVisualPresenter.UpdateProvider(
            _visual,
            _providers,
            SelectedProviderId,
            _strings,
            _lightTheme));
        AttachMenuItemHandlers();
        SetOpen(false);
        _providerSelected(SelectedProviderId);
        e.Handled = true;
    }

    private void AttachMenuItemHandlers()
    {
        if (_visual?.Menu.Child is not StackPanel panel) return;
        foreach (var item in panel.Children.OfType<Button>())
        {
            item.Click += OnProviderMenuItemClick;
            _menuItems.Add(item);
        }
    }

    private void DetachMenuItemHandlers()
    {
        foreach (var item in _menuItems) item.Click -= OnProviderMenuItemClick;
        _menuItems.Clear();
    }
}

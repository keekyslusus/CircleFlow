using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using CircleToSearch.Capture;
using CircleToSearch.Search;
using CircleToSearch.Ui;

namespace CircleToSearch.Shell.SettingsPreview;

// The dialog that chooses which providers the selection toolbar's provider menu offers.
internal sealed class SettingsProviderMenuPanel
{
    private readonly FrameworkElement _root;
    private readonly SettingsWindowModel _model;
    private readonly UiStrings _strings;
    private readonly bool _lightTheme;
    private readonly Action<string> _showStatus;
    private readonly Action _changed;
    private readonly SettingsCollapseMotion _hiddenSection;
    private readonly Dictionary<string, ProviderRows> _rows = [];

    internal SettingsProviderMenuPanel(FrameworkElement root, SettingsWindowModel model, UiStrings strings,
        bool lightTheme, Action<string> showStatus, Action changed)
    {
        _root = root;
        _model = model;
        _strings = strings;
        _lightTheme = lightTheme;
        _showStatus = showStatus;
        _changed = changed;
        _hiddenSection = new SettingsCollapseMotion(Element<FrameworkElement>("HiddenProviderSection"));
        root.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnClick));
    }

    internal void ApplyTexts()
    {
        _root.Resources["ProviderHideLabel"] = _strings.SettingsPreviewText("hide_provider");
        _root.Resources["ProviderShowLabel"] = _strings.SettingsPreviewText("show_provider");
        _root.Resources["ProviderAlwaysShownLabel"] = _strings.SettingsPreviewText("provider_always_shown");
        BuildRows();
    }

    // Unfolds each provider's row in the list it now belongs to and folds its other row, so a move slides the
    // rows around it and resizes the dialog instead of jumping.
    internal void Refresh()
    {
        var anyHidden = false;
        foreach (var (id, rows) in _rows)
        {
            var shown = _model.IsProviderShownInMenu(id);
            anyHidden |= !shown;
            rows.ShownMotion.Set(shown);
            rows.HiddenMotion.Set(!shown);
        }
        _hiddenSection.Set(anyHidden);
    }

    // Every provider has a row in both lists, so moving one never creates or removes a row mid-animation.
    private void BuildRows()
    {
        var shownList = Element<Panel>("ShownProviders");
        var hiddenList = Element<Panel>("HiddenProviders");
        shownList.Children.Clear();
        hiddenList.Children.Clear();
        _rows.Clear();
        foreach (var descriptor in _model.Providers)
        {
            var shownRow = AddRow(shownList, descriptor, shown: true);
            var hiddenRow = AddRow(hiddenList, descriptor, shown: false);
            _rows[descriptor.Id] = new ProviderRows(shownRow, new SettingsCollapseMotion(shownRow),
                hiddenRow, new SettingsCollapseMotion(hiddenRow));
        }
        Refresh();
    }

    private ContentPresenter AddRow(Panel list, SearchProviderDescriptor descriptor, bool shown)
    {
        var label = ProviderVisualCatalog.Label(descriptor, _strings);
        var row = new ContentPresenter
        {
            Content = new ProviderRow(
                descriptor.Id,
                ProviderVisualCatalog.CreateProviderMark(descriptor.Id, _lightTheme),
                label,
                ProviderVisualCatalog.Detail(descriptor, _strings),
                shown,
                _model.CanHideProviderFromMenu(descriptor.Id),
                _strings.SettingsHideProvider(label),
                _strings.SettingsShowProvider(label)),
            ContentTemplate = (DataTemplate)_root.FindResource("ProviderMenuRow"),
        };
        list.Children.Add(row);
        return row;
    }

    private void OnClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not Button { DataContext: ProviderRow row, Tag: string action }) return;
        bool shown;
        switch (action)
        {
            case "provider-hide": shown = false; break;
            case "provider-show": shown = true; break;
            default: return;
        }
        if (!_model.ShowProviderInMenu(row.Id, shown)) _showStatus(_strings.StorageSaveFailed);
        Refresh();
        _changed();
        FocusAction(row.Id);
        e.Handled = true;
    }

    // The clicked row folds away, so keyboard focus goes to the button that undoes the move.
    private void FocusAction(string providerId)
    {
        var shown = _model.IsProviderShownInMenu(providerId);
        var row = shown ? _rows[providerId].ShownRow : _rows[providerId].HiddenRow;
        row.ApplyTemplate();
        _root.UpdateLayout();
        if (row.ContentTemplate.FindName(shown ? "Hide" : "Show", row) is Button button) button.Focus();
    }

    private T Element<T>(string name) where T : FrameworkElement => (T)_root.FindName(name);

    private sealed record ProviderRows(ContentPresenter ShownRow, SettingsCollapseMotion ShownMotion,
        ContentPresenter HiddenRow, SettingsCollapseMotion HiddenMotion);

    private sealed record ProviderRow(string Id, FrameworkElement Mark, string Label, string Detail, bool Shown,
        bool CanHide, string HideLabel, string ShowLabel);
}

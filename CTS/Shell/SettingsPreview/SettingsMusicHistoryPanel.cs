using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CircleToSearch.Capture;
using CircleToSearch.MusicRecognition;
using CircleToSearch.Ui;

namespace CircleToSearch.Shell.SettingsPreview;

// The recognized tracks on the Music page: grouped by day, filtered by the search box.
internal sealed class SettingsMusicHistoryPanel
{
    private const int CoverPixels = 64;
    private readonly SettingsWindowModel _model;
    private readonly UiStrings _strings;
    private readonly FrameworkElement _root;
    private readonly TextBox _search;
    private readonly ClipboardCopyService _clipboardCopy;
    private readonly Dictionary<string, ImageSource?> _covers = new(StringComparer.Ordinal);
    private IReadOnlyList<MusicHistoryEntry> _shownEntries = [];
    private DateTime _shownDay;

    internal SettingsMusicHistoryPanel(FrameworkElement root, SettingsWindowModel model, UiStrings strings,
        ClipboardCopyService clipboardCopy)
    {
        _root = root;
        _model = model;
        _strings = strings;
        _search = Element<TextBox>("HistorySearch");
        _clipboardCopy = clipboardCopy;
        _search.TextChanged += (_, _) => Refresh();
        root.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnClick));
    }

    internal void ApplyTexts()
    {
        _root.Resources["HistoryCopyLabel"] = _strings.CopyTrackInfo;
        _root.Resources["HistoryOpenLabel"] = _strings.OpenInShazam;
        Refresh();
    }

    // Rebuilding every row loses keyboard focus and hover, so skip it when the list would look the same.
    internal void RefreshIfOutdated()
    {
        if (Today() != _shownDay || !_model.MusicHistory.SequenceEqual(_shownEntries)) Refresh();
    }

    internal void Refresh()
    {
        var entries = _model.MusicHistory;
        _shownEntries = entries;
        _shownDay = Today();
        if (entries.Count == 0 && _search.Text.Length != 0)
        {
            // Clearing the text raises TextChanged, which refreshes again.
            _search.Text = string.Empty;
            return;
        }
        var query = _search.Text.Trim();
        var found = entries.Where(entry => Matches(entry, query)).ToArray();
        Element<FrameworkElement>("HistoryToolbar").Visibility = Visible(entries.Count != 0);
        Element<FrameworkElement>("HistoryEmpty").Visibility = Visible(entries.Count == 0);
        Element<FrameworkElement>("HistoryNoMatch").Visibility = Visible(entries.Count != 0 && found.Length == 0);
        Element<TextBlock>("HistoryNoMatchText").Text = _strings.SettingsMusicHistoryNoMatch(query);
        Element<ItemsControl>("HistoryDays").ItemsSource = Days(found);
    }

    private HistoryDay[] Days(IReadOnlyList<MusicHistoryEntry> entries)
    {
        var time = _model.Time;
        var culture = CultureInfo.CurrentCulture;
        var today = Today();
        return entries
            .GroupBy(entry => TimeZoneInfo.ConvertTime(entry.RecognizedAt, time.LocalTimeZone).Date)
            .Select(day => new HistoryDay(
                DayLabel(day.Key, today, culture),
                day.Select((entry, index) => new HistoryRow(
                    entry,
                    TimeZoneInfo.ConvertTime(entry.RecognizedAt, time.LocalTimeZone).ToString("t", culture),
                    entry.Track.Title,
                    entry.Track.Artist,
                    string.IsNullOrWhiteSpace(entry.Track.Genre) ? null : entry.Track.Genre,
                    Cover(entry.Track.CoverUrl),
                    _model.CanOpenInShazam(entry),
                    index == 0)).ToArray()))
            .ToArray();
    }

    private DateTime Today() => TimeZoneInfo.ConvertTime(_model.Time.GetUtcNow(), _model.Time.LocalTimeZone).Date;

    private string DayLabel(DateTime day, DateTime today, CultureInfo culture)
    {
        if (day == today) return _strings.SettingsPreviewText("history_today");
        if (day == today.AddDays(-1)) return _strings.SettingsPreviewText("history_yesterday");
        var label = day.Year == today.Year
            ? $"{day.ToString("dddd", culture)}, {day.ToString(culture.DateTimeFormat.MonthDayPattern, culture)}"
            : day.ToString("D", culture);
        return char.ToUpper(label[0], culture) + label[1..];
    }

    private static bool Matches(MusicHistoryEntry entry, string query) =>
        query.Length == 0
        || new[] { entry.Track.Title, entry.Track.Artist, entry.Track.Album }
            .Any(text => text?.Contains(query, StringComparison.CurrentCultureIgnoreCase) == true);

    // Cached, so typing in the search box does not download the covers again.
    private ImageSource? Cover(string? url)
    {
        if (url is null) return null;
        if (_covers.TryGetValue(url, out var cached)) return cached;
        ImageSource? cover = null;
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
        {
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.UriSource = uri;
                image.DecodePixelWidth = CoverPixels;
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.EndInit();
                cover = image;
            }
            catch (Exception exception) when (exception is NotSupportedException or InvalidOperationException) { }
        }
        return _covers[url] = cover;
    }

    private void OnClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not Button { DataContext: HistoryRow row, Tag: string action }) return;
        switch (action)
        {
            case "history-copy": _clipboardCopy.TryCopy(row.Entry.Track.TrackInfo); break;
            case "history-open": _model.OpenInShazam(row.Entry); break;
            default: return;
        }
        e.Handled = true;
    }

    private T Element<T>(string name) where T : FrameworkElement => (T)_root.FindName(name);

    private static Visibility Visible(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    private sealed record HistoryDay(string Label, IReadOnlyList<HistoryRow> Tracks);

    private sealed record HistoryRow(MusicHistoryEntry Entry, string Time, string Title, string Artist, string? Genre,
        ImageSource? Cover, bool CanOpen, bool IsFirst);
}

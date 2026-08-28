namespace CircleToSearch.Ui;

using System.Globalization;

public static class UiStrings
{
    public static string SelectionPrompt => Localized("Выделите область", "Select an area");

    public static string CancelKeyName => Localized("Esc", "Esc");

    public static string CancelAction => Localized("отмена", "cancel");

    public static string VisualSearchQuerySubtitle(string hotkeyStatus) => Localized(
        $"Визуальный поиск по области — горячая клавиша: {hotkeyStatus}",
        $"Visually search the selected region — hotkey: {hotkeyStatus}");

    public static string SearchProvider(string displayName) => Localized(
        $"Поисковый сервис: {displayName}",
        $"Search provider: {displayName}");

    public static string GoogleLensRuntime(string? version) => version is null
        ? Localized(
            "Среда WebView2 для Google Lens: не найдена",
            "Google Lens WebView2 Runtime: not detected")
        : Localized(
            $"Среда WebView2 для Google Lens: {version}",
            $"Google Lens WebView2 Runtime: {version}");

    private static string Localized(string russian, string english) =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("ru", StringComparison.OrdinalIgnoreCase)
            ? russian
            : english;
}

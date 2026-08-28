namespace CircleToSearch.Ui;

using System.Globalization;

public static class UiStrings
{
    public static string SelectionPrompt => Localized("Выделите область", "Select an area");

    public static string CancelKeyName => Localized("Esc", "Esc");

    public static string CancelAction => Localized("отмена", "cancel");

    private static string Localized(string russian, string english) =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("ru", StringComparison.OrdinalIgnoreCase)
            ? russian
            : english;
}

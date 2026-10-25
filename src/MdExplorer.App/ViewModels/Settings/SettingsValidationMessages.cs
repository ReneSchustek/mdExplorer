using MdExplorer.App.Localization;
using MdExplorer.Core.Settings;

namespace MdExplorer.App.ViewModels.Settings;

/// <summary>
/// Übersetzt einen Befund des <see cref="SettingsValidator"/> in die Zeile, die der Nutzer liest.
/// </summary>
/// <remarks>
/// Der Core meldet Feld und Regel, die Worte kommen von hier. Jeder Schlüssel steht wörtlich im
/// Code, damit der Ressourcen-Wächter ihn findet; ein zusammengesetzter Schlüssel fiele ihm durch.
/// </remarks>
internal static class SettingsValidationMessages
{
    /// <summary>Liefert die Meldungszeile zu <paramref name="error"/>.</summary>
    /// <param name="error">Der Befund.</param>
    /// <returns>Feld und Regel als ein Satz.</returns>
    public static string Format(SettingsValidationError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return UiText.Format("Settings_Validation_Line", FieldLabel(error), RuleText(error));
    }

    // Listen zählen für den Nutzer ab 1, nicht ab 0.
    private static string FieldLabel(SettingsValidationError error) => error.Field switch
    {
        SettingsField.Roots => UiText.Format("Settings_ValidationField_Roots", error.Index + 1),
        SettingsField.ExclusionPatterns => UiText.Format("Settings_ValidationField_ExclusionPatterns", error.Index + 1),
        SettingsField.UiExcludedFolders => UiText.Format("Settings_ValidationField_UiExcludedFolders", error.Index + 1),
        SettingsField.PreviewFontSize => UiText.Get("Settings_ValidationField_PreviewFontSize"),
        SettingsField.ResultsPerPage => UiText.Get("Settings_ValidationField_ResultsPerPage"),
        SettingsField.SearchDebounceMs => UiText.Get("Settings_ValidationField_SearchDebounceMs"),
        SettingsField.IndexerResyncIntervalSeconds => UiText.Get("Settings_ValidationField_IndexerResyncIntervalSeconds"),
        SettingsField.Theme => UiText.Get("Settings_ValidationField_Theme"),
        _ => error.Field.ToString(),
    };

    private static string RuleText(SettingsValidationError error) => error.Rule switch
    {
        SettingsRule.ValueEmpty => UiText.Get("Settings_ValidationRule_ValueEmpty"),
        SettingsRule.NegationWithoutPattern => UiText.Get("Settings_ValidationRule_NegationWithoutPattern"),
        SettingsRule.OutOfRange => UiText.Format("Settings_ValidationRule_OutOfRange", error.Minimum, error.Maximum),
        _ => RuleTextWithValue(error),
    };

    // Die Regeln, deren Meldung den beanstandeten Wert nennt.
    private static string RuleTextWithValue(SettingsValidationError error) => error.Rule switch
    {
        SettingsRule.PathNotAbsolute => UiText.Format("Settings_ValidationRule_PathNotAbsolute", error.Value),
        SettingsRule.DirectoryMissing => UiText.Format("Settings_ValidationRule_DirectoryMissing", error.Value),
        SettingsRule.PatternInvalid => UiText.Format("Settings_ValidationRule_PatternInvalid", error.Value),
        SettingsRule.PatternRooted => UiText.Format("Settings_ValidationRule_PatternRooted", error.Value),
        SettingsRule.PatternUnsupportedSyntax => UiText.Format("Settings_ValidationRule_PatternUnsupportedSyntax", error.Value),
        SettingsRule.UnknownValue => UiText.Format("Settings_ValidationRule_UnknownValue", error.Value),
        _ => error.Rule.ToString(),
    };
}

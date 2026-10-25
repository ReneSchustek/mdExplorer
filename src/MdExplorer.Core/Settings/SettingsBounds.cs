using MdExplorer.Core.Models;

namespace MdExplorer.Core.Settings;

/// <summary>
/// Die Wertebereiche der Zahlen-Einstellungen an einer Stelle.
/// </summary>
/// <remarks>
/// Geprüft wird an drei Stellen: im Einstellungsdialog vor dem Speichern, beim Speichern selbst
/// (ein Aufrufer ohne Dialog geht sonst an der Prüfung vorbei) und beim Laden einer von Hand
/// bearbeiteten Datei. Stünden die Prüfungen getrennt, liefen sie auseinander, sobald eine Grenze
/// sich ändert.
/// </remarks>
public static class SettingsBounds
{
    /// <summary>Alle begrenzten Zahlen-Einstellungen mit Wert, Grenzen und Vorgabe.</summary>
    /// <param name="settings">Der zu prüfende Stand.</param>
    /// <returns>Eine Zeile je begrenztem Feld.</returns>
    public static IReadOnlyList<BoundedValue> Of(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        AppearanceSettings appearance = settings.Appearance;
        BehaviorSettings behavior = settings.Behavior;

        return
        [
            new(SettingsField.PreviewFontSize, appearance.PreviewFontSize, AppearanceSettings.MinPreviewFontSize, AppearanceSettings.MaxPreviewFontSize, AppearanceSettings.Default.PreviewFontSize),
            new(SettingsField.ResultsPerPage, appearance.ResultsPerPage, AppearanceSettings.MinResultsPerPage, AppearanceSettings.MaxResultsPerPage, AppearanceSettings.Default.ResultsPerPage),
            new(SettingsField.SearchDebounceMs, behavior.SearchDebounceMs, BehaviorSettings.MinSearchDebounceMs, BehaviorSettings.MaxSearchDebounceMs, BehaviorSettings.Default.SearchDebounceMs),
            new(SettingsField.IndexerResyncIntervalSeconds, behavior.IndexerResyncIntervalSeconds, BehaviorSettings.MinIndexerResyncIntervalSeconds, BehaviorSettings.MaxIndexerResyncIntervalSeconds, BehaviorSettings.Default.IndexerResyncIntervalSeconds),
        ];
    }
}

/// <summary>Eine begrenzte Zahlen-Einstellung.</summary>
/// <param name="Field">Welches Feld.</param>
/// <param name="Value">Der aktuelle Wert.</param>
/// <param name="Minimum">Kleinster zulässiger Wert.</param>
/// <param name="Maximum">Größter zulässiger Wert.</param>
/// <param name="Fallback">Vorgabe, die beim Laden an die Stelle eines Werts außerhalb tritt.</param>
public sealed record BoundedValue(SettingsField Field, int Value, int Minimum, int Maximum, int Fallback)
{
    /// <summary><see langword="true"/>, wenn der Wert innerhalb seiner Grenzen liegt.</summary>
    public bool IsWithinBounds => Value >= Minimum && Value <= Maximum;
}

using System.ComponentModel.DataAnnotations;

namespace MdExplorer.Core.Models;

/// <summary>
/// Wurzel-Datensatz für die Settings-Datei <c>settings.json</c> unterhalb
/// <c>%LOCALAPPDATA%\MdExplorer\</c>. Fehlt die Datei oder ist sie unlesbar, gilt
/// <see cref="Default"/>. Eine Datei mit älterer <see cref="SchemaVersion"/> wird beim Laden
/// auf den aktuellen Stand gehoben; Felder, die sie noch nicht kennt, bekommen ihren Standardwert.
/// </summary>
/// <param name="SchemaVersion">Versionsnummer des persistierten Schemas, erlaubt Migrations-Pipeline.</param>
/// <param name="Indexing">Indexierungsbezogene Einstellungen (Roots + Excludes).</param>
/// <param name="Appearance">Darstellungsbezogene Einstellungen.</param>
/// <param name="Behavior">Verhaltensbezogene Einstellungen.</param>
public sealed record AppSettings(
    int SchemaVersion,
    IndexingSettings Indexing,
    AppearanceSettings Appearance,
    BehaviorSettings Behavior)
{
    /// <summary>
    /// Aktuell unterstützte Schema-Version. Sie steigt, sobald ein neues Feld einen Standardwert
    /// braucht, den die Deserialisierung nicht von selbst liefert (2: Hashtags, 3: Update-Prüfung).
    /// </summary>
    public const int CurrentSchemaVersion = 3;

    /// <summary>
    /// Liefert eine vollständig defaultierte Settings-Instanz. Wird verwendet,
    /// wenn keine Datei existiert oder das Laden fehlschlägt.
    /// </summary>
    public static AppSettings Default => new(
        CurrentSchemaVersion,
        IndexingSettings.Default,
        AppearanceSettings.Default,
        BehaviorSettings.Default);
}

using System.ComponentModel.DataAnnotations;

namespace MdExplorer.Core.Models;

/// <summary>Settings für Darstellung (Theme, Schriftgröße, Trefferanzahl).</summary>
/// <param name="Theme">Theme-Wahl.</param>
/// <param name="PreviewFontSize">Schriftgröße der Preview in Pixel.</param>
/// <param name="ResultsPerPage">Anzahl der angezeigten Suchtreffer.</param>
/// <remarks>
/// Die Grenzen stehen als Konstanten hier und an keiner zweiten Stelle. Die Einstellungen kommen
/// aus einer JSON-Datei, nicht aus dem Optionssystem, das die <c>Range</c>-Angaben auswerten
/// würde. Geprüft wird deshalb zweimal: vor dem Speichern im <c>SettingsValidator</c> und beim
/// Laden im <c>JsonSettingsService</c>, der einen Wert außerhalb auf die Vorgabe zurücksetzt.
/// </remarks>
public sealed record AppearanceSettings(
    AppTheme Theme,
    [property: Range(AppearanceSettings.MinPreviewFontSize, AppearanceSettings.MaxPreviewFontSize)] int PreviewFontSize,
    [property: Range(AppearanceSettings.MinResultsPerPage, AppearanceSettings.MaxResultsPerPage)] int ResultsPerPage)
{
    /// <summary>Kleinste Schriftgröße der Vorschau in Pixel; darunter ist Text nicht mehr lesbar.</summary>
    public const int MinPreviewFontSize = 8;

    /// <summary>Größte Schriftgröße der Vorschau in Pixel; darüber passt kaum ein Wort in die Spalte.</summary>
    public const int MaxPreviewFontSize = 64;

    /// <summary>Kleinste Trefferzahl je Seite.</summary>
    public const int MinResultsPerPage = 10;

    /// <summary>Größte Trefferzahl je Seite; die Liste bleibt damit in einem Zug ladbar.</summary>
    public const int MaxResultsPerPage = 1_000;

    /// <summary>
    /// Default-Schriftgröße der Preview in Pixel. 16 px ist auch die Grundschrift eines Browsers;
    /// die Vorschau beginnt damit in der Größe, in der dieselbe Datei anderswo erscheint.
    /// </summary>
    private const int DefaultPreviewFontSize = 16;

    /// <summary>Default-Anzahl der angezeigten Suchtreffer.</summary>
    private const int DefaultResultsPerPage = 50;

    /// <summary>System-Theme, 16 px Preview, 50 Treffer/Seite.</summary>
    public static AppearanceSettings Default => new(AppTheme.System, DefaultPreviewFontSize, DefaultResultsPerPage);
}

using System.Text.Json.Serialization;

namespace MdExplorer.App.Services;

/// <summary>
/// Persistierte UI-Layout-Daten: Spaltenbreiten des Hauptfensters sowie
/// Position und Größe des Hilfefensters. Werden in <see cref="UiSettingsStore"/>
/// im <c>%LOCALAPPDATA%</c>-Ordner serialisiert und beim Start zurückgeladen.
/// </summary>
/// <param name="FolderColumnWidth">Breite der Folder-Tree-Spalte in geräteunabhängigen Einheiten.</param>
/// <param name="PreviewColumnWidth">Breite der Vorschau-Spalte in geräteunabhängigen Einheiten.</param>
/// <param name="HelpWindow">Geometrie des Hilfefensters; <see langword="null"/>, solange
/// das Fenster noch nie geöffnet wurde; dann gilt die Default-Geometrie.</param>
/// <param name="IsTagCloudVisible">Ob das Tag-Cloud-Panel sichtbar ist. Default sichtbar.</param>
/// <param name="LeftTabIndex">Zuletzt gewählter Tab in der linken Spalte (0=Ordner, 1=Alle Dateien, 2=Suche).</param>
/// <param name="GraphPathPrefix">Zuletzt im Graph-Fenster gesetzter Pfad-Prefix-Filter. <see langword="null"/>
/// oder leer = kein Filter. Persistierung erlaubt Wiederkehr in die zuletzt fokussierte Sicht.</param>
/// <param name="RecentFiles">Verlauf der zuletzt geöffneten Dateien, jüngste zuerst; <see langword="null"/>,
/// solange noch keine geöffnet war.</param>
internal sealed record UiLayout(
    double FolderColumnWidth,
    double PreviewColumnWidth,
    WindowGeometry? HelpWindow = null,
    bool IsTagCloudVisible = true,
    int LeftTabIndex = 0,
    string? GraphPathPrefix = null,
    IReadOnlyList<string>? RecentFiles = null)
{
    // Die Ordnerspalte bekommt ein Viertel der Startbreite des Hauptfensters von 1280. Die
    // Vorschau ist die einzige Sternspalte und füllt den Rest; ihr Wert wirkt nur als Gewicht.
    private const double DefaultFolderColumnWidth = 320;
    private const double DefaultPreviewColumnWidth = 512;

    // Die linke Spalte hat drei Register: Ordner, Alle Dateien, Suche.
    private const int LeftTabCount = 3;

    /// <summary>Voreinstellung, solange noch kein Layout gespeichert ist.</summary>
    public static UiLayout Default { get; } = new(DefaultFolderColumnWidth, DefaultPreviewColumnWidth, HelpWindow: null, IsTagCloudVisible: true, LeftTabIndex: 0, GraphPathPrefix: null);

    /// <summary>
    /// Ersetzt jeden Wert, mit dem das Fenster nicht aufgebaut werden kann, durch seine Voreinstellung.
    /// </summary>
    /// <remarks>
    /// Die Datei liegt im Profil des Nutzers und lässt sich von Hand bearbeiten. Eine negative Breite
    /// ließe das Hauptfenster beim Aufbau werfen, und zwar bei jedem Start. Jeder Wert fällt einzeln
    /// zurück; was brauchbar ist, bleibt.
    /// </remarks>
    public UiLayout Sanitized() => this with
    {
        FolderColumnWidth = IsUsableLength(FolderColumnWidth) ? FolderColumnWidth : DefaultFolderColumnWidth,
        PreviewColumnWidth = IsUsableLength(PreviewColumnWidth) ? PreviewColumnWidth : DefaultPreviewColumnWidth,
        HelpWindow = HelpWindow is { IsUsable: true } ? HelpWindow : null,
        LeftTabIndex = LeftTabIndex is >= 0 and < LeftTabCount ? LeftTabIndex : 0,
        RecentFiles = RecentFiles is null ? null : [.. RecentFiles.Where(path => !string.IsNullOrWhiteSpace(path))],
    };

    /// <summary>Ob eine Länge ein Fenster oder eine Spalte tragen kann: endlich und größer als null.</summary>
    internal static bool IsUsableLength(double value) => double.IsFinite(value) && value > 0;
}

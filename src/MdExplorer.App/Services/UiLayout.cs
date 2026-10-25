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
internal sealed record UiLayout(
    double FolderColumnWidth,
    double PreviewColumnWidth,
    WindowGeometry? HelpWindow = null,
    bool IsTagCloudVisible = true,
    int LeftTabIndex = 0,
    string? GraphPathPrefix = null)
{
    // Die Ordnerspalte bekommt ein Viertel der Startbreite des Hauptfensters von 1280. Die
    // Vorschau ist die einzige Sternspalte und füllt den Rest; ihr Wert wirkt nur als Gewicht.
    private const double DefaultFolderColumnWidth = 320;
    private const double DefaultPreviewColumnWidth = 512;

    /// <summary>Voreinstellung, solange noch kein Layout gespeichert ist.</summary>
    public static UiLayout Default { get; } = new(DefaultFolderColumnWidth, DefaultPreviewColumnWidth, HelpWindow: null, IsTagCloudVisible: true, LeftTabIndex: 0, GraphPathPrefix: null);
}

/// <summary>Geometrie eines persistierten Fensters in geräteunabhängigen Einheiten (1/96 Zoll), wie WPF sie führt.</summary>
/// <param name="Left">Linke Kante.</param>
/// <param name="Top">Obere Kante.</param>
/// <param name="Width">Breite.</param>
/// <param name="Height">Höhe.</param>
internal sealed record WindowGeometry(double Left, double Top, double Width, double Height);

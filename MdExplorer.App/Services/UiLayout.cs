namespace MdExplorer.App.Services;

/// <summary>
/// Persistierte UI-Layout-Daten: Spaltenbreiten des Hauptfensters sowie
/// Position und Größe des Hilfefensters. Werden in <see cref="UiSettingsStore"/>
/// im <c>%LOCALAPPDATA%</c>-Ordner serialisiert und beim Start zurückgeladen.
/// </summary>
/// <param name="FolderColumnWidth">Breite der Folder-Tree-Spalte in Pixel.</param>
/// <param name="ResultColumnWidth">Breite der Trefferlisten-Spalte in Pixel.</param>
/// <param name="PreviewColumnWidth">Breite der Vorschau-Spalte in Pixel.</param>
/// <param name="HelpWindow">Geometrie des Hilfefensters; <see langword="null"/>, solange
/// das Fenster noch nie geöffnet wurde — dann gilt die Default-Geometrie.</param>
/// <param name="IsTagCloudVisible">Ob das Tag-Cloud-Panel sichtbar ist. Default sichtbar.</param>
/// <param name="LeftTabIndex">Zuletzt gewaehlter Tab in der linken Spalte (0=Ordner, 1=Alle Dateien, 2=Suche).</param>
/// <param name="GraphPathPrefix">Zuletzt im Graph-Fenster gesetzter Pfad-Prefix-Filter. <see langword="null"/>
/// oder leer = kein Filter. Persistierung erlaubt Wiederkehr in die zuletzt fokussierte Sicht.</param>
internal sealed record UiLayout(
    double FolderColumnWidth,
    double ResultColumnWidth,
    double PreviewColumnWidth,
    WindowGeometry? HelpWindow = null,
    bool IsTagCloudVisible = true,
    int LeftTabIndex = 0,
    string? GraphPathPrefix = null)
{
    /// <summary>Voreingestellte Werte (entsprechen den Anteilen 25 / 35 / 40 % bei 1280 px).</summary>
    public static UiLayout Default { get; } = new(320, 448, 512, HelpWindow: null, IsTagCloudVisible: true, LeftTabIndex: 0, GraphPathPrefix: null);
}

/// <summary>Geometrie eines persistierten Fensters in Bildschirm-Pixeln.</summary>
/// <param name="Left">Linke Kante in Pixel.</param>
/// <param name="Top">Obere Kante in Pixel.</param>
/// <param name="Width">Breite in Pixel.</param>
/// <param name="Height">Höhe in Pixel.</param>
internal sealed record WindowGeometry(double Left, double Top, double Width, double Height);

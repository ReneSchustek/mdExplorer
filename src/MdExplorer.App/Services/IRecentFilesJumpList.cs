namespace MdExplorer.App.Services;

/// <summary>Zeigt den Verlauf in der Sprungliste des Taskleistensymbols.</summary>
internal interface IRecentFilesJumpList
{
    /// <summary>Ersetzt die Einträge der Sprungliste durch diese Dateien; eine leere Liste leert sie.</summary>
    /// <param name="paths">Die Pfade, jüngste zuerst.</param>
    void Show(IReadOnlyList<string> paths);
}

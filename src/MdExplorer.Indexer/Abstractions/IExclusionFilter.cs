namespace MdExplorer.Indexer.Abstractions;

/// <summary>
/// Entscheidet, ob eine konkrete Markdown-Datei vom Index ausgeschlossen ist.
/// Kombiniert globale Glob-Muster aus den Settings mit der
/// <c>.mdignore</c>-Hierarchie unterhalb des jeweiligen Roots.
/// </summary>
public interface IExclusionFilter
{
    /// <summary>
    /// Liefert <see langword="true"/>, wenn die Datei nach aktueller Konfiguration
    /// ausgeschlossen ist.
    /// </summary>
    /// <param name="absoluteFilePath">Vollqualifizierter Pfad der Datei.</param>
    /// <param name="rootAbsolutePath">Vollqualifizierter Pfad des Roots, zu dem die Datei gehört.</param>
    bool IsExcluded(string absoluteFilePath, string rootAbsolutePath);

    /// <summary>
    /// Verwirft alle gecachten Matcher und gelesenen <c>.mdignore</c>-Inhalte. Wird
    /// nach einer Settings-Änderung aufgerufen, damit der nächste Scan die neuen
    /// Regeln sieht.
    /// </summary>
    void Invalidate();
}

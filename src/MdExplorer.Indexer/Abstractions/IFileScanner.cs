using MdExplorer.Core.Abstractions;

namespace MdExplorer.Indexer.Abstractions;

/// <summary>
/// Rekursiver Datei-Scanner für Markdown-Dateien unterhalb einer Wurzel.
/// Wendet die Ausschlussregeln aus <see cref="IExclusionFilter"/> an.
/// </summary>
public interface IFileScanner
{
    /// <summary>
    /// Liefert alle <c>*.md</c>-Dateien unterhalb der Wurzel, mit Größe und Änderungszeit aus der
    /// Aufzählung; der Abgleich braucht dafür keinen weiteren Zugriff je Datei.
    /// </summary>
    /// <param name="rootAbsolutePath">Wurzelverzeichnis.</param>
    /// <param name="cancellationToken">Abbruchsteuerung.</param>
    /// <returns>Die Dateien.</returns>
    IEnumerable<FileEntry> EnumerateMarkdownFiles(string rootAbsolutePath, CancellationToken cancellationToken);

    /// <summary>
    /// Wendet dieselben Ausschlussregeln wie die Aufzählung auf eine einzelne Datei an. Der Indexer
    /// prüft damit Watcher-Ereignisse, damit für eine gemeldete Datei dasselbe gilt wie für eine
    /// gefundene.
    /// </summary>
    /// <param name="absoluteFilePath">Vollqualifizierter Pfad der Datei.</param>
    /// <param name="rootAbsolutePath">Wurzel, zu der die Datei gehört.</param>
    /// <returns><see langword="true"/>, wenn die Datei nicht in den Index gehört.</returns>
    bool IsExcluded(string absoluteFilePath, string rootAbsolutePath);

    /// <summary>Lädt die Ausschlussregeln neu, etwa nach einer geänderten <c>.mdignore</c>.</summary>
    void InvalidateExclusions();
}

namespace MdExplorer.Indexer.Abstractions;

/// <summary>
/// Rekursiver Datei-Scanner für Markdown-Dateien unterhalb einer Wurzel.
/// Wendet die konfigurierten Ausschlussordner an.
/// </summary>
public interface IFileScanner
{
    /// <summary>Liefert die absoluten Pfade aller <c>*.md</c>-Dateien unterhalb der Wurzel.</summary>
    /// <param name="rootAbsolutePath">Wurzelverzeichnis.</param>
    /// <param name="cancellationToken">Abbruchsteuerung.</param>
    IEnumerable<string> EnumerateMarkdownFiles(string rootAbsolutePath, CancellationToken cancellationToken);
}

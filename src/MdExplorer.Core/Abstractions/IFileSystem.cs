namespace MdExplorer.Core.Abstractions;

/// <summary>
/// Abstraktion über das Dateisystem. Erlaubt austauschbare Implementierungen
/// (z. B. <c>LocalFileSystem</c> in Produktion, <c>FakeFileSystem</c> in Tests).
/// Konkrete Produktiv-Implementierung lebt in <c>MdExplorer.Core.FileSystem</c>.
/// </summary>
public interface IFileSystem
{
    /// <summary>Prüft, ob das angegebene Verzeichnis existiert.</summary>
    bool DirectoryExists(string path);

    /// <summary>Prüft, ob die angegebene Datei existiert.</summary>
    bool FileExists(string path);

    /// <summary>Stellt sicher, dass das Verzeichnis existiert. Erzeugt es bei Bedarf.</summary>
    void EnsureDirectoryExists(string path);

    /// <summary>Liefert die Pfade aller Dateien im Verzeichnis, gemäß Suchmuster.</summary>
    IEnumerable<string> EnumerateFiles(string directory, string searchPattern, bool recursive);

    /// <summary>
    /// Liefert die direkten Unterverzeichnisse (nicht-rekursiv). Wird vom Indexer-BFS
    /// genutzt, der pro Verzeichnis selbst entscheidet, ob er weiter absteigt.
    /// </summary>
    IEnumerable<string> EnumerateDirectories(string directory);

    /// <summary>
    /// <c>true</c>, wenn der Pfad ein Symlink oder eine NTFS-Junction (Reparse-Point) ist.
    /// Wird vom Indexer geprüft, bevor er in das Verzeichnis absteigt.
    /// </summary>
    bool IsReparsePoint(string path);

    /// <summary>
    /// Liefert den kanonischen Endpfad eines Verzeichnisses. Symlinks/Junctions werden auf
    /// ihr finales Ziel aufgelöst. Normaler Pfad → vollqualifizierter Pfad.
    /// Wird für Zyklus-Erkennung im Indexer-BFS verwendet.
    /// </summary>
    string GetDirectoryFinalPath(string path);

    /// <summary>Liest den vollständigen Datei-Inhalt als Bytes — streamingbasiert.</summary>
    Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken);

    /// <summary>Liest den vollständigen Datei-Inhalt als Bytes synchron — für kleine Steuer-Dateien wie <c>.mdignore</c>.</summary>
    byte[] ReadAllBytes(string path);

    /// <summary>
    /// Öffnet die Datei zum Lesen als <see cref="Stream"/>. Der Aufrufer ist für die Freigabe verantwortlich.
    /// </summary>
    Stream OpenRead(string path);

    /// <summary>Liefert die letzte Schreibzeit der Datei in UTC.</summary>
    DateTime GetLastWriteTimeUtc(string path);

    /// <summary>Liefert die Größe der Datei in Byte.</summary>
    long GetFileSize(string path);

    /// <summary>
    /// Schreibt den Inhalt atomar an den Zielpfad. Implementierung schreibt in eine Temp-Datei
    /// im Zielverzeichnis und führt anschließend ein <see cref="File.Move(string,string,bool)"/>
    /// aus, sodass entweder der vollständige neue oder der alte Inhalt sichtbar bleibt.
    /// </summary>
    /// <param name="path">Zielpfad der Datei.</param>
    /// <param name="content">Roher Inhalt, der geschrieben werden soll.</param>
    /// <param name="cancellationToken">Token für kooperativen Abbruch.</param>
    Task WriteAllBytesAtomicAsync(string path, ReadOnlyMemory<byte> content, CancellationToken cancellationToken);
}

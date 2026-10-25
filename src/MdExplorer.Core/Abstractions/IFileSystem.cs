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

    /// <summary>Legt das Verzeichnis samt fehlender Elternverzeichnisse an; ein vorhandenes ist kein Fehler.</summary>
    void EnsureDirectoryExists(string path);

    /// <summary>Liefert die Pfade aller Dateien im Verzeichnis, gemäß Suchmuster.</summary>
    IEnumerable<string> EnumerateFiles(string directory, string searchPattern, bool recursive);

    /// <summary>
    /// Liefert die Dateien eines Verzeichnisses (nicht-rekursiv) mit Größe, Änderungszeit und
    /// Reparse-Kennzeichen, so wie die Aufzählung des Verzeichnisses sie mitbringt.
    /// </summary>
    /// <remarks>
    /// Die Werte einzeln je Datei zu erfragen kostet je Wert einen Zugriff auf das Dateisystem;
    /// bei tausenden Notizen ist das der größte Teil eines Abgleichs. Die Vorgabe hier fragt
    /// einzeln nach und genügt Attrappen; die echte Umsetzung nimmt die Werte aus der Aufzählung.
    /// </remarks>
    /// <param name="directory">Das Verzeichnis.</param>
    /// <param name="searchPattern">Das Suchmuster, etwa <c>*.md</c>.</param>
    /// <returns>Die Dateien.</returns>
    IEnumerable<FileEntry> EnumerateFileEntries(string directory, string searchPattern) =>
        EnumerateFiles(directory, searchPattern, recursive: false)
            .Select(path => new FileEntry(path, GetFileSize(path), GetLastWriteTimeUtc(path), IsReparsePoint(path)));

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

    /// <summary>Liest den vollständigen Datei-Inhalt als Bytes (streamingbasiert).</summary>
    Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken);

    /// <summary>Liest den vollständigen Datei-Inhalt als Bytes synchron, für kleine Steuer-Dateien wie <c>.mdignore</c>.</summary>
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

    /// <summary>
    /// Schreibt den Inhalt atomar an den Zielpfad, synchron; sonst wie
    /// <see cref="WriteAllBytesAtomicAsync"/>.
    /// </summary>
    /// <remarks>
    /// Für kleine Dateien, die beim Schließen eines Fensters gespeichert werden. Dort gibt es
    /// keinen Aufrufer, der auf einen Task warten könnte, und ein nicht abgewarteter Task ginge
    /// beim Beenden verloren.
    /// </remarks>
    /// <param name="path">Zielpfad der Datei.</param>
    /// <param name="content">Roher Inhalt, der geschrieben werden soll.</param>
    void WriteAllBytesAtomic(string path, ReadOnlySpan<byte> content);

    /// <summary>
    /// Verschiebt eine Datei; das Zielverzeichnis wird bei Bedarf angelegt.
    /// </summary>
    /// <remarks>
    /// Überschreibt <b>nicht</b>: Ein vorhandenes Ziel ist ein Fehler und keine stille
    /// Ersetzung, sonst verschwindet beim Umbenennen eine fremde Datei, und niemand erfährt
    /// davon. Umbenennen und Verschieben sind derselbe Vorgang, nur mit anderem Zielpfad.
    /// </remarks>
    /// <param name="sourcePath">Bisheriger Pfad.</param>
    /// <param name="destinationPath">Künftiger Pfad.</param>
    void MoveFile(string sourcePath, string destinationPath);

    /// <summary>Löscht eine Datei. Eine bereits fehlende Datei ist kein Fehler.</summary>
    /// <param name="path">Pfad der Datei.</param>
    void DeleteFile(string path);
}

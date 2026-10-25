using System.IO;
using MdExplorer.Core.Abstractions;

namespace MdExplorer.App.Tests.Fakes;

/// <summary>
/// Dateisystem im Speicher für Ordnerbaum, Editor und Dateivorgänge. Aufzählen liefert nichts;
/// asynchrones Lesen, Schreiben, Verschieben und Löschen lassen sich gezielt scheitern lassen.
/// </summary>
internal sealed class FakeFileSystem : IFileSystem
{
    public HashSet<string> Directories { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, byte[]> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool DirectoryExists(string path) => Directories.Contains(path);

    public bool FileExists(string path) => Files.ContainsKey(path);

    public void EnsureDirectoryExists(string path) => _ = Directories.Add(path);

    /// <summary>Der Fehler, den das Aufzählen meldet; <see langword="null"/>, wenn es gelingen soll.</summary>
    public Exception? FailOnEnumerate { get; set; }

    /// <summary>Hält jedes Aufzählen an, bis der Test das Signal setzt; <see langword="null"/> lässt es durch.</summary>
    /// <remarks>
    /// Wartet höchstens fünf Sekunden, damit ein Aufrufer, der fälschlich auf dem eigenen Thread
    /// liest, den Testlauf nicht für immer anhält, sondern an der Zusicherung scheitert.
    /// </remarks>
    public ManualResetEventSlim? EnumerationGate { get; set; }

    /// <summary>Liefert die Dateien aus <see cref="Files"/>, die im Verzeichnis liegen und zur Endung des Musters passen.</summary>
    public IEnumerable<string> EnumerateFiles(string directory, string searchPattern, bool recursive)
    {
        RecordEnumeration();
        string extension = searchPattern.StartsWith("*.", StringComparison.Ordinal) ? searchPattern[1..] : string.Empty;
        return [.. Files.Keys.Where(path => IsInside(path, directory, recursive)
            && path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))];
    }

    /// <summary>Liefert die direkten Unterverzeichnisse aus <see cref="Directories"/>.</summary>
    public IEnumerable<string> EnumerateDirectories(string directory)
    {
        RecordEnumeration();
        return [.. Directories.Where(path => IsInside(path, directory, recursive: false))];
    }

    public bool IsReparsePoint(string path) => false;

    public string GetDirectoryFinalPath(string path) => path;

    /// <summary>Der Fehler, den das Lesen meldet; <see langword="null"/>, wenn es gelingen soll.</summary>
    /// <remarks>
    /// Eine gesperrte oder mitten im Zugriff gelöschte Datei ist im Betrieb der Normalfall
    /// und nicht die Ausnahme. Ohne diesen Haken bliebe der Zweig, der sie auffängt,
    /// ungeprüft, und ein Aufrufer, der ihn wegoptimiert, fiele niemandem auf.
    /// </remarks>
    public Exception? FailOnRead { get; set; }

    public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken) =>
        FailOnRead is not null
            ? Task.FromException<byte[]>(FailOnRead)
            : Task.FromResult(Files.TryGetValue(path, out byte[]? bytes) ? bytes : Array.Empty<byte>());

    public byte[] ReadAllBytes(string path)
    {
        if (FailOnRead is not null)
        {
            throw FailOnRead;
        }
        return Files.TryGetValue(path, out byte[]? bytes) ? bytes : Array.Empty<byte>();
    }

    public Stream OpenRead(string path) => Stream.Null;

    public DateTime GetLastWriteTimeUtc(string path) => DateTime.UnixEpoch;

    public long GetFileSize(string path) => 0;

    public Dictionary<string, byte[]> WrittenFiles { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Fehler, den <see cref="WriteAllBytesAtomicAsync"/> statt des Schreibens liefert.
    /// Bildet eine schreibgeschützte oder belegte Zieldatei nach. Anders lässt sich der
    /// Fehlerpfad des Editors gegen ein In-Memory-Dateisystem nicht auslösen.
    /// </summary>
    public Exception? FailOnWrite { get; set; }

    public Task WriteAllBytesAtomicAsync(string path, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        if (FailOnWrite is not null)
        {
            return Task.FromException(FailOnWrite);
        }
        WrittenFiles[path] = content.ToArray();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Wie <see cref="WriteAllBytesAtomicAsync"/>; das Geschriebene ist danach auch lesbar, damit
    /// ein Speichern und Laden über denselben Fake geht.
    /// </summary>
    public void WriteAllBytesAtomic(string path, ReadOnlySpan<byte> content)
    {
        if (FailOnWrite is not null)
        {
            throw FailOnWrite;
        }
        WrittenFiles[path] = content.ToArray();
        Files[path] = content.ToArray();
    }

    /// <summary>Wenn gesetzt, wirft <see cref="MoveFile"/> diese Ausnahme.</summary>
    public Exception? FailOnMove { get; set; }

    /// <summary>Pfade, die über <see cref="DeleteFile"/> entfernt wurden.</summary>
    public List<string> DeletedFiles { get; } = [];

    /// <inheritdoc />
    public void MoveFile(string sourcePath, string destinationPath)
    {
        if (FailOnMove is not null)
        {
            throw FailOnMove;
        }
        // Wie unter Windows: Dieselbe Datei in anderer Schreibweise ist kein belegtes Ziel.
        bool sameFile = string.Equals(sourcePath, destinationPath, StringComparison.OrdinalIgnoreCase);
        if (!sameFile && Files.ContainsKey(destinationPath))
        {
            throw new IOException($"Ziel existiert bereits: {destinationPath}");
        }
        if (!Files.TryGetValue(sourcePath, out byte[]? content))
        {
            throw new FileNotFoundException("Quelle fehlt", sourcePath);
        }
        // Erst entfernen, dann eintragen: Bei gleicher Datei in neuer Schreibweise behielte das
        // Wörterbuch sonst den alten Schlüssel, und das Entfernen danach nähme die Datei mit.
        _ = Files.Remove(sourcePath);
        Files[destinationPath] = content;
    }

    /// <summary>Der Fehler, den das Löschen meldet; <see langword="null"/>, wenn es gelingen soll.</summary>
    /// <remarks>
    /// Eine Datei, die gerade in einem anderen Programm offen ist, lässt sich unter Windows
    /// nicht löschen. Das ist der wahrscheinlichste Fehlschlag des ganzen Vorgangs.
    /// </remarks>
    public Exception? FailOnDelete { get; set; }

    /// <inheritdoc />
    public void DeleteFile(string path)
    {
        if (FailOnDelete is not null)
        {
            throw FailOnDelete;
        }

        DeletedFiles.Add(path);
        _ = Files.Remove(path);
    }

    private static bool IsInside(string path, string directory, bool recursive)
    {
        string folder = directory.TrimEnd(Path.DirectorySeparatorChar);
        if (recursive)
        {
            return path.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        return string.Equals(Path.GetDirectoryName(path), folder, StringComparison.OrdinalIgnoreCase);
    }

    private void RecordEnumeration()
    {
        _ = EnumerationGate?.Wait(TimeSpan.FromSeconds(5));
        if (FailOnEnumerate is not null)
        {
            throw FailOnEnumerate;
        }
    }
}

using System.IO;
using MdExplorer.Core.Abstractions;

namespace MdExplorer.App.Tests.Fakes;

/// <summary>Minimaler Fake — deckt nur die Aufrufe ab, die der Folder-Tree benötigt.</summary>
internal sealed class FakeFileSystem : IFileSystem
{
    public HashSet<string> Directories { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, byte[]> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool DirectoryExists(string path) => Directories.Contains(path);

    public bool FileExists(string path) => Files.ContainsKey(path);

    public void EnsureDirectoryExists(string path) => _ = Directories.Add(path);

    public IEnumerable<string> EnumerateFiles(string directory, string searchPattern, bool recursive) => [];

    public IEnumerable<string> EnumerateDirectories(string directory) => [];

    public bool IsReparsePoint(string path) => false;

    public string GetDirectoryFinalPath(string path) => path;

    /// <summary>Der Fehler, den das Lesen meldet — <see langword="null"/>, wenn es gelingen soll.</summary>
    /// <remarks>
    /// Eine gesperrte oder mitten im Zugriff gelöschte Datei ist im Betrieb der Normalfall
    /// und nicht die Ausnahme. Ohne diesen Haken bliebe der Zweig, der sie auffängt,
    /// ungeprüft — und ein Aufrufer, der ihn wegoptimiert, fiele niemandem auf.
    /// </remarks>
    public Exception? FailOnRead { get; set; }

    public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken) =>
        FailOnRead is not null
            ? Task.FromException<byte[]>(FailOnRead)
            : Task.FromResult(Files.TryGetValue(path, out byte[]? bytes) ? bytes : Array.Empty<byte>());

    public byte[] ReadAllBytes(string path) =>
        Files.TryGetValue(path, out byte[]? bytes) ? bytes : Array.Empty<byte>();

    public Stream OpenRead(string path) => Stream.Null;

    public DateTime GetLastWriteTimeUtc(string path) => DateTime.UnixEpoch;

    public long GetFileSize(string path) => 0;

    public Dictionary<string, byte[]> WrittenFiles { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Fehler, den <see cref="WriteAllBytesAtomicAsync"/> statt des Schreibens liefert.
    /// Bildet eine schreibgeschützte oder belegte Zieldatei nach — anders lässt sich der
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
        if (Files.ContainsKey(destinationPath))
        {
            throw new IOException($"Ziel existiert bereits: {destinationPath}");
        }
        if (!Files.TryGetValue(sourcePath, out byte[]? content))
        {
            throw new FileNotFoundException("Quelle fehlt", sourcePath);
        }
        Files[destinationPath] = content;
        _ = Files.Remove(sourcePath);
    }

    /// <summary>Der Fehler, den das Löschen meldet — <see langword="null"/>, wenn es gelingen soll.</summary>
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
}

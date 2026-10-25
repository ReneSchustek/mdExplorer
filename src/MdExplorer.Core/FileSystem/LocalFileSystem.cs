using MdExplorer.Core.Abstractions;

namespace MdExplorer.Core.FileSystem;

/// <summary>
/// Produktive Implementierung von <see cref="IFileSystem"/> gegen das lokale Windows-Dateisystem.
/// </summary>
public sealed class LocalFileSystem : IFileSystem
{
    /// <inheritdoc />
    public bool DirectoryExists(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Directory.Exists(path);
    }

    /// <inheritdoc />
    public bool FileExists(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return File.Exists(path);
    }

    /// <inheritdoc />
    public void EnsureDirectoryExists(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _ = Directory.CreateDirectory(path);
    }

    /// <inheritdoc />
    public IEnumerable<string> EnumerateFiles(string directory, string searchPattern, bool recursive)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(searchPattern);

        SearchOption searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        return Directory.EnumerateFiles(directory, searchPattern, searchOption);
    }

    /// <inheritdoc />
    public IEnumerable<FileEntry> EnumerateFileEntries(string directory, string searchPattern)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(searchPattern);

        // Eine FileInfo aus der Aufzählung trägt Größe, Zeit und Attribute schon aus den Daten des
        // Verzeichnisses; keiner der drei Werte fragt die Datei noch einmal ab.
        return new DirectoryInfo(directory)
            .EnumerateFiles(searchPattern, SearchOption.TopDirectoryOnly)
            .Select(file => new FileEntry(
                file.FullName,
                file.Length,
                file.LastWriteTimeUtc,
                (file.Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint));
    }

    /// <inheritdoc />
    public IEnumerable<string> EnumerateDirectories(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        return Directory.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly);
    }

    /// <inheritdoc />
    public bool IsReparsePoint(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        try
        {
            FileAttributes attributes = File.GetAttributes(path);
            return (attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            // Ein fehlender Pfad ist kein Link. Abgefragt wird ohne vorheriges Exists: Zwischen
            // beiden Aufrufen könnte die Datei verschwinden.
            return false;
        }
    }

    /// <inheritdoc />
    public string GetDirectoryFinalPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        try
        {
            FileSystemInfo? target = Directory.ResolveLinkTarget(path, returnFinalTarget: true);
            return Path.GetFullPath(target?.FullName ?? path);
        }
        catch (IOException)
        {
            // Defekter Link / Zugriff verweigert: fällt auf den Original-Pfad zurück.
            return Path.GetFullPath(path);
        }
        catch (UnauthorizedAccessException)
        {
            return Path.GetFullPath(path);
        }
    }

    /// <inheritdoc />
    public async Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public byte[] ReadAllBytes(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return File.ReadAllBytes(path);
    }

    /// <inheritdoc />
    public Stream OpenRead(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        // Geteilt mit Schreiben und Löschen: Ein Editor, der die Datei gerade speichert, soll
        // nicht an der Sperre des Lesers scheitern. 81.920 Byte ist der Kopierpuffer von .NET
        // und bleibt unter der Grenze, ab der ein Array im Large Object Heap landet.
        return new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
    }

    /// <inheritdoc />
    public DateTime GetLastWriteTimeUtc(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return File.GetLastWriteTimeUtc(path);
    }

    /// <inheritdoc />
    public long GetFileSize(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return new FileInfo(path).Length;
    }

    /// <inheritdoc />
    public async Task WriteAllBytesAtomicAsync(string path, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        string tempPath = PrepareTempPath(path);
        try
        {
            await WriteContentAsync(tempPath, content, cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            TryDeleteSilently(tempPath);
            throw;
        }
    }

    /// <inheritdoc />
    public void WriteAllBytesAtomic(string path, ReadOnlySpan<byte> content)
    {
        string tempPath = PrepareTempPath(path);
        try
        {
            using (FileStream stream = new(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 4096, FileOptions.WriteThrough))
            {
                stream.Write(content);
                stream.Flush();
            }
            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            TryDeleteSilently(tempPath);
            throw;
        }
    }

    /// <inheritdoc />
    public void MoveFile(string sourcePath, string destinationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        string? targetDirectory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(targetDirectory))
        {
            _ = Directory.CreateDirectory(targetDirectory);
        }

        // overwrite: false, damit ein vorhandenes Ziel wirft statt eine fremde Datei zu verschlucken.
        File.Move(sourcePath, destinationPath, overwrite: false);
    }

    /// <inheritdoc />
    public void DeleteFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        // File.Delete schweigt bei einer fehlenden Datei bereits, und das ist hier das gewünschte
        // Verhalten: Wer löschen will, was schon weg ist, hat sein Ziel erreicht.
        File.Delete(path);
    }

    /// <summary>Legt das Zielverzeichnis an und nennt die Zwischendatei daneben.</summary>
    /// <remarks>
    /// Die Zwischendatei muss im selben Verzeichnis liegen, damit <c>File.Move</c> atomar bleibt;
    /// über Laufwerke hinweg ist es ein Kopieren.
    /// </remarks>
    private static string PrepareTempPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        // Bei einem bloßen Dateinamen ist das Verzeichnis leer, bei einer Laufwerkswurzel null;
        // in beiden Fällen gäbe es keinen Ordner für die Zwischendatei.
        string? targetDirectory = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(targetDirectory))
        {
            throw new ArgumentException($"Zielpfad enthält kein Verzeichnis: {path}", nameof(path));
        }

        _ = Directory.CreateDirectory(targetDirectory);
        return Path.Combine(targetDirectory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
    }

    private static async Task WriteContentAsync(string tempPath, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        FileStreamOptions options = new()
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = FileOptions.Asynchronous | FileOptions.WriteThrough,
        };
        FileStream stream = new(tempPath, options);
        await using (stream.ConfigureAwait(false))
        {
            await stream.WriteAsync(content, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static void TryDeleteSilently(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Aufräumen einer Temp-Datei ist Best-Effort, denn der eigentliche Fehler wurde bereits geworfen.
        }
    }
}

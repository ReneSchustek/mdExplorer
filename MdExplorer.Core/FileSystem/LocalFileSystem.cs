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
    public IEnumerable<string> EnumerateDirectories(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        return Directory.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly);
    }

    /// <inheritdoc />
    public bool IsReparsePoint(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Directory.Exists(path) && !File.Exists(path))
        {
            return false;
        }
        FileAttributes attributes = File.GetAttributes(path);
        return (attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
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
            // Defekter Link / Zugriff verweigert — fällt auf den Original-Pfad zurück.
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
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string targetDirectory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException($"Zielpfad enthält kein Verzeichnis: {path}");
        _ = Directory.CreateDirectory(targetDirectory);

        // Temp-Datei muss im selben Verzeichnis liegen, damit File.Move atomar bleibt
        // (Volume-übergreifendes Move ist nicht atomar).
        string tempPath = Path.Combine(targetDirectory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

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
        catch (IOException)
        {
            // Aufräumen einer Temp-Datei ist Best-Effort — der eigentliche Fehler wurde bereits geworfen.
        }
        catch (UnauthorizedAccessException)
        {
            // dito.
        }
    }
}

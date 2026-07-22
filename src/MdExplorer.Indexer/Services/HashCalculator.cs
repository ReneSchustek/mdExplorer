using System.Security.Cryptography;
using MdExplorer.Core.Abstractions;
using MdExplorer.Indexer.Abstractions;

namespace MdExplorer.Indexer.Services;

/// <summary>
/// SHA-256-Inhalts-Hash über streamendes Einlesen. Vermeidet Memory-Spikes bei großen Dateien.
/// </summary>
public sealed class HashCalculator(IFileSystem fileSystem) : IHashCalculator
{
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

    /// <inheritdoc />
    public async Task<string> ComputeAsync(string absolutePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);
        cancellationToken.ThrowIfCancellationRequested();

        Stream stream = _fileSystem.OpenRead(absolutePath);
        await using (stream.ConfigureAwait(false))
        {
            byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
            // Convert.ToHexStringLower: allokationsfrei (kein StringBuilder + 32 Byte-Strings pro Hash),
            // Lowercase-Ergebnis identisch zum bisherigen "x2"-Format. Hot-Path bei jedem (Re-)Sync.
            return Convert.ToHexStringLower(hash);
        }
    }
}

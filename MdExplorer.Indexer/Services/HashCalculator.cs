using System.Globalization;
using System.Security.Cryptography;
using System.Text;
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
            return ConvertToHexString(hash);
        }
    }

    private static string ConvertToHexString(byte[] bytes)
    {
        StringBuilder builder = new(bytes.Length * 2);
        foreach (byte value in bytes)
        {
            _ = builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
        }
        return builder.ToString();
    }
}

namespace MdExplorer.Indexer.Abstractions;

/// <summary>
/// Berechnet einen stabilen, kollisionsarmen Inhalts-Hash für eine Datei.
/// Implementierung muss streamend arbeiten — kein <c>ReadAllBytes</c>.
/// </summary>
public interface IHashCalculator
{
    /// <summary>Berechnet den Hex-kodierten SHA-256-Hash der angegebenen Datei.</summary>
    Task<string> ComputeAsync(string absolutePath, CancellationToken cancellationToken);
}

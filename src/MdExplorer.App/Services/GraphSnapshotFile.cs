using System.IO;
using System.Text;
using MdExplorer.Core.Abstractions;

namespace MdExplorer.App.Services;

/// <summary>
/// Die Datei, über die ein Graph-Fenster sein Dokument lädt. Jedes Fenster hat seine eigene.
/// </summary>
/// <remarks>
/// <para>
/// Der Umweg über eine Datei ist nötig, weil <c>NavigateToString</c> bei 2 MB mit
/// <c>E_INVALIDARG</c> endet, und ein großer Bestand kommt samt eingebettetem Skript darüber.
/// </para>
/// <para>
/// Mit einer gemeinsamen Datei für alle Fenster lud ein Fenster beim Neuladen den Graphen eines
/// anderen. Geschrieben wird asynchron, damit mehrere Megabyte nicht die Oberfläche anhalten.
/// </para>
/// </remarks>
internal sealed class GraphSnapshotFile
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly IFileSystem _fileSystem;
    private readonly string _directory;
    private readonly string _documentUri;
    private int _version;

    /// <summary>Legt die Datei für ein Fenster an; geschrieben wird erst mit <see cref="WriteAsync"/>.</summary>
    /// <param name="fileSystem">Dateizugriff.</param>
    /// <param name="directory">Ordner der Schnappschüsse.</param>
    /// <param name="windowId">Kennung des Fensters; sie macht den Dateinamen eindeutig.</param>
    public GraphSnapshotFile(IFileSystem fileSystem, string directory, Guid windowId)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        _fileSystem = fileSystem;
        _directory = directory;
        FilePath = Path.Combine(directory, "graph-" + windowId.ToString("N") + ".html");
        _documentUri = new Uri(FilePath).AbsoluteUri;
    }

    /// <summary>Voller Pfad der Datei.</summary>
    public string FilePath { get; }

    /// <summary>Schreibt das Dokument und liefert die Adresse, unter der es zu laden ist.</summary>
    /// <param name="html">Das vollständige Dokument.</param>
    /// <param name="cancellationToken">Abbruch, etwa beim Schließen des Fensters.</param>
    /// <exception cref="IOException">Die Datei ließ sich nicht schreiben.</exception>
    /// <exception cref="UnauthorizedAccessException">Der Ordner ist nicht beschreibbar.</exception>
    public async Task<Uri> WriteAsync(string html, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(html);

        _fileSystem.EnsureDirectoryExists(_directory);
        await _fileSystem.WriteAllBytesAtomicAsync(FilePath, Utf8NoBom.GetBytes(html), cancellationToken).ConfigureAwait(true);

        // Jede Fassung bekommt eine eigene Adresse. Sonst lieferte der Browser den alten Inhalt
        // aus dem Zwischenspeicher, wenn sich nur der Inhalt der Datei geändert hat.
        _version++;
        return new Uri(_documentUri + "?v=" + _version.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>Ob <paramref name="uri"/> eine Fassung dieser Datei ist.</summary>
    /// <param name="uri">Adresse, wie die Browserfläche sie meldet.</param>
    public bool IsThisDocument(string uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        return uri.StartsWith(_documentUri, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Löscht die Datei; gibt es sie nicht, geschieht nichts.</summary>
    /// <exception cref="IOException">Die Datei ist noch geöffnet.</exception>
    /// <exception cref="UnauthorizedAccessException">Löschen ist nicht erlaubt.</exception>
    public void Delete() => _fileSystem.DeleteFile(FilePath);
}

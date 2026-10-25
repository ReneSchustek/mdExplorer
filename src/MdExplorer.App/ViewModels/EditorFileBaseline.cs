using System.Security.Cryptography;
using System.Text;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Text;

namespace MdExplorer.App.ViewModels;

/// <summary>
/// Der Stand einer Datei, wie der Editor sie zuletzt geladen oder gespeichert hat.
/// </summary>
/// <remarks>
/// Aus ihm folgen drei Dinge: ob der Text geändert ist, wie er wieder geschrieben wird
/// (Zeilenende und Byte-Order-Mark wie vorgefunden) und ob jemand anderes die Datei inzwischen
/// geändert hat (Prüfsumme).
/// </remarks>
/// <param name="Text">Der Text, gegen den „geändert“ gemessen wird.</param>
/// <param name="ContentHash">SHA-256 der Bytes auf der Platte; leer, solange keine Datei geladen ist.</param>
/// <param name="LineEnding">Das Zeilenende der Datei.</param>
/// <param name="HasBom">Ob die Datei mit einer UTF-8-Byte-Order-Mark beginnt.</param>
internal sealed record EditorFileBaseline(string Text, string ContentHash, LineEndingStyle LineEnding, bool HasBom)
{
    /// <summary>Kein Dokument geladen.</summary>
    public static EditorFileBaseline Empty { get; } = new(string.Empty, string.Empty, LineEndingDetector.Default, HasBom: false);

    /// <summary>Der Stand einer frisch gelesenen Datei.</summary>
    /// <param name="bytes">Die gelesenen Bytes; nur über sie stimmen Prüfsumme und Byte-Order-Mark.</param>
    /// <param name="text">Der dekodierte Text.</param>
    public static EditorFileBaseline FromFile(byte[] bytes, string text)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(text);

        return new(text, HashOf(bytes), LineEndingDetector.Detect(text), bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble));
    }

    /// <summary>Bringt den Text in die Form der Datei: ihr Zeilenende, ihre Markierung.</summary>
    /// <remarks>
    /// Die Markierung bleibt, wie sie war: Werkzeuge, die eine Datei mit Markierung angelegt haben,
    /// erwarten sie beim nächsten Öffnen wieder, und ein Speichern soll nur den Text ändern.
    /// </remarks>
    /// <param name="text">Der Text aus dem Editor.</param>
    /// <returns>Der normalisierte Text und die Bytes, die geschrieben werden.</returns>
    public (string Normalized, byte[] Payload) Encode(string text)
    {
        string normalized = LineEndingDetector.Normalize(text, LineEnding);
        byte[] body = Encoding.UTF8.GetBytes(normalized);
        if (!HasBom)
        {
            return (normalized, body);
        }

        byte[] preamble = Encoding.UTF8.GetPreamble();
        byte[] payload = new byte[preamble.Length + body.Length];
        preamble.CopyTo(payload, 0);
        body.CopyTo(payload, preamble.Length);
        return (normalized, payload);
    }

    /// <summary>Der Stand nach einem Speichern.</summary>
    public EditorFileBaseline AfterSave(string text, byte[] payload) => this with { Text = text, ContentHash = HashOf(payload) };

    /// <summary>Ob die Datei seit dem Laden oder letzten Speichern von außen geändert wurde.</summary>
    /// <remarks>Ist sie inzwischen weg, legt das Speichern sie neu an; überschrieben wird nichts.</remarks>
    public async Task<bool> IsChangedOnDiskAsync(IFileSystem fileSystem, string filePath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        if (ContentHash.Length == 0 || !fileSystem.FileExists(filePath))
        {
            return false;
        }
        byte[] currentBytes = await fileSystem.ReadAllBytesAsync(filePath, cancellationToken).ConfigureAwait(true);
        return !string.Equals(HashOf(currentBytes), ContentHash, StringComparison.Ordinal);
    }

    private static string HashOf(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}

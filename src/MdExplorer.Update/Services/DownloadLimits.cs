namespace MdExplorer.Update.Services;

/// <summary>
/// Grenzen eines Paket-Downloads.
/// </summary>
/// <param name="MaxBytes">Höchstgröße des Pakets in Byte.</param>
/// <param name="StallTimeout">Wie lange ein einzelner Lesevorgang ohne Daten bleiben darf.</param>
/// <remarks>
/// <see cref="HttpClient.Timeout"/> greift bei <see cref="HttpCompletionOption.ResponseHeadersRead"/>
/// nur bis zu den Kopfzeilen. Den Körper begrenzen erst diese beiden Werte: eine stehende
/// Verbindung über die Stillstandsgrenze, ein endloser Strom über die Höchstgröße.
/// </remarks>
internal sealed record DownloadLimits(long MaxBytes, TimeSpan StallTimeout)
{
    private const long Megabyte = 1024 * 1024;

    private const long DefaultMaxMegabytes = 200;

    /// <summary>
    /// 200 MB sind das Vierfache des heutigen Pakets von knapp 50 MB. Eine Minute ohne ein
    /// einziges Byte ist auch auf einer langsamen Leitung kein Durchsatzproblem mehr, sondern ein
    /// Stillstand.
    /// </summary>
    public static DownloadLimits Default { get; } = new(DefaultMaxMegabytes * Megabyte, TimeSpan.FromMinutes(1));
}

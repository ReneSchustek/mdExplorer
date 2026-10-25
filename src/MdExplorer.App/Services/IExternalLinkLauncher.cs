namespace MdExplorer.App.Services;

/// <summary>
/// Übergibt einen Verweis ins Netz an das Standardprogramm des Systems (Browser, Mailprogramm).
/// </summary>
internal interface IExternalLinkLauncher
{
    /// <summary>Öffnet <paramref name="uri"/> im Standardprogramm.</summary>
    /// <param name="uri">Ein absoluter <c>http</c>-, <c>https</c>- oder <c>mailto</c>-Verweis.</param>
    void Open(Uri uri);
}

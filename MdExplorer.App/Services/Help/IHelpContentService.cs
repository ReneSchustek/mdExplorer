namespace MdExplorer.App.Services.Help;

/// <summary>
/// Lädt das eingebettete Handbuch (<c>docs/HANDBUCH.md</c>) und rendert es zu HTML
/// inklusive Inhaltsverzeichnis. Das Ergebnis wird einmalig erzeugt und gecacht.
/// </summary>
internal interface IHelpContentService
{
    /// <summary>
    /// Liefert das gerenderte Handbuch. Beim ersten Aufruf wird die eingebettete
    /// Ressource gelesen, Markdig-Pipeline ausgeführt und das Inhaltsverzeichnis
    /// aus den H2-Überschriften extrahiert.
    /// </summary>
    Task<HelpContent> GetAsync(CancellationToken cancellationToken);
}

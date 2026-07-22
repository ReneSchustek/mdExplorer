namespace MdExplorer.App.Services.Help;

/// <summary>
/// Gerendertes Handbuch: das vollständige HTML, das Inhaltsverzeichnis aus
/// den H2-Überschriften und der Plain-Text-Auszug für die In-Hilfe-Suche.
/// </summary>
/// <param name="Html">HTML-Repräsentation des Handbuchs (Markdig-Ausgabe mit Anker-Identifiern).</param>
/// <param name="Toc">Inhaltsverzeichnis: pro H2 ein Eintrag mit Anker-Slug und Anzeige-Titel.</param>
/// <param name="PlainText">Klartext-Repräsentation des Handbuchs für eine schlichte IndexOf-Suche.</param>
internal sealed record HelpContent(
    string Html,
    IReadOnlyList<HelpTocEntry> Toc,
    string PlainText);

/// <summary>Ein Kapitel-Eintrag im Hilfe-Inhaltsverzeichnis.</summary>
/// <param name="Slug">Anker-Identifier (entspricht <c>id</c>-Attribut der HTML-Überschrift).</param>
/// <param name="Title">Anzeige-Titel der Überschrift (HTML-decodiert).</param>
internal sealed record HelpTocEntry(string Slug, string Title);

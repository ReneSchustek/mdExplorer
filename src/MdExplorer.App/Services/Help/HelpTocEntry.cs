namespace MdExplorer.App.Services.Help;

/// <summary>Ein Kapitel-Eintrag im Hilfe-Inhaltsverzeichnis.</summary>
/// <param name="Slug">Anker-Identifier (entspricht <c>id</c>-Attribut der HTML-Überschrift).</param>
/// <param name="Title">Anzeige-Titel der Überschrift (HTML-decodiert).</param>
internal sealed record HelpTocEntry(string Slug, string Title);

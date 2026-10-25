namespace MdExplorer.App.Services.Help;

/// <summary>
/// Stabile Anker-Slugs der Hilfe-Kapitel. Die Werte stimmen mit den
/// <c>{#slug}</c>-Attributen der H2-Überschriften in <c>docs/HANDBUCH.md</c>
/// überein und damit auch mit den <c>id</c>-Attributen im gerenderten HTML.
/// Wer die Slugs hier ändert, muss das Handbuch entsprechend nachziehen,
/// und umgekehrt.
/// </summary>
internal static class HelpContext
{
    /// <summary>Inhaltsverzeichnis (Default, wenn nichts Spezielles aktiv ist).</summary>
    public const string TableOfContents = "inhalt";

    /// <summary>Einführung. Keine Ansicht setzt diesen Kontext; erreichbar über das Inhaltsverzeichnis.</summary>
    public const string Intro = "einfuehrung";

    /// <summary>Installation. Ebenfalls nur über das Inhaltsverzeichnis erreichbar.</summary>
    public const string Install = "installation";

    /// <summary>Die Oberfläche; gilt, solange der Fokus in der Liste aller Dateien steht.</summary>
    public const string Layout = "layout";

    /// <summary>Indexierung und <c>.mdignore</c>; gilt im Ordnerbaum und im Einstellungsfenster.</summary>
    public const string Indexing = "indexierung";

    /// <summary>Suche und Vorschau: aktives Suchfeld, Dokument-Panel.</summary>
    public const string Search = "suche";

    /// <summary>Tag-Cloud und Tag-Verwaltung.</summary>
    public const string TagCloud = "tagcloud";

    /// <summary>Graph-Ansicht.</summary>
    public const string Graph = "graph";

    /// <summary>Häufige Fragen. Keine Ansicht setzt diesen Kontext.</summary>
    public const string Faq = "faq";
}

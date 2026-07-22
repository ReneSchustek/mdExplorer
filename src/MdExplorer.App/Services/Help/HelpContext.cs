namespace MdExplorer.App.Services.Help;

/// <summary>
/// Stabile Anker-Slugs der Hilfe-Kapitel. Die Werte stimmen mit den
/// <c>{#slug}</c>-Attributen der H2-Überschriften in <c>docs/HANDBUCH.md</c>
/// überein und damit auch mit den <c>id</c>-Attributen im gerenderten HTML.
/// Wer die Slugs hier ändert, muss das Handbuch entsprechend nachziehen —
/// und umgekehrt.
/// </summary>
internal static class HelpContext
{
    /// <summary>Inhaltsverzeichnis — Default, wenn nichts Spezielles aktiv ist.</summary>
    public const string TableOfContents = "inhalt";

    /// <summary>Einführungs-Kapitel — Initial-State bei leeren Roots.</summary>
    public const string Intro = "einfuehrung";

    /// <summary>Installations-Kapitel.</summary>
    public const string Install = "installation";

    /// <summary>Drei-Spalten-Layout — Folder-Tree, Splitter, Preview.</summary>
    public const string Layout = "layout";

    /// <summary>Indexierung und `.mdignore` — Settings-Dialog, Folder-Tree-Pause.</summary>
    public const string Indexing = "indexierung";

    /// <summary>Suche und Vorschau — aktives Suchfeld, Dokument-Panel.</summary>
    public const string Search = "suche";

    /// <summary>Tag-Cloud und Tag-Verwaltung.</summary>
    public const string TagCloud = "tagcloud";

    /// <summary>Graph-Ansicht.</summary>
    public const string Graph = "graph";

    /// <summary>FAQ-Kapitel — wird derzeit nicht automatisch gesetzt.</summary>
    public const string Faq = "faq";
}

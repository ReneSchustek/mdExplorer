namespace MdExplorer.TagCloud.Messaging;

/// <summary>
/// Nachricht — der Anwender hat einen Tag in der Cloud angeklickt.
/// Empfänger (i. d. R. das Such-ViewModel) fügt einen entsprechenden Filter
/// in das aktive Such-Query ein.
/// </summary>
/// <param name="Slug">Normalisierter Tag-Slug (Lowercase, Umlaute erhalten).</param>
/// <param name="DisplayName">Original-Tag-Name für UI-Anzeige/Logs.</param>
/// <param name="Mode">Filter-Modus — additiv, ersetzend oder exkludierend.</param>
public sealed record TagClickedMessage(string Slug, string DisplayName, TagFilterMode Mode);

/// <summary>Filter-Wirkung beim Tag-Klick.</summary>
public enum TagFilterMode
{
    /// <summary>Neue Suche — bestehender Filter wird ersetzt (Default-Klick).</summary>
    Replace = 0,

    /// <summary>Ergänzt den bestehenden Suchstring um <c>tag:slug</c> (Strg-Klick).</summary>
    Add = 1,

    /// <summary>Ergänzt den bestehenden Suchstring um <c>-tag:slug</c> (Alt-Klick).</summary>
    Exclude = 2,
}

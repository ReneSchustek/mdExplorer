namespace MdExplorer.TagCloud.Messaging;

/// <summary>Filter-Wirkung beim Tag-Klick.</summary>
public enum TagFilterMode
{
    /// <summary>Neue Suche, bestehender Filter wird ersetzt (Default-Klick).</summary>
    Replace = 0,

    /// <summary>Ergänzt den bestehenden Suchstring um <c>tag:slug</c> (Strg-Klick).</summary>
    Add = 1,

    /// <summary>Ergänzt den bestehenden Suchstring um <c>-tag:slug</c> (Alt-Klick).</summary>
    Exclude = 2,
}

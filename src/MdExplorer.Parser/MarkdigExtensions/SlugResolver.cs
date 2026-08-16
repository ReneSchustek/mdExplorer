namespace MdExplorer.Parser.MarkdigExtensions;

/// <summary>
/// Überführt einen Roh-Zielnamen in einen URL-sicheren Slug — und sagt, wenn das nicht geht.
/// </summary>
/// <remarks>
/// <para>
/// Der Rückgabewert ist der eigentliche Punkt dieser Signatur. Bis zum 16.08.2026 reichte hier
/// ein <c>Func&lt;string, string&gt;</c>, das im Zweifel eine Ausnahme warf. Ein Verweis wie
/// <c>[[…]]</c> — drei Punkte als Auslassungszeichen — hat damit den Renderer abgebrochen, und
/// weil der Renderer das ganze Dokument schreibt, fiel die **ganze Datei** aus dem Index. Ein
/// Satzzeichen kostete ein Dokument.
/// </para>
/// <para>
/// Ein Zielname kommt aus fremdem Text. Dass er unbrauchbar ist, ist deshalb kein Fehler,
/// sondern ein Ergebnis — und gehört in den Rückgabewert, nicht in eine Ausnahme.
/// </para>
/// </remarks>
/// <param name="raw">Der Roh-Zielname, wie er im Dokument steht.</param>
/// <param name="slug">Der Slug bei Erfolg, andernfalls die leere Zeichenkette.</param>
/// <returns><see langword="true"/>, wenn ein Slug gebildet werden konnte.</returns>
public delegate bool SlugResolver(string raw, out string slug);

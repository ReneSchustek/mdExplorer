namespace MdExplorer.Indexer.Services;

/// <summary>
/// Die Muster einer <c>.mdignore</c> zusammen mit dem Ordner, in dem sie liegt. Ein Muster gilt
/// relativ zu diesem Ordner, wie in einer <c>.gitignore</c>; ohne den Ordner ließe es sich nicht
/// richtig abgleichen.
/// </summary>
/// <param name="Directory">Absoluter Ordner, in dem die <c>.mdignore</c> liegt.</param>
/// <param name="Patterns">Die Muster in der Reihenfolge der Datei.</param>
public sealed record MdIgnoreSource(string Directory, IReadOnlyList<string> Patterns);

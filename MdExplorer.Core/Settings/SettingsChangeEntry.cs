namespace MdExplorer.Core.Settings;

/// <summary>
/// Einzelner Diff-Eintrag, der im Settings-Audit-Log persistiert wird.
/// </summary>
/// <param name="Path">
/// JSON-Punktpfad der geänderten Property, z. B. <c>indexing.roots[0]</c>
/// oder <c>behavior.searchDebounceMs</c>.
/// </param>
/// <param name="Previous">Voriger Wert als JSON-Literal (<c>null</c> bei neu eingeführten Properties).</param>
/// <param name="Current">Neuer Wert als JSON-Literal (<c>null</c> bei entfernten Properties).</param>
public sealed record SettingsChangeEntry(string Path, string? Previous, string? Current);

namespace MdExplorer.Graph.Models;

/// <summary>
/// Pro-Aufruf-Filter für <see cref="MdExplorer.Graph.Abstractions.IGraphService.BuildSnapshotAsync"/>.
/// Statische Pfad-Exclusions und Knoten-Obergrenzen liegen in <c>GraphOptions</c>; dieser Record
/// kapselt nur den dynamisch durch die UI gesetzten Pfad-Prefix.
/// </summary>
/// <param name="PathPrefix">Relativer Pfad-Prefix (Trennzeichen <c>/</c>); nur Dateien deren
/// <c>RelativePath</c> mit dem Prefix beginnt fliessen in den Snapshot. <see langword="null"/>
/// oder leer bedeutet kein Filter.</param>
public sealed record GraphFilter(string? PathPrefix = null)
{
    /// <summary>Kein Filter — alle Files aus dem Source-Snapshot werden berücksichtigt.</summary>
    public static GraphFilter None { get; } = new();
}

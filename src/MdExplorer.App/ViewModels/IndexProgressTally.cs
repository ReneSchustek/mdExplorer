namespace MdExplorer.App.ViewModels;

/// <summary>
/// Zählt die indizierten Dateien je Wurzel und liefert die Summe für die Statuszeile.
/// </summary>
/// <remarks>
/// Der Indexer meldet je Wurzel, wie weit er ist. Die Summe ergibt sich erst aus allen
/// Wurzeln, und eine Wurzel, die der Nutzer entfernt, darf danach nicht weiter mitzählen.
/// Die Meldungen kommen von Hintergrund-Threads; deshalb die Sperre.
/// </remarks>
internal sealed class IndexProgressTally
{
    private readonly Dictionary<string, int> _perRoot = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _gate = new();

    /// <summary>Übernimmt den Stand einer Wurzel.</summary>
    /// <param name="root">Die gemeldete Wurzel.</param>
    /// <param name="processedCount">Wie viele Dateien dort erfasst sind.</param>
    /// <returns>Die Summe über alle Wurzeln.</returns>
    public int Record(string root, int processedCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        lock (_gate)
        {
            _perRoot[root] = processedCount;
            return _perRoot.Values.Sum();
        }
    }

    /// <summary>Vergisst alle Wurzeln, die nicht mehr eingestellt sind.</summary>
    /// <param name="currentRoots">Die eingestellten Wurzeln.</param>
    /// <returns>Die Summe über die verbliebenen Wurzeln.</returns>
    public int Retain(IReadOnlyCollection<string> currentRoots)
    {
        ArgumentNullException.ThrowIfNull(currentRoots);
        HashSet<string> keep = new(currentRoots, StringComparer.OrdinalIgnoreCase);
        lock (_gate)
        {
            foreach (string root in _perRoot.Keys.Where(root => !keep.Contains(root)).ToList())
            {
                _ = _perRoot.Remove(root);
            }
            return _perRoot.Values.Sum();
        }
    }
}

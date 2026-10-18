using MdExplorer.Indexer.Abstractions;

namespace MdExplorer.Indexer.Tests.Fakes;

/// <summary>
/// Exclusion-Filter, der einen Pfad ausschließt, wenn einer der Pfad-Segmente
/// in der konfigurierten Ordnerliste vorkommt. Hält Tests, die nur einen Ordner
/// ausblenden wollen, frei von Glob-Mustern und <c>.mdignore</c>-Dateien.
/// </summary>
internal sealed class StubExclusionFilter : IExclusionFilter
{
    private readonly HashSet<string> _excludedSegments;

    public StubExclusionFilter(IEnumerable<string> excludedFolderNames)
    {
        ArgumentNullException.ThrowIfNull(excludedFolderNames);
        _excludedSegments = new HashSet<string>(excludedFolderNames, StringComparer.OrdinalIgnoreCase);
    }

    public bool IsExcluded(string absoluteFilePath, string rootAbsolutePath)
    {
        string relative = Path.GetRelativePath(rootAbsolutePath, absoluteFilePath);
        foreach (string segment in relative.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (_excludedSegments.Contains(segment))
            {
                return true;
            }
        }
        return false;
    }

    public void Invalidate()
    {
    }
}

using MdExplorer.Indexer.Abstractions;

namespace MdExplorer.Indexer.Tests.Fakes;

/// <summary>
/// <see cref="IExclusionFilter"/>-Stub für Tests, der nie ausschließt.
/// Hilft, FileScanner/MarkdownIndexer-Tests von Glob-Komplexität zu entkoppeln.
/// </summary>
internal sealed class PassThroughExclusionFilter : IExclusionFilter
{
    public bool IsExcluded(string absoluteFilePath, string rootAbsolutePath) => false;

    public void Invalidate()
    {
    }
}

using MdExplorer.Core.Abstractions;

namespace MdExplorer.TagCloud.Tests.Fakes;

/// <summary>Liefert je Slug die Dateien, die der Test vorgibt.</summary>
internal sealed class FakeTagFileLookupQuery : ITagFileLookupQuery
{
    private readonly Dictionary<string, IReadOnlyList<TagFileLookupRow>> _files = new(StringComparer.Ordinal);

    public void SetFiles(string slug, params TagFileLookupRow[] rows) =>
        _files[slug] = rows;

    public Task<IReadOnlyList<TagFileLookupRow>> GetFilesByTagSlugAsync(string slug, CancellationToken cancellationToken)
    {
        if (_files.TryGetValue(slug, out IReadOnlyList<TagFileLookupRow>? rows))
        {
            return Task.FromResult(rows);
        }
        return Task.FromResult<IReadOnlyList<TagFileLookupRow>>([]);
    }
}

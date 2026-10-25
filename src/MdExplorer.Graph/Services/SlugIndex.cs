using MdExplorer.Core.Abstractions;

namespace MdExplorer.Graph.Services;

/// <summary>
/// Ordnet den normalisierten Dateinamen ihre Datei zu; daran lösen Graph, Relationen und der
/// Klick auf einen WikiLink dasselbe Ziel auf.
/// </summary>
internal static class SlugIndex
{
    /// <summary>Baut den Index über alle Dateien.</summary>
    /// <param name="files">Die Datei-Stammdaten.</param>
    /// <param name="normalizer">Die Normalisierung, die auch die Verweise durchlaufen.</param>
    /// <returns>Slug auf Datei-Id.</returns>
    public static Dictionary<string, Guid> Build(IReadOnlyList<GraphSourceFile> files, ITagNormalizer normalizer)
    {
        Dictionary<string, Guid> index = new(files.Count, StringComparer.Ordinal);

        // Bei gleichnamigen Dateien gewinnt die erste nach relativem Pfad, ordinal verglichen.
        // Der Klick auf einen WikiLink geht über ResolveAsync und damit über denselben Index;
        // Kante und Klick zeigen auf dieselbe Datei.
        foreach (GraphSourceFile file in files.OrderBy(file => file.RelativePath, StringComparer.Ordinal))
        {
            // TryToSlug: ein Dateiname aus reinen Sonderzeichen (z. B. "#.md", unter Windows gültig)
            // liefert keinen Slug; überspringen statt eine ArgumentException nach oben zu werfen.
            if (!normalizer.TryToSlug(file.FileNameWithoutExtension, out string slug))
            {
                continue;
            }
            _ = index.TryAdd(slug, file.Id);
        }
        return index;
    }
}

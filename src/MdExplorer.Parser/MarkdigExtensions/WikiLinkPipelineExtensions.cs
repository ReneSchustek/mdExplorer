using System.Linq;
using Markdig;

namespace MdExplorer.Parser.MarkdigExtensions;

/// <summary>
/// Fluent-Erweiterung für die Markdig-Pipeline zur Aktivierung der MdExplorer-WikiLinks.
/// </summary>
public static class WikiLinkPipelineExtensions
{
    /// <summary>
    /// Aktiviert die <see cref="WikiLinkExtension"/> mit dem übergebenen Slug-Bilder.
    /// </summary>
    /// <param name="pipeline">Pipeline-Builder.</param>
    /// <param name="slugResolver">Bildet den Slug — und meldet, wenn der Zielname keinen hergibt.</param>
    /// <returns>Der übergebene <paramref name="pipeline"/>-Builder für Fluent-Verkettung.</returns>
    public static MarkdownPipelineBuilder UseMdExplorerWikiLinks(
        this MarkdownPipelineBuilder pipeline,
        SlugResolver slugResolver)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(slugResolver);

        if (pipeline.Extensions.Any(extension => extension is WikiLinkExtension))
        {
            return pipeline;
        }

        pipeline.Extensions.Add(new WikiLinkExtension(slugResolver));
        return pipeline;
    }
}

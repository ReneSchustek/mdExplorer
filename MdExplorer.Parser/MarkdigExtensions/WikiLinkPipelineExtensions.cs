using System.Linq;
using Markdig;

namespace MdExplorer.Parser.MarkdigExtensions;

/// <summary>
/// Fluent-Erweiterung für die Markdig-Pipeline zur Aktivierung der MdExplorer-WikiLinks.
/// </summary>
public static class WikiLinkPipelineExtensions
{
    /// <summary>
    /// Aktiviert die <see cref="WikiLinkExtension"/> mit dem übergebenen Slug-Provider.
    /// </summary>
    /// <param name="pipeline">Pipeline-Builder.</param>
    /// <param name="slugProvider">Funktion, die den Roh-Zielnamen in einen URL-sicheren Slug überführt.</param>
    /// <returns>Der übergebene <paramref name="pipeline"/>-Builder für Fluent-Verkettung.</returns>
    public static MarkdownPipelineBuilder UseMdExplorerWikiLinks(
        this MarkdownPipelineBuilder pipeline,
        Func<string, string> slugProvider)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(slugProvider);

        if (pipeline.Extensions.Any(extension => extension is WikiLinkExtension))
        {
            return pipeline;
        }

        pipeline.Extensions.Add(new WikiLinkExtension(slugProvider));
        return pipeline;
    }
}

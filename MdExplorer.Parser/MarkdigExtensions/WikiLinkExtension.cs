using System.Linq;
using Markdig;
using Markdig.Renderers;

namespace MdExplorer.Parser.MarkdigExtensions;

/// <summary>
/// Markdig-Erweiterung, die WikiLink-Parsing und HTML-Rendering aktiviert.
/// Erwartet einen Slug-Provider, der den Zielnamen in einen URL-sicheren Slug überführt
/// (typischerweise <c>TagNormalizer.ToSlug</c>).
/// </summary>
public sealed class WikiLinkExtension : IMarkdownExtension
{
    private readonly Func<string, string> _slugProvider;

    /// <summary>Erzeugt die Extension mit dem übergebenen Slug-Provider.</summary>
    public WikiLinkExtension(Func<string, string> slugProvider)
    {
        ArgumentNullException.ThrowIfNull(slugProvider);
        _slugProvider = slugProvider;
    }

    /// <inheritdoc />
    public void Setup(MarkdownPipelineBuilder pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        if (!ContainsParser(pipeline))
        {
            pipeline.InlineParsers.Insert(0, new WikiLinkInlineParser());
        }
    }

    /// <inheritdoc />
    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        if (renderer is HtmlRenderer htmlRenderer && !ContainsRenderer(htmlRenderer))
        {
            htmlRenderer.ObjectRenderers.Insert(0, new WikiLinkHtmlRenderer(_slugProvider));
        }
    }

    private static bool ContainsParser(MarkdownPipelineBuilder pipeline)
        => pipeline.InlineParsers.Any(parser => parser is WikiLinkInlineParser);

    private static bool ContainsRenderer(HtmlRenderer renderer)
        => renderer.ObjectRenderers.Any(existing => existing is WikiLinkHtmlRenderer);
}

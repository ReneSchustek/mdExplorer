using System.Linq;
using Markdig;
using Markdig.Renderers;

namespace MdExplorer.Parser.MarkdigExtensions;

/// <summary>
/// Markdig-Erweiterung, die WikiLink-Parsing und HTML-Rendering aktiviert.
/// Erwartet einen <see cref="SlugResolver"/>, der den Zielnamen in einen URL-sicheren Slug
/// überführt (typischerweise <c>TagNormalizer.TryToSlug</c>).
/// </summary>
public sealed class WikiLinkExtension : IMarkdownExtension
{
    private readonly SlugResolver _slugResolver;

    /// <summary>Erzeugt die Extension mit dem übergebenen Slug-Bilder.</summary>
    /// <param name="slugResolver">Bildet den Slug — und meldet, wenn der Zielname keinen hergibt.</param>
    public WikiLinkExtension(SlugResolver slugResolver)
    {
        ArgumentNullException.ThrowIfNull(slugResolver);
        _slugResolver = slugResolver;
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
            htmlRenderer.ObjectRenderers.Insert(0, new WikiLinkHtmlRenderer(_slugResolver));
        }
    }

    private static bool ContainsParser(MarkdownPipelineBuilder pipeline)
        => pipeline.InlineParsers.Any(parser => parser is WikiLinkInlineParser);

    private static bool ContainsRenderer(HtmlRenderer renderer)
        => renderer.ObjectRenderers.Any(existing => existing is WikiLinkHtmlRenderer);
}

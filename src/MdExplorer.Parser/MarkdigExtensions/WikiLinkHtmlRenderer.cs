using Markdig.Renderers;
using Markdig.Renderers.Html;

namespace MdExplorer.Parser.MarkdigExtensions;

/// <summary>
/// HTML-Renderer für <see cref="WikiLinkInline"/>. Schreibt <c>&lt;a href="mdexplorer://slug"&gt;</c>;
/// das eigene <c>mdexplorer://</c>-Schema vermeidet, dass externe Browser den Link verfolgen,
/// und der Slug wird über den injizierten Provider erzeugt.
/// </summary>
public sealed class WikiLinkHtmlRenderer : HtmlObjectRenderer<WikiLinkInline>
{
    /// <summary>URL-Schema für interne WikiLink-Navigation in der MdExplorer-App.</summary>
    public const string UrlScheme = "mdexplorer://";

    private readonly Func<string, string> _slugProvider;

    /// <summary>Erzeugt einen Renderer mit dem übergebenen Slug-Provider (typischerweise <c>TagNormalizer.ToSlug</c>).</summary>
    public WikiLinkHtmlRenderer(Func<string, string> slugProvider)
    {
        ArgumentNullException.ThrowIfNull(slugProvider);
        _slugProvider = slugProvider;
    }

    /// <inheritdoc />
    protected override void Write(HtmlRenderer renderer, WikiLinkInline obj)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(obj);

        string slug = _slugProvider(obj.Target);
        if (renderer.EnableHtmlForInline)
        {
            _ = renderer.Write("<a href=\"");
            _ = renderer.Write(UrlScheme);
            _ = renderer.WriteEscapeUrl(slug);
            _ = renderer.Write("\">");
            _ = renderer.WriteEscape(obj.Display);
            _ = renderer.Write("</a>");
            return;
        }

        _ = renderer.WriteEscape(obj.Display);
    }
}

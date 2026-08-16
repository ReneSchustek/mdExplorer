using Markdig.Renderers;
using Markdig.Renderers.Html;

namespace MdExplorer.Parser.MarkdigExtensions;

/// <summary>
/// HTML-Renderer für <see cref="WikiLinkInline"/>. Schreibt <c>&lt;a href="mdexplorer://slug"&gt;</c>;
/// das eigene <c>mdexplorer://</c>-Schema vermeidet, dass externe Browser den Link verfolgen,
/// und der Slug wird über den injizierten <see cref="SlugResolver"/> erzeugt.
/// </summary>
public sealed class WikiLinkHtmlRenderer : HtmlObjectRenderer<WikiLinkInline>
{
    /// <summary>URL-Schema für interne WikiLink-Navigation in der MdExplorer-App.</summary>
    public const string UrlScheme = "mdexplorer://";

    private readonly SlugResolver _slugResolver;

    /// <summary>Erzeugt einen Renderer mit dem übergebenen Slug-Bilder (typischerweise <c>TagNormalizer.TryToSlug</c>).</summary>
    /// <param name="slugResolver">Bildet den Slug — und meldet, wenn der Zielname keinen hergibt.</param>
    public WikiLinkHtmlRenderer(SlugResolver slugResolver)
    {
        ArgumentNullException.ThrowIfNull(slugResolver);
        _slugResolver = slugResolver;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Gibt der Zielname keinen Slug her — <c>[[…]]</c>, <c>[[+]]</c> —, bleibt der Anzeigetext
    /// stehen, aber ohne Verweis. Es gibt kein Ziel, also gibt es keinen Link; der Satz drumherum
    /// ist davon nicht betroffen. Vorher warf diese Stelle, und weil der Renderer das ganze
    /// Dokument schreibt, verlor ein Satzzeichen die ganze Datei.
    /// </remarks>
    protected override void Write(HtmlRenderer renderer, WikiLinkInline obj)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(obj);

        if (renderer.EnableHtmlForInline && _slugResolver(obj.Target, out string slug))
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

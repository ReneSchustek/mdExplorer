using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace MdExplorer.Parser.MarkdigExtensions;

/// <summary>
/// Bereinigt jedes gelesene Dokument, bevor HTML daraus wird: nur ungefährliche Verweisziele und
/// nur unschädliche Attribute bleiben.
/// </summary>
/// <remarks>
/// <para>
/// Die Notizen stammen nicht alle vom Nutzer selbst. Ohne diese Stufe hielt allein die
/// Sicherheitsregel der Vorschau ein <c>javascript:</c>-Ziel oder ein <c>onclick</c> davon ab,
/// etwas auszuführen, und ein <c>style</c>-Attribut konnte die Vorschau überdecken. Das HTML
/// soll für sich sauber sein, nicht erst durch die Schicht dahinter.
/// </para>
/// <para>
/// Verweise: erlaubt sind relative Ziele, <c>http</c>, <c>https</c>, <c>mailto</c> und das eigene
/// <c>mdexplorer</c>; Bilder dürfen zusätzlich <c>data:image/</c> sein, das die Vorschau
/// ohnehin zulässt. Jedes andere Ziel wird geleert. Attribute: <c>id</c> und Klassen bleiben, damit
/// Anker wie <c>{#inhalt}</c> weiter funktionieren; von den übrigen bleiben nur die aus
/// <see cref="AllowedAttributes"/>. Eine Liste der erlaubten statt der verbotenen, weil über
/// <c>{…}</c> sonst auch <c>href</c> oder <c>src</c> einzuschleusen wären.
/// </para>
/// </remarks>
public sealed class SafeOutputExtension : IMarkdownExtension
{
    private static readonly string[] AllowedSchemes = ["http", "https", "mailto", "mdexplorer"];

    private static readonly string[] AllowedAttributes = ["title", "lang", "dir", "alt", "width", "height"];

    /// <inheritdoc />
    public void Setup(MarkdownPipelineBuilder pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        pipeline.DocumentProcessed -= Sanitize;
        pipeline.DocumentProcessed += Sanitize;
    }

    /// <inheritdoc />
    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
    {
        // Gerendert wird mit den gewöhnlichen Renderern; bereinigt ist das Dokument schon.
    }

    /// <summary>Sagt, ob ein Verweisziel in der Ausgabe stehen bleiben darf.</summary>
    /// <param name="url">Das Ziel, wie es in der Notiz steht.</param>
    /// <param name="isImage">Ob es die Quelle eines Bildes ist.</param>
    /// <returns><see langword="true"/> für relative Ziele und die erlaubten Schemata.</returns>
    internal static bool IsSafeUrl(string? url, bool isImage)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return true;
        }
        string trimmed = url.Trim();
        string? scheme = SchemeOf(trimmed);
        if (scheme is null)
        {
            return true;
        }
        if (isImage && trimmed.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        return AllowedSchemes.Contains(scheme, StringComparer.OrdinalIgnoreCase);
    }

    private static void Sanitize(MarkdownDocument document)
    {
        foreach (MarkdownObject node in document.Descendants())
        {
            switch (node)
            {
                case LinkInline link when !IsSafeUrl(link.Url, link.IsImage):
                    link.Url = string.Empty;
                    break;
                case AutolinkInline autolink when !autolink.IsEmail && !IsSafeUrl(autolink.Url, isImage: false):
                    autolink.Url = string.Empty;
                    break;
                default:
                    break;
            }
            RemoveUnsafeAttributes(node.TryGetAttributes());
        }
    }

    private static void RemoveUnsafeAttributes(HtmlAttributes? attributes)
    {
        _ = attributes?.Properties?.RemoveAll(property => !AllowedAttributes.Contains(property.Key, StringComparer.OrdinalIgnoreCase));
    }

    // Ein Schema steht vor dem ersten Doppelpunkt, sofern davor kein Schrägstrich, Fragezeichen
    // oder Rautenzeichen kommt; ein Doppelpunkt mitten in einem Pfad macht ihn nicht zum Schema.
    // Ein Laufwerksbuchstabe zählt als Schema und fällt damit heraus: Ein Ziel auf der Platte des
    // Nutzers gehört nicht in eine Notiz.
    private static string? SchemeOf(string url)
    {
        int colon = url.IndexOf(':', StringComparison.Ordinal);
        if (colon <= 0)
        {
            return null;
        }
        int pathStart = url.IndexOfAny(['/', '?', '#']);
        return pathStart >= 0 && pathStart < colon ? null : url[..colon];
    }
}

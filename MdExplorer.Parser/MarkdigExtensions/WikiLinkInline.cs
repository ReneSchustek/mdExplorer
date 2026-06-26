using Markdig.Syntax.Inlines;

namespace MdExplorer.Parser.MarkdigExtensions;

/// <summary>
/// AST-Knoten für einen WikiLink (<c>[[Ziel]]</c> oder <c>[[Ziel|Anzeige]]</c>).
/// <see cref="Target"/> trägt den Roh-Zielnamen vor dem Pipe, <see cref="Display"/> den anzuzeigenden Text.
/// </summary>
public sealed class WikiLinkInline : LeafInline
{
    /// <summary>Erzeugt einen WikiLink mit Ziel und Anzeigetext.</summary>
    public WikiLinkInline(string target, string display)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(display);
        Target = target;
        Display = display;
    }

    /// <summary>Roh-Zielname (vor dem optionalen Pipe).</summary>
    public string Target { get; }

    /// <summary>Anzuzeigender Text (entspricht <see cref="Target"/>, wenn kein Pipe verwendet wurde).</summary>
    public string Display { get; }
}

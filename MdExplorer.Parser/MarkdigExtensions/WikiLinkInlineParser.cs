using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Syntax;

namespace MdExplorer.Parser.MarkdigExtensions;

/// <summary>
/// Inline-Parser für WikiLinks <c>[[Ziel]]</c> und <c>[[Ziel|Anzeige]]</c>.
/// Läuft vor dem Standard-LinkInlineParser, damit das doppelte <c>[</c> nicht als
/// normaler Markdown-Link interpretiert wird. Bricht ohne Konsum ab, sobald Inhalt
/// einen Zeilenumbruch, ein verschachteltes <c>[[</c> oder ein leeres Ziel enthält.
/// </summary>
public sealed class WikiLinkInlineParser : InlineParser
{
    /// <summary>Maximale Länge des Inhalts zwischen <c>[[</c> und <c>]]</c> in Zeichen.</summary>
    public const int MaxContentLength = 512;

    /// <summary>Erzeugt eine neue Instanz und registriert <c>[</c> als Öffnungszeichen.</summary>
    public WikiLinkInlineParser()
    {
        OpeningCharacters = ['['];
    }

    /// <inheritdoc />
    public override bool Match(InlineProcessor processor, ref StringSlice slice)
    {
        ArgumentNullException.ThrowIfNull(processor);
        if (slice.PeekChar() != '[')
        {
            return false;
        }
        int contentStart = slice.Start + 2;
        if (!TryFindClosingBrackets(slice.Text, contentStart, slice.End, out int closeIndex))
        {
            return false;
        }
        if (!SplitTargetAndDisplay(slice.Text[contentStart..closeIndex], out string target, out string display))
        {
            return false;
        }
        EmitInline(processor, ref slice, closeIndex, target, display);
        return true;
    }

    // Scan-Phase als reine Funktion. Findet die Position des zweiten ']'.
    private static bool TryFindClosingBrackets(string text, int contentStart, int sliceEnd, out int closeIndex)
    {
        closeIndex = -1;
        int upperBound = Math.Min(sliceEnd, contentStart + MaxContentLength);
        for (int i = contentStart; i <= upperBound; i++)
        {
            if (i >= text.Length)
            {
                return false;
            }
            char current = text[i];
            if (current is '\n' or '\r')
            {
                return false;
            }
            if (current == ']' && i + 1 <= sliceEnd && i + 1 < text.Length && text[i + 1] == ']')
            {
                closeIndex = i;
                return true;
            }
            if (current == '[')
            {
                return false;
            }
        }
        return false;
    }

    // Split-Phase — trim + optional Pipe-Split + Empty-Guards.
    private static bool SplitTargetAndDisplay(string rawContent, out string target, out string display)
    {
        string raw = rawContent.Trim();
        target = string.Empty;
        display = string.Empty;
        if (raw.Length == 0)
        {
            return false;
        }
        int pipe = raw.IndexOf('|', StringComparison.Ordinal);
        if (pipe < 0)
        {
            target = raw;
            display = raw;
        }
        else
        {
            target = raw[..pipe].Trim();
            display = raw[(pipe + 1)..].Trim();
        }
        return target.Length > 0 && display.Length > 0;
    }

    // Emit-Phase — Source-Position ermitteln, Slice weiterschieben, Inline-Knoten setzen.
    private static void EmitInline(
        InlineProcessor processor,
        ref StringSlice slice,
        int closeIndex,
        string target,
        string display)
    {
        int openStart = slice.Start;
        int sourceStart = processor.GetSourcePosition(openStart, out int line, out int column);
        int sourceEnd = processor.GetSourcePosition(closeIndex + 1);
        slice.Start = closeIndex + 2;
        processor.Inline = new WikiLinkInline(target, display)
        {
            Span = new SourceSpan(sourceStart, sourceEnd),
            Line = line,
            Column = column,
        };
    }
}

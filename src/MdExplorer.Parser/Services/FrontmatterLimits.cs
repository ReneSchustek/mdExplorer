using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace MdExplorer.Parser.Services;

/// <summary>Grund, aus dem ein Frontmatter-Block nicht gelesen wird.</summary>
internal enum FrontmatterRejection
{
    /// <summary>Der Block hält alle Grenzen ein.</summary>
    None,

    /// <summary>Der Block ist länger als <see cref="FrontmatterLimits.MaxLength"/>.</summary>
    TooLong,

    /// <summary>Der Block enthält einen Alias.</summary>
    Alias,

    /// <summary>Der Block ist tiefer verschachtelt als <see cref="FrontmatterLimits.MaxNestingDepth"/>.</summary>
    TooDeep,
}

/// <summary>
/// Die Grenzen, unter denen ein Frontmatter-Block überhaupt gelesen wird.
/// </summary>
/// <remarks>
/// Das Frontmatter kommt aus Dateien, die der Nutzer nicht alle selbst geschrieben hat. Index und
/// Tag-Umschreiber prüfen deshalb dieselben Grenzen, bevor sie das YAML anfassen: Ein Block, den
/// der Index verwirft, ändert auch der Umschreiber nicht. Der Vorlauf über die Ereignisse des
/// YAML-Lesers arbeitet ohne Rekursion; erst das Laden in Knoten ruft sich je Ebene selbst auf.
/// </remarks>
internal static class FrontmatterLimits
{
    /// <summary>Größte Länge des Frontmatter-Blocks in Zeichen.</summary>
    /// <remarks>
    /// Frontmatter sind Metadaten: Titel, Datum, ein paar Schlagworte, selten mehr als zwei
    /// Kilobyte. 64 KiB lassen dreißigfachen Spielraum und halten zugleich Lesezeit und Ausgabe
    /// klein. Weil Aliase abgelehnt werden, stammt jedes Zeichen der Ausgabe aus einem Zeichen der
    /// Eingabe, dazu höchstens das Trennzeichen „, “ je Listeneintrag. Die Ausgabe bleibt damit
    /// unter dem Dreifachen dieser Grenze; ein eigenes Ausgabebudget ist nicht nötig.
    /// </remarks>
    public const int MaxLength = 64 * 1024;

    /// <summary>Größte Verschachtelungstiefe aus Listen und Abschnitten.</summary>
    /// <remarks>
    /// Echte Notizen kommen mit zwei oder drei Ebenen aus. Der Überlauf des Aufrufstapels beginnt
    /// beim Laden in Knoten zwischen 2.000 und 4.000 Ebenen (gemessen); 64 lässt reichlich Abstand
    /// nach beiden Seiten, auch auf einem Thread mit kleinerem Stapel.
    /// </remarks>
    public const int MaxNestingDepth = 64;

    /// <summary>Prüft den Block gegen die Grenzen.</summary>
    /// <remarks>
    /// Ein Alias wird abgelehnt, nicht aufgelöst: Er macht die Größe der Ausgabe unabhängig von der
    /// Größe der Eingabe (zehn Verweise je Ebene verzehnfachen sie), und Metadaten einer Notiz
    /// brauchen keine geteilten Knoten. Ein Anker allein schadet nicht und bleibt erlaubt.
    /// </remarks>
    /// <param name="yaml">Der Inhalt zwischen den Trennzeilen.</param>
    /// <returns>Der erste verletzte Grund, oder <see cref="FrontmatterRejection.None"/>.</returns>
    /// <exception cref="YamlException">Das YAML ist syntaktisch kaputt.</exception>
    public static FrontmatterRejection FindRejection(string yaml)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        if (yaml.Length > MaxLength)
        {
            return FrontmatterRejection.TooLong;
        }

        using StringReader reader = new(yaml);
        YamlDotNet.Core.Parser parser = new(reader);
        int depth = 0;
        while (parser.MoveNext())
        {
            FrontmatterRejection rejection = Inspect(parser.Current, ref depth);
            if (rejection != FrontmatterRejection.None)
            {
                return rejection;
            }
        }
        return FrontmatterRejection.None;
    }

    private static FrontmatterRejection Inspect(ParsingEvent? current, ref int depth)
    {
        switch (current)
        {
            case AnchorAlias:
                return FrontmatterRejection.Alias;
            case SequenceStart or MappingStart:
                depth++;
                return depth > MaxNestingDepth ? FrontmatterRejection.TooDeep : FrontmatterRejection.None;
            case SequenceEnd or MappingEnd:
                depth--;
                return FrontmatterRejection.None;
            default:
                return FrontmatterRejection.None;
        }
    }
}

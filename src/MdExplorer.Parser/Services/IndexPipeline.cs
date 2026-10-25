using Markdig;
using MdExplorer.Parser.MarkdigExtensions;

namespace MdExplorer.Parser.Services;

/// <summary>
/// Die Markdig-Pipeline, mit der der Index ein Dokument liest.
/// </summary>
/// <remarks>
/// <para>
/// Parser, Editor-Zählung und Umschreiber müssen dasselbe Dokument sehen. Liest einer davon mit
/// anderen Erweiterungen, gilt dieselbe Raute an einer Stelle als Schlagwort und an der anderen
/// nicht: Der Editor zeigt dann Schlagworte, die der Index nicht kennt, oder die Verwaltung
/// benennt Rauten um, die nie gezählt wurden.
/// </para>
/// <para>
/// Die Erweiterungen sind einzeln gewählt statt über <c>UseAdvancedExtensions</c>: Dort ist
/// <c>MediaLinks</c> dabei, das Videoverweise als <c>iframe</c> einbettet, und jede neue
/// Markdig-Fassung könnte weitere Ausgaben mitbringen, ohne dass es jemand bemerkt.
/// <c>GenericAttributes</c> bleibt für Anker wie <c>{#inhalt}</c>; was es an Attributen erlaubt,
/// begrenzt <see cref="SafeOutputExtension"/>.
/// </para>
/// </remarks>
internal static class IndexPipeline
{
    /// <summary>Liefert den Builder mit den Erweiterungen des Index, noch nicht gebaut.</summary>
    /// <param name="slugResolver">Bildet die Slugs der WikiLink-Ziele.</param>
    /// <returns>Der Builder, den der Aufrufer bei Bedarf noch ergänzt.</returns>
    public static MarkdownPipelineBuilder CreateBuilder(SlugResolver slugResolver)
    {
        MarkdownPipelineBuilder builder = new MarkdownPipelineBuilder()
            .UseAlertBlocks()
            .UseAbbreviations()
            .UseAutoIdentifiers()
            .UseCitations()
            .UseCustomContainers()
            .UseDefinitionLists()
            .UseEmphasisExtras()
            .UseFigures()
            .UseFooters()
            .UseFootnotes()
            .UseGridTables()
            .UseMathematics()
            .UsePipeTables()
            .UseListExtras()
            .UseTaskLists()
            .UseDiagrams()
            .UseAutoLinks()
            .UseYamlFrontMatter()
            .DisableHtml()
            .UseMdExplorerWikiLinks(slugResolver)
            // Markdig verlangt die allgemeinen Attribute als letzte Erweiterung, die Inhalte liest.
            .UseGenericAttributes();
        builder.Extensions.Add(new SafeOutputExtension());
        return builder;
    }
}

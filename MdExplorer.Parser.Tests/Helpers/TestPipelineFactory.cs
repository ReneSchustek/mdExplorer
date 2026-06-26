using Markdig;
using Markdig.Syntax;
using MdExplorer.Parser.MarkdigExtensions;
using MdExplorer.Parser.Services;

namespace MdExplorer.Parser.Tests.Helpers;

/// <summary>
/// Stellt eine produktionsnahe Markdig-Pipeline für die Service-Tests bereit:
/// AdvancedExtensions, EmphasisExtras, YamlFrontMatter, HTML disabled und die MdExplorer-WikiLink-Extension.
/// </summary>
internal static class TestPipelineFactory
{
    private static readonly TagNormalizer Normalizer = new();

    public static MarkdownPipeline CreatePipeline() =>
        new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .UseEmphasisExtras()
            .UseYamlFrontMatter()
            .DisableHtml()
            .UseMdExplorerWikiLinks(Normalizer.ToSlug)
            .Build();

    public static MarkdownDocument Parse(string markdown) =>
        Markdown.Parse(markdown, CreatePipeline());

    public static string ToHtml(string markdown) =>
        Markdown.ToHtml(markdown, CreatePipeline());
}

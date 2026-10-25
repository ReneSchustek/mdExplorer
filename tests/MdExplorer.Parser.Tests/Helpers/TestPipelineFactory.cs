using Markdig;
using Markdig.Syntax;
using MdExplorer.Parser.Services;

namespace MdExplorer.Parser.Tests.Helpers;

/// <summary>
/// Stellt die Markdig-Pipeline des Index für die Service-Tests bereit. Gebaut wird sie über
/// <see cref="IndexPipeline.CreateBuilder"/>, damit eine geänderte Index-Pipeline auch hier wirkt.
/// </summary>
internal static class TestPipelineFactory
{
    private static readonly TagNormalizer Normalizer = new();

    public static MarkdownPipeline CreatePipeline() =>
        IndexPipeline.CreateBuilder(Normalizer.TryToSlug).Build();

    public static MarkdownDocument Parse(string markdown) =>
        Markdown.Parse(markdown, CreatePipeline());

    public static string ToHtml(string markdown) =>
        Markdown.ToHtml(markdown, CreatePipeline());
}

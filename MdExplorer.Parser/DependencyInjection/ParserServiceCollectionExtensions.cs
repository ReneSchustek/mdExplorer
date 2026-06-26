using MdExplorer.Parser.Abstractions;
using MdExplorer.Parser.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MdExplorer.Parser.DependencyInjection;

/// <summary>DI-Registrierung des Parser-Moduls.</summary>
public static class ParserServiceCollectionExtensions
{
    /// <summary>
    /// Registriert den <see cref="ParseOrchestrator"/> als <see cref="IHostedService"/> sowie alle
    /// modulinternen Abhängigkeiten. Erwartet, dass <c>ParserOptions</c> bereits über das Optionssystem
    /// registriert wurde und die Data-Schicht (<c>AddData</c>) die Repositories bereitstellt.
    /// </summary>
    public static IServiceCollection AddParser(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        _ = services.AddSingleton<ITagNormalizer, TagNormalizer>();
        _ = services.AddSingleton<IFrontmatterExtractor, FrontmatterExtractor>();
        _ = services.AddSingleton<ITagExtractor, TagExtractor>();
        _ = services.AddSingleton<IWikiLinkExtractor, WikiLinkExtractor>();
        _ = services.AddSingleton<IMarkdownTagRewriter, MarkdownTagRewriter>();
        _ = services.AddSingleton<IMarkdownParser, MarkdigParser>();
        _ = services.AddSingleton<ParseOrchestrator>();
        _ = services.AddHostedService(sp => sp.GetRequiredService<ParseOrchestrator>());

        return services;
    }
}

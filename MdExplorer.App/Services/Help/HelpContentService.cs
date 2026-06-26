using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.Extensions.Logging;

namespace MdExplorer.App.Services.Help;

/// <summary>
/// Standard-Implementierung von <see cref="IHelpContentService"/>: lädt die
/// eingebettete Datei <c>Handbuch.md</c>, rendert sie mit Markdig zu HTML
/// (Auto-Identifier für Anker, ohne WikiLink-Rewrite) und extrahiert die
/// H2-Überschriften als Inhaltsverzeichnis. Das Resultat wird thread-safe
/// gecacht — das Handbuch ändert sich zur Laufzeit nicht.
/// </summary>
internal sealed partial class HelpContentService : IHelpContentService, IDisposable
{
    private const string ResourceName = "MdExplorer.App.Assets.Help.Handbuch.md";

    private readonly ILogger<HelpContentService> _logger;
    private readonly MarkdownPipeline _pipeline;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private HelpContent? _cached;
    private bool _disposed;

    /// <summary>Erzeugt den Service und baut die Markdig-Pipeline einmalig auf.</summary>
    public HelpContentService(ILogger<HelpContentService> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        _pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .UseAutoIdentifiers()
            .DisableHtml()
            .Build();
    }

    /// <inheritdoc />
    public async Task<HelpContent> GetAsync(CancellationToken cancellationToken)
    {
        HelpContent? snapshot = Volatile.Read(ref _cached);
        if (snapshot is not null)
        {
            return snapshot;
        }

        await _initLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            snapshot = Volatile.Read(ref _cached);
            if (snapshot is not null)
            {
                return snapshot;
            }

            string markdown = ReadEmbeddedResource();
            MarkdownDocument ast = Markdown.Parse(markdown, _pipeline);
            string html = ast.ToHtml(_pipeline);
            List<HelpTocEntry> toc = ExtractToc(ast);
            string plainText = ExtractPlainText(ast);
            HelpContent built = new(html, toc, plainText);
            Volatile.Write(ref _cached, built);
            LogLoaded(_logger, toc.Count, plainText.Length);
            return built;
        }
        finally
        {
            _ = _initLock.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _initLock.Dispose();
    }

    private static string ReadEmbeddedResource()
    {
        Assembly assembly = typeof(HelpContentService).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream(ResourceName);
        if (stream is null)
        {
            throw new InvalidOperationException(
                $"Eingebettete Hilfe-Ressource fehlt: {ResourceName}.");
        }
        using StreamReader reader = new(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static List<HelpTocEntry> ExtractToc(MarkdownDocument ast)
    {
        List<HelpTocEntry> entries = [];
        foreach (Block block in ast)
        {
            if (block is not HeadingBlock heading || heading.Level != 2)
            {
                continue;
            }
            string? slug = heading.TryGetAttributes()?.Id;
            if (string.IsNullOrEmpty(slug))
            {
                continue;
            }
            string title = ExtractInlineText(heading.Inline);
            if (string.IsNullOrWhiteSpace(title))
            {
                continue;
            }
            entries.Add(new HelpTocEntry(slug, title));
        }
        return entries;
    }

    private static string ExtractPlainText(MarkdownDocument ast)
    {
        StringBuilder builder = new();
        AppendBlocks(builder, ast);
        // Mehrfach-Whitespace vereinheitlichen, damit IndexOf-Treffer nicht durch
        // Markdown-Strukturen (Listen, Tabellen, eingerueckte Zeilen) verfaelscht werden.
        return CollapseWhitespace().Replace(builder.ToString(), " ").Trim();
    }

    private static void AppendBlocks(StringBuilder builder, IEnumerable<Block> blocks)
    {
        foreach (Block block in blocks)
        {
            switch (block)
            {
                case LeafBlock leaf when leaf.Inline is not null:
                    _ = builder.Append(ExtractInlineText(leaf.Inline));
                    _ = builder.Append(' ');
                    break;
                case ContainerBlock container:
                    AppendBlocks(builder, container);
                    break;
            }
        }
    }

    private static string ExtractInlineText(ContainerInline? inline)
    {
        if (inline is null)
        {
            return string.Empty;
        }
        StringBuilder builder = new();
        AppendInline(builder, inline);
        return builder.ToString().Trim();
    }

    private static void AppendInline(StringBuilder builder, ContainerInline container)
    {
        foreach (Inline node in container)
        {
            switch (node)
            {
                case LiteralInline literal:
                    _ = builder.Append(literal.Content.ToString());
                    break;
                case CodeInline code:
                    _ = builder.Append(code.Content);
                    break;
                case LineBreakInline:
                    _ = builder.Append(' ');
                    break;
                case ContainerInline child:
                    AppendInline(builder, child);
                    break;
            }
        }
    }

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex CollapseWhitespace();

    [LoggerMessage(EventId = 1100, Level = LogLevel.Information, Message = "Hilfe-Inhalt geladen ({TocCount} Kapitel, {TextLength} Zeichen Plaintext).")]
    private static partial void LogLoaded(ILogger logger, int tocCount, int textLength);
}

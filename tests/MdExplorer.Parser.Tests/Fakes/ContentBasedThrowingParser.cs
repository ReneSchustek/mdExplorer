using System.Runtime.ExceptionServices;
using MdExplorer.Parser.Abstractions;
using MdExplorer.Parser.Models;

namespace MdExplorer.Parser.Tests.Fakes;

/// <summary>
/// Parser, der bei einem festgelegten Roh-Inhalt wirft und sonst an die echte Kette
/// weiterreicht. <see cref="EngineVersion"/> ist setzbar, damit sich ein Fassungswechsel
/// nachstellen lässt.
/// </summary>
internal sealed class ContentBasedThrowingParser : IMarkdownParser
{
    public const string InitialEngineVersion = "test-engine/1";

    private readonly IMarkdownParser _inner;
    private readonly string _failingContent;
    private readonly Exception _failure;

    public ContentBasedThrowingParser(IMarkdownParser inner, string failingContent, Exception failure)
    {
        _inner = inner;
        _failingContent = failingContent;
        _failure = failure;
    }

    public string EngineVersion { get; set; } = InitialEngineVersion;

    private int _parseCallCount;

    // Zählt jeden Aufruf; die Zahl belegt, dass eine bekannte unparsbare Datei gar nicht
    // erst wieder beim Parser ankommt. Der Orchestrator parst parallel, deshalb atomar.
    public int ParseCallCount => Volatile.Read(ref _parseCallCount);

    public ParseResult Parse(string markdownText)
    {
        _ = Interlocked.Increment(ref _parseCallCount);
        if (string.Equals(markdownText, _failingContent, StringComparison.Ordinal))
        {
            ExceptionDispatchInfo.Capture(_failure).Throw();
        }
        return _inner.Parse(markdownText);
    }
}

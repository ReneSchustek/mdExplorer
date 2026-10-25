using MdExplorer.Core.Abstractions;
using MdExplorer.Parser.Models;

namespace MdExplorer.Parser.Services;

/// <summary>Eine erfolgreich geparste Datei, bereit zum Schreiben.</summary>
/// <param name="Snapshot">Die Datei, wie der Index sie kennt.</param>
/// <param name="Result">Das Ergebnis des Parsers.</param>
internal readonly record struct ParsedEntry(MarkdownSourceSnapshot Snapshot, ParseResult Result);

/// <summary>Eine Datei, die sich nicht parsen ließ, mit dem Grund für Vermerk und Protokoll.</summary>
/// <param name="Snapshot">Die Datei, wie der Index sie kennt.</param>
/// <param name="Reason">Der Grund in einer Zeile, gekürzt auf die Spaltenlänge des Vermerks.</param>
/// <param name="Failure">Die Ausnahme, wenn eine geworfen wurde; für den vollen Stapel im Protokoll.</param>
internal readonly record struct ParseFailureEntry(MarkdownSourceSnapshot Snapshot, string Reason, Exception? Failure);

/// <summary>Ergebniszahlen eines Stapels, addierbar, damit der Durchlauf sie aufsummieren kann.</summary>
/// <param name="Processed">Geparste und geschriebene Dateien.</param>
/// <param name="Skipped">Dateien ohne Arbeit, weil aktuell oder mit gültigem Vermerk.</param>
/// <param name="FailuresChanged">Ob ein Vermerk angelegt oder aufgehoben wurde.</param>
internal readonly record struct BatchOutcome(int Processed, int Skipped, bool FailuresChanged)
{
    /// <summary>Summe zweier Stapel.</summary>
    /// <param name="other">Der zweite Stapel.</param>
    /// <returns>Die aufsummierten Zahlen.</returns>
    public BatchOutcome Add(BatchOutcome other) =>
        new(Processed + other.Processed, Skipped + other.Skipped, FailuresChanged || other.FailuresChanged);
}

/// <summary>Sammelt die Ergebnisse der parallel laufenden Parse-Vorgänge unter einer Sperre.</summary>
internal sealed class ParseCollector
{
    private readonly Lock _gate = new();

    /// <summary>Die erfolgreich geparsten Dateien.</summary>
    public List<ParsedEntry> Parsed { get; } = [];

    /// <summary>Die Dateien, an denen der Parser gescheitert ist oder die er nicht annimmt.</summary>
    public List<ParseFailureEntry> Failed { get; } = [];

    /// <summary>Nimmt ein Ergebnis auf.</summary>
    /// <param name="entry">Die geparste Datei.</param>
    public void AddParsed(ParsedEntry entry)
    {
        lock (_gate)
        {
            Parsed.Add(entry);
        }
    }

    /// <summary>Nimmt einen Fehlschlag auf.</summary>
    /// <param name="entry">Die gescheiterte Datei.</param>
    public void AddFailure(ParseFailureEntry entry)
    {
        lock (_gate)
        {
            Failed.Add(entry);
        }
    }
}

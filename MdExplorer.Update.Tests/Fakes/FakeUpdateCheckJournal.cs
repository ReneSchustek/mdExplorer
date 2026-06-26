using MdExplorer.Update.Abstractions;

namespace MdExplorer.Update.Tests.Fakes;

/// <summary>In-Memory-Implementierung von <see cref="IUpdateCheckJournal"/> für Tests.</summary>
internal sealed class FakeUpdateCheckJournal : IUpdateCheckJournal
{
    /// <summary>Erzeugt das Journal, optional mit einem vorbelegten letzten Prüfzeitpunkt.</summary>
    public FakeUpdateCheckJournal(DateTimeOffset? initialLastCheck = null) => LastCheck = initialLastCheck;

    /// <summary>Der aktuell gespeicherte letzte Prüfzeitpunkt.</summary>
    public DateTimeOffset? LastCheck { get; private set; }

    /// <summary>Anzahl der Schreibvorgänge — erlaubt die Prüfung, ob der Throttle persistiert wurde.</summary>
    public int WriteCount { get; private set; }

    /// <inheritdoc />
    public Task<DateTimeOffset?> ReadLastCheckAsync(CancellationToken cancellationToken) =>
        Task.FromResult(LastCheck);

    /// <inheritdoc />
    public Task WriteLastCheckAsync(DateTimeOffset timestampUtc, CancellationToken cancellationToken)
    {
        LastCheck = timestampUtc;
        WriteCount++;
        return Task.CompletedTask;
    }
}

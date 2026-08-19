using MdExplorer.Core.Models;
using MdExplorer.Parser.Tests.Helpers;
using Microsoft.Extensions.Logging;

namespace MdExplorer.Parser.Tests.Services;

/// <summary>
/// Hält fest, dass eine Datei, die der Parser nicht verarbeiten kann, genau einmal
/// vollständig protokolliert und danach in Ruhe gelassen wird.
/// </summary>
/// <remarks>
/// Vor dieser Absicherung erfüllte eine gescheiterte Datei die Auswahlbedingung nach jedem
/// Fehlschlag unverändert und stand fünf Sekunden später wieder im Stapel. Gemessen an einer
/// Protokolldatei vom 17.08.2026: 1.948 Fehlversuche an einer einzigen Datei, 517.491
/// Stapelzeilen, 97,1 Prozent der Datei. Jede echte Diagnose war damit weggerollt.
/// </remarks>
public sealed class ParseFailureThrottlingTests
{
    private const string BadContent = "DEEPLY_NESTED_PAYLOAD_FOR_TEST";

    private const int ParseFailedEventId = 206;

    private const int ParseFailedAgainEventId = 210;

    [Fact]
    public async Task RunOnce_OnFirstFailure_RecordsHashAndEngineVersion()
    {
        ThrowingParserHarness harness = CreateHarness();
        Guid badId = harness.AddSource("/r/bad.md", "hash-1", BadContent);

        await harness.Sut.RunOnceAsync(TestContext.Current.CancellationToken);

        ParseFailure recorded = harness.FailureRepo.Snapshot[badId];
        Assert.Equal("hash-1", recorded.ContentHash);
        Assert.Equal(ContentBasedThrowingParserVersion, recorded.EngineVersion);
        Assert.Contains("depth limit", recorded.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunOnce_OnSameHash_DoesNotParseAgain()
    {
        ThrowingParserHarness harness = CreateHarness();
        _ = harness.AddSource("/r/bad.md", "hash-1", BadContent);

        await harness.Sut.RunOnceAsync(TestContext.Current.CancellationToken);
        int callsAfterFirstRun = harness.Parser.ParseCallCount;

        for (int i = 0; i < 10; i++)
        {
            await harness.Sut.RunOnceAsync(TestContext.Current.CancellationToken);
        }

        Assert.Equal(callsAfterFirstRun, harness.Parser.ParseCallCount);
    }

    [Fact]
    public async Task RunOnce_OnSameHash_WritesStackTraceOnlyOnce()
    {
        ThrowingParserHarness harness = CreateHarness();
        _ = harness.AddSource("/r/bad.md", "hash-1", BadContent);

        for (int i = 0; i < 10; i++)
        {
            await harness.Sut.RunOnceAsync(TestContext.Current.CancellationToken);
        }

        Assert.Equal(1, CountEntries(harness, ParseFailedEventId));
    }

    [Fact]
    public async Task RunOnce_OnChangedHash_TriesAgain()
    {
        ThrowingParserHarness harness = CreateHarness();
        Guid badId = harness.AddSource("/r/bad.md", "hash-1", BadContent);
        await harness.Sut.RunOnceAsync(TestContext.Current.CancellationToken);
        int callsAfterFirstRun = harness.Parser.ParseCallCount;

        harness.UpdateSource(badId, "hash-2", "# Jetzt lesbar");
        await harness.Sut.RunOnceAsync(TestContext.Current.CancellationToken);

        Assert.True(harness.Parser.ParseCallCount > callsAfterFirstRun);
        Assert.True(harness.DocRepo.Snapshot.ContainsKey(badId));
    }

    [Fact]
    public async Task RunOnce_OnFileParsableAgain_ClearsTheMark()
    {
        ThrowingParserHarness harness = CreateHarness();
        Guid badId = harness.AddSource("/r/bad.md", "hash-1", BadContent);
        await harness.Sut.RunOnceAsync(TestContext.Current.CancellationToken);

        harness.UpdateSource(badId, "hash-2", "# Jetzt lesbar");
        await harness.Sut.RunOnceAsync(TestContext.Current.CancellationToken);

        Assert.Empty(harness.FailureRepo.Snapshot);
        Assert.Equal(0, harness.FailureStatus.UnparsableFileCount);
    }

    [Fact]
    public async Task RunOnce_OnChangedContentStillUnparsable_WritesStackTraceAgain()
    {
        ThrowingParserHarness harness = CreateHarness();
        Guid badId = harness.AddSource("/r/bad.md", "hash-1", BadContent);
        await harness.Sut.RunOnceAsync(TestContext.Current.CancellationToken);

        // Gleicher unparsbarer Inhalt, aber neuer Hash: eine bearbeitete Datei, die weiterhin
        // scheitert, ist ein neuer Sachverhalt und bekommt deshalb wieder den vollen Stapel.
        harness.UpdateSource(badId, "hash-2", BadContent);
        await harness.Sut.RunOnceAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, CountEntries(harness, ParseFailedEventId));
        Assert.Equal(0, CountEntries(harness, ParseFailedAgainEventId));
        Assert.Equal("hash-2", harness.FailureRepo.Snapshot[badId].ContentHash);
    }

    [Fact]
    public async Task RunOnce_AfterEngineChange_TriesAgainAndLogsWithoutStackTrace()
    {
        ThrowingParserHarness harness = CreateHarness();
        _ = harness.AddSource("/r/bad.md", "hash-1", BadContent);
        await harness.Sut.RunOnceAsync(TestContext.Current.CancellationToken);

        // Eine neue Parser-Fassung hebt den Vermerk auf — die Datei könnte jetzt parsbar sein.
        // Scheitert sie erneut am selben Inhalt, steht der Stapel längst im Protokoll.
        harness.Parser.EngineVersion = "test-engine/2";
        await harness.Sut.RunOnceAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, CountEntries(harness, ParseFailedEventId));
        Assert.Equal(1, CountEntries(harness, ParseFailedAgainEventId));
        Assert.Null(SingleEntry(harness, ParseFailedAgainEventId).Exception);
        Assert.NotNull(SingleEntry(harness, ParseFailedEventId).Exception);
    }

    [Fact]
    public async Task RunOnce_OnUnparsableFiles_ReportsCountToOperationStatus()
    {
        ThrowingParserHarness harness = CreateHarness();
        _ = harness.AddSource("/r/bad.md", "hash-1", BadContent);
        _ = harness.AddSource("/r/good.md", "hash-2", "# Alles gut");

        await harness.Sut.RunOnceAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, harness.FailureStatus.UnparsableFileCount);
    }

    [Fact]
    public async Task RunOnce_OnUnchangedFailures_DoesNotCountAgain()
    {
        ThrowingParserHarness harness = CreateHarness();
        _ = harness.AddSource("/r/bad.md", "hash-1", BadContent);
        await harness.Sut.RunOnceAsync(TestContext.Current.CancellationToken);
        int countCallsAfterFirstRun = harness.FailureRepo.CountCallCount;

        for (int i = 0; i < 5; i++)
        {
            await harness.Sut.RunOnceAsync(TestContext.Current.CancellationToken);
        }

        Assert.Equal(countCallsAfterFirstRun, harness.FailureRepo.CountCallCount);
    }

    [Fact]
    public async Task RunOnce_OnUnparsableFile_KeepsProcessingTheRest()
    {
        ThrowingParserHarness harness = CreateHarness();
        Guid badId = harness.AddSource("/r/bad.md", "hash-1", BadContent);
        Guid goodId = harness.AddSource("/r/good.md", "hash-2", "Body mit #ok.");

        await harness.Sut.RunOnceAsync(TestContext.Current.CancellationToken);

        Assert.False(harness.DocRepo.Snapshot.ContainsKey(badId));
        Assert.True(harness.DocRepo.Snapshot.ContainsKey(goodId));
        Assert.True(harness.TagRepo.TagsBySlug.ContainsKey("ok"));
    }

    private const string ContentBasedThrowingParserVersion = "test-engine/1";

    private static ThrowingParserHarness CreateHarness() =>
        new(BadContent, new ArgumentException("Markdown elements in the input are too deeply nested - depth limit exceeded."));

    private static int CountEntries(ThrowingParserHarness harness, int eventId) =>
        harness.Logger.Entries.Count(entry => entry.EventId == eventId && entry.Level == LogLevel.Warning);

    private static RecordedLogEntry SingleEntry(ThrowingParserHarness harness, int eventId) =>
        harness.Logger.Entries.Single(entry => entry.EventId == eventId);
}

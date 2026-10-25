using MdExplorer.Core.Models;
using MdExplorer.Parser.Tests.Fakes;
using MdExplorer.Parser.Tests.Helpers;
using Microsoft.Extensions.Logging;

namespace MdExplorer.Parser.Tests.Services;

/// <summary>
/// Hält fest, dass eine Datei, die der Parser nicht verarbeiten kann, genau einmal
/// vollständig protokolliert und danach in Ruhe gelassen wird.
/// </summary>
/// <remarks>
/// Ohne diese Absicherung erfüllte eine gescheiterte Datei die Auswahlbedingung nach jedem
/// Fehlschlag unverändert und stünde fünf Sekunden später wieder im Stapel. In einer
/// gemessenen Protokolldatei waren das 1.948 Fehlversuche an einer einzigen Datei, 517.491
/// Stapelzeilen, 97,1 Prozent der Datei. Jede echte Diagnose rollte damit weg.
/// </remarks>
public sealed class ParseFailureThrottlingTests
{
    private const string BadContent = "DEEPLY_NESTED_PAYLOAD_FOR_TEST";

    // Die Ereignis-Ids von LogParseFailed und LogParseFailedAgain im ParseOrchestrator.
    private const int ParseFailedEventId = 206;

    private const int ParseFailedAgainEventId = 210;

    [Fact]
    public async Task RunOnce_OnFirstFailure_RecordsHashAndEngineVersion()
    {
        using OrchestratorHarness harness = CreateHarness(out _);
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
        using OrchestratorHarness harness = CreateHarness(out ContentBasedThrowingParser parser);
        _ = harness.AddSource("/r/bad.md", "hash-1", BadContent);

        await harness.Sut.RunOnceAsync(TestContext.Current.CancellationToken);
        int callsAfterFirstRun = parser.ParseCallCount;

        // Zehn weitere Durchläufe stehen für fünfzig Sekunden Abfragetakt.
        for (int i = 0; i < 10; i++)
        {
            await harness.Sut.RunOnceAsync(TestContext.Current.CancellationToken);
        }

        Assert.Equal(callsAfterFirstRun, parser.ParseCallCount);
    }

    [Fact]
    public async Task RunOnce_OnSameHash_WritesStackTraceOnlyOnce()
    {
        using OrchestratorHarness harness = CreateHarness(out _);
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
        using OrchestratorHarness harness = CreateHarness(out ContentBasedThrowingParser parser);
        Guid badId = harness.AddSource("/r/bad.md", "hash-1", BadContent);
        await harness.Sut.RunOnceAsync(TestContext.Current.CancellationToken);
        int callsAfterFirstRun = parser.ParseCallCount;

        harness.UpdateSource(badId, "hash-2", "# Jetzt lesbar");
        await harness.Sut.RunOnceAsync(TestContext.Current.CancellationToken);

        Assert.True(parser.ParseCallCount > callsAfterFirstRun);
        Assert.True(harness.DocRepo.Snapshot.ContainsKey(badId));
    }

    [Fact]
    public async Task RunOnce_OnFileParsableAgain_ClearsTheMark()
    {
        using OrchestratorHarness harness = CreateHarness(out _);
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
        using OrchestratorHarness harness = CreateHarness(out _);
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
        using OrchestratorHarness harness = CreateHarness(out ContentBasedThrowingParser parser);
        _ = harness.AddSource("/r/bad.md", "hash-1", BadContent);
        await harness.Sut.RunOnceAsync(TestContext.Current.CancellationToken);

        // Eine neue Parser-Fassung hebt den Vermerk auf, denn die Datei könnte jetzt parsbar sein.
        // Scheitert sie erneut am selben Inhalt, steht der Stapel längst im Protokoll.
        parser.EngineVersion = "test-engine/2";
        await harness.Sut.RunOnceAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, CountEntries(harness, ParseFailedEventId));
        Assert.Equal(1, CountEntries(harness, ParseFailedAgainEventId));
        Assert.Null(SingleEntry(harness, ParseFailedAgainEventId).Exception);
        Assert.NotNull(SingleEntry(harness, ParseFailedEventId).Exception);
    }

    [Fact]
    public async Task RunOnce_OnUnparsableFiles_ReportsCountToOperationStatus()
    {
        using OrchestratorHarness harness = CreateHarness(out _);
        _ = harness.AddSource("/r/bad.md", "hash-1", BadContent);
        _ = harness.AddSource("/r/good.md", "hash-2", "# Alles gut");

        await harness.Sut.RunOnceAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, harness.FailureStatus.UnparsableFileCount);
    }

    [Fact]
    public async Task RunOnce_OnUnchangedFailures_DoesNotCountAgain()
    {
        using OrchestratorHarness harness = CreateHarness(out _);
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
        using OrchestratorHarness harness = CreateHarness(out _);
        Guid badId = harness.AddSource("/r/bad.md", "hash-1", BadContent);
        Guid goodId = harness.AddSource("/r/good.md", "hash-2", "Body mit #ok.");

        await harness.Sut.RunOnceAsync(TestContext.Current.CancellationToken);

        Assert.False(harness.DocRepo.Snapshot.ContainsKey(badId));
        Assert.True(harness.DocRepo.Snapshot.ContainsKey(goodId));
        Assert.True(harness.TagRepo.TagsBySlug.ContainsKey("ok"));
    }

    [Fact]
    public async Task RunOnce_OnAnUnreadableFile_WritesTheStackTraceOnlyOnce()
    {
        // Eine gesperrte Datei wird bei jedem Takt neu versucht, weil die Sperre jederzeit enden
        // kann. Der volle Stapel darf trotzdem nur einmal ins Protokoll.
        using OrchestratorHarness harness = CreateHarness(out _);
        _ = harness.AddSource("/r/locked.md", "hash-1", "Text");
        _ = harness.FileSystem.FailOnRead.Add(LockedPath);

        for (int i = 0; i < 10; i++)
        {
            await harness.Sut.RunOnceAsync(TestContext.Current.CancellationToken);
        }

        Assert.Equal(1, harness.Logger.Entries.Count(entry => entry.EventId == ReadFailedEventId && entry.Exception is not null));
    }

    [Fact]
    public async Task RunOnce_WhenAnUnreadableFileBecomesReadable_ParsesIt()
    {
        using OrchestratorHarness harness = CreateHarness(out _);
        Guid lockedId = harness.AddSource("/r/locked.md", "hash-1", "Text mit #frei.");
        _ = harness.FileSystem.FailOnRead.Add(LockedPath);
        await harness.Sut.RunOnceAsync(TestContext.Current.CancellationToken);

        _ = harness.FileSystem.FailOnRead.Remove(LockedPath);
        await harness.Sut.RunOnceAsync(TestContext.Current.CancellationToken);

        Assert.True(harness.DocRepo.Snapshot.ContainsKey(lockedId));
    }

    [Fact]
    public async Task RunOnce_WhenTheFileFailsAgainAfterItWasRead_WritesTheStackTraceAgain()
    {
        // Eine neue Sperre nach einem erfolgreichen Lesen ist ein neues Ereignis und gehört mit
        // vollem Stapel ins Protokoll.
        using OrchestratorHarness harness = CreateHarness(out _);
        Guid lockedId = harness.AddSource("/r/locked.md", "hash-1", "Text");
        _ = harness.FileSystem.FailOnRead.Add(LockedPath);
        await harness.Sut.RunOnceAsync(TestContext.Current.CancellationToken);
        _ = harness.FileSystem.FailOnRead.Remove(LockedPath);
        await harness.Sut.RunOnceAsync(TestContext.Current.CancellationToken);

        harness.UpdateSource(lockedId, "hash-2", "Neuer Text");
        _ = harness.FileSystem.FailOnRead.Add(LockedPath);
        await harness.Sut.RunOnceAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, harness.Logger.Entries.Count(entry => entry.EventId == ReadFailedEventId && entry.Exception is not null));
    }

    private const string ContentBasedThrowingParserVersion = ContentBasedThrowingParser.InitialEngineVersion;

    // Die Ereignis-Id von LogReadFailed im ParseOrchestrator.
    private const int ReadFailedEventId = 205;

    private static readonly string LockedPath = "/r/locked.md".Replace('/', Path.DirectorySeparatorChar);

    private static OrchestratorHarness CreateHarness(out ContentBasedThrowingParser parser)
    {
        parser = new ContentBasedThrowingParser(
            OrchestratorHarness.CreateParser(),
            BadContent,
            new ArgumentException("Markdown elements in the input are too deeply nested - depth limit exceeded."));
        return new OrchestratorHarness(parser);
    }

    private static int CountEntries(OrchestratorHarness harness, int eventId) =>
        harness.Logger.Entries.Count(entry => entry.EventId == eventId && entry.Level == LogLevel.Warning);

    private static RecordedLogEntry SingleEntry(OrchestratorHarness harness, int eventId) =>
        harness.Logger.Entries.Single(entry => entry.EventId == eventId);
}

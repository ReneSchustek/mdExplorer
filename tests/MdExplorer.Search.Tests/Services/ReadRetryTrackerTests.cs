using MdExplorer.Core.Abstractions;
using MdExplorer.Search.Services;
using Microsoft.Extensions.Time.Testing;

namespace MdExplorer.Search.Tests.Services;

/// <summary>
/// Eine nicht lesbare Datei wartet mit demselben Inhaltsstand bis zum nächsten Versuch; ein neuer
/// Inhalt, ein Erfolg oder das Verschwinden aus dem Bestand beendet das Warten.
/// </summary>
public sealed class ReadRetryTrackerTests
{
    private static readonly TimeSpan Delay = TimeSpan.FromMinutes(5);
    private static readonly Guid FileId = new("00000001-0000-0000-0000-000000000000");

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 25, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Failed_FirstTime_ReportsNoRepeatAndWaits()
    {
        ReadRetryTracker sut = new(_time, Delay);

        Assert.False(sut.Failed(Document("h1")));
        Assert.True(sut.IsWaiting(Document("h1")));
    }

    [Fact]
    public void Failed_AgainWithTheSameContent_ReportsARepeat()
    {
        ReadRetryTracker sut = new(_time, Delay);
        _ = sut.Failed(Document("h1"));

        Assert.True(sut.Failed(Document("h1")));
    }

    [Fact]
    public void IsWaiting_AfterTheDelay_IsOver()
    {
        ReadRetryTracker sut = new(_time, Delay);
        _ = sut.Failed(Document("h1"));

        _time.Advance(Delay);

        Assert.False(sut.IsWaiting(Document("h1")));
    }

    [Fact]
    public void IsWaiting_WithNewContent_IsOver()
    {
        ReadRetryTracker sut = new(_time, Delay);
        _ = sut.Failed(Document("h1"));

        Assert.False(sut.IsWaiting(Document("h2")));
    }

    [Fact]
    public void Succeeded_AfterAFailure_EndsTheWait()
    {
        ReadRetryTracker sut = new(_time, Delay);
        _ = sut.Failed(Document("h1"));

        sut.Succeeded(FileId);

        Assert.False(sut.IsWaiting(Document("h1")));
        Assert.False(sut.Failed(Document("h1")));
    }

    [Fact]
    public void ForgetOtherThan_ForAFileNoLongerInTheStock_DropsItsFailure()
    {
        ReadRetryTracker sut = new(_time, Delay);
        _ = sut.Failed(Document("h1"));

        sut.ForgetOtherThan(new HashSet<Guid>());

        Assert.False(sut.IsWaiting(Document("h1")));
    }

    private static SearchSourceDocument Document(string contentHash) =>
        new(FileId, "Titel", @"C:\Wurzel\notiz", "notiz", contentHash, "{}");
}

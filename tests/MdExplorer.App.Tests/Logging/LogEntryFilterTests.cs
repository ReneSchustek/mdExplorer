using MdExplorer.App.Logging;
using Microsoft.Extensions.Logging;

namespace MdExplorer.App.Tests.Logging;

/// <summary>
/// Das Protokollfenster zeigt Einträge ab einer Stufe, mit einem Suchtext und aus einem Zeitraum
/// bis jetzt. Wer einen Fehler von eben sucht, will nicht durch den Vormittag blättern.
/// </summary>
public sealed class LogEntryFilterTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private static readonly LogLevelFilter AllLevels = LogEntryFilter.Levels[0];

    private static readonly LogPeriodFilter AllTime = LogEntryFilter.Periods[0];

    private static readonly LogPeriodFilter LastFifteenMinutes =
        LogEntryFilter.Periods.Single(period => period.Window == TimeSpan.FromMinutes(15));

    [Fact]
    public void Matches_EntryWithinThePeriod_IsShown()
        => Assert.True(LogEntryFilter.Matches(EntryAt(Now.AddMinutes(-10)), AllLevels, string.Empty, LastFifteenMinutes, Now));

    [Fact]
    public void Matches_EntryOlderThanThePeriod_IsHidden()
        => Assert.False(LogEntryFilter.Matches(EntryAt(Now.AddMinutes(-16)), AllLevels, string.Empty, LastFifteenMinutes, Now));

    [Fact]
    public void Matches_WithoutPeriod_ShowsOldEntries()
        => Assert.True(LogEntryFilter.Matches(EntryAt(Now.AddDays(-3)), AllLevels, string.Empty, AllTime, Now));

    [Fact]
    public void Matches_BelowTheChosenLevel_IsHidden()
    {
        LogLevelFilter warnings = LogEntryFilter.Levels.Single(level => level.MinimumLevel == LogLevel.Warning);

        Assert.False(LogEntryFilter.Matches(EntryAt(Now), warnings, string.Empty, AllTime, Now));
    }

    [Theory]
    [InlineData("indexer", true)]
    [InlineData("GESPEICHERT", true)]
    [InlineData("Graph", false)]
    public void Matches_SearchText_LooksAtMessageAndSource(string query, bool expected)
        => Assert.Equal(expected, LogEntryFilter.Matches(EntryAt(Now), AllLevels, query, AllTime, Now));

    [Fact]
    public void Periods_AllHaveATextAndGrowLonger()
    {
        Assert.All(LogEntryFilter.Periods, period => Assert.DoesNotContain("[?", period.Display, StringComparison.Ordinal));
        Assert.Equal(
            LogEntryFilter.Periods.Skip(1).Select(period => period.Window).Order(),
            LogEntryFilter.Periods.Skip(1).Select(period => period.Window));
    }

    private static LogEntry EntryAt(DateTimeOffset timestamp) =>
        new(timestamp, LogLevel.Information, "MdExplorer.Indexer", "Datei gespeichert.", Exception: null);
}

using System.IO;
using MdExplorer.App.ViewModels;
using Microsoft.Extensions.Logging;

namespace MdExplorer.App.Tests.ViewModels;

/// <summary>
/// Ein Vorgang, auf den niemand wartet, meldet seinen Fehler im Protokoll statt ihn zu verschlucken.
/// </summary>
public sealed class ObservedTaskTests
{
    [Fact]
    public void Forget_WithAFailedTask_LogsTheError()
    {
        RecordingLogger logger = new();
        IOException failure = new("Datei gesperrt");

        ObservedTask.Forget(Task.FromException(failure), logger);

        Assert.Same(failure, Assert.Single(logger.Errors));
    }

    [Fact]
    public void Forget_WithACancelledTask_LogsNothing()
    {
        RecordingLogger logger = new();

        ObservedTask.Forget(Task.FromCanceled(new CancellationToken(canceled: true)), logger);

        Assert.Empty(logger.Errors);
    }

    [Fact]
    public void Forget_WithASuccessfulTask_LogsNothing()
    {
        RecordingLogger logger = new();

        ObservedTask.Forget(Task.CompletedTask, logger);

        Assert.Empty(logger.Errors);
    }

    /// <summary>Hält die gemeldeten Fehler fest.</summary>
    private sealed class RecordingLogger : ILogger
    {
        public List<Exception> Errors { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Error && exception is not null)
            {
                Errors.Add(exception);
            }
        }
    }
}

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MdExplorer.Data.Sqlite;

/// <summary>
/// EF-Core-Execution-Strategy mit exponentiellem Backoff für transiente
/// SQLite-Lock-Konflikte. Pendant zu <c>SqlServerRetryingExecutionStrategy</c>.
/// Wiederholt Operationen, die mit <c>SQLITE_BUSY</c> (5) oder
/// <c>SQLITE_LOCKED</c> (6) abgebrochen wurden, also wenn der
/// <c>busy_timeout</c> nicht ausgereicht hat, um den Lock abzuwarten.
/// </summary>
/// <remarks>
/// Manuell gestartete Transaktionen über <c>SqliteConnection.BeginTransaction</c>
/// sind nicht betroffen, weil sie die Strategy nicht durchlaufen
/// (siehe <see cref="MdExplorer.Data.Repositories.SqliteSearchIndexStorage"/>).
/// </remarks>
public sealed class SqliteRetryingExecutionStrategy : ExecutionStrategy
{
    /// <summary>Standard-Wert für die Anzahl Wiederholungen, derselbe wie beim Vorbild für SQL Server.</summary>
    public const int DefaultRetryCount = 6;

    /// <summary>
    /// Standard-Maximalverzögerung zwischen zwei Versuchen. Deutlich kürzer als die 30 Sekunden
    /// des SQL-Server-Vorbilds: Gewartet wird auf eine lokale Datei, nicht auf einen Server.
    /// </summary>
    public static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromSeconds(2);

    // Primäre Ergebniscodes laut SQLite-Dokumentation ("Result and Error Codes").
    private const int SqliteBusyErrorCode = 5;
    private const int SqliteLockedErrorCode = 6;

    private const string RetryLimitMessage = "Die Datenbank blieb über alle Wiederholungsversuche belegt.";

    /// <summary>Erzeugt die Strategy mit den Default-Werten.</summary>
    public SqliteRetryingExecutionStrategy(ExecutionStrategyDependencies dependencies)
        : base(dependencies, DefaultRetryCount, DefaultRetryDelay)
    {
    }

    /// <summary>Erzeugt die Strategy mit individuellem Retry-Budget.</summary>
    public SqliteRetryingExecutionStrategy(
        ExecutionStrategyDependencies dependencies,
        int maxRetryCount,
        TimeSpan maxRetryDelay)
        : base(dependencies, maxRetryCount, maxRetryDelay)
    {
    }

    /// <inheritdoc />
    /// <remarks>
    /// Ein erschöpftes Wiederholungsbudget meldet EF Core als <see cref="RetryLimitExceededException"/>,
    /// die nicht von <see cref="System.Data.Common.DbException"/> erbt. Die Dienste fangen aber genau
    /// diesen Basistyp; deshalb geht der Fehler als <see cref="DatabaseOperationException"/> weiter.
    /// </remarks>
    public override TResult Execute<TState, TResult>(
        TState state,
        Func<DbContext, TState, TResult> operation,
        Func<DbContext, TState, ExecutionResult<TResult>>? verifySucceeded)
    {
        try
        {
            return base.Execute(state, operation, verifySucceeded);
        }
        catch (RetryLimitExceededException exception)
        {
            throw new DatabaseOperationException(RetryLimitMessage, exception);
        }
    }

    /// <inheritdoc />
    /// <remarks>Übersetzt wie <see cref="Execute{TState, TResult}"/>.</remarks>
    public override async Task<TResult> ExecuteAsync<TState, TResult>(
        TState state,
        Func<DbContext, TState, CancellationToken, Task<TResult>> operation,
        Func<DbContext, TState, CancellationToken, Task<ExecutionResult<TResult>>>? verifySucceeded,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.ExecuteAsync(state, operation, verifySucceeded, cancellationToken).ConfigureAwait(false);
        }
        catch (RetryLimitExceededException exception)
        {
            throw new DatabaseOperationException(RetryLimitMessage, exception);
        }
    }

    /// <inheritdoc />
    protected override bool ShouldRetryOn(Exception exception)
    {
        return exception is SqliteException sqlite
            && (sqlite.SqliteErrorCode == SqliteBusyErrorCode
                || sqlite.SqliteErrorCode == SqliteLockedErrorCode);
    }
}

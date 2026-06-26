using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Storage;

namespace MdExplorer.Data.Sqlite;

/// <summary>
/// EF-Core-Execution-Strategy mit exponentiellem Backoff für transiente
/// SQLite-Lock-Konflikte. Pendant zu <c>SqlServerRetryingExecutionStrategy</c>.
/// Wiederholt Operationen, die mit <c>SQLITE_BUSY</c> (5) oder
/// <c>SQLITE_LOCKED</c> (6) abgebrochen wurden — also wenn der
/// <c>busy_timeout</c> nicht ausgereicht hat, um den Lock abzuwarten.
/// </summary>
/// <remarks>
/// Manuell gestartete Transaktionen über <c>SqliteConnection.BeginTransaction</c>
/// sind nicht betroffen, weil sie die Strategy nicht durchlaufen
/// (siehe <see cref="MdExplorer.Data.Repositories.SqliteSearchIndexStorage"/>).
/// </remarks>
public sealed class SqliteRetryingExecutionStrategy : ExecutionStrategy
{
    /// <summary>Standard-Wert für die Anzahl Wiederholungen.</summary>
    public const int DefaultRetryCount = 6;

    /// <summary>Standard-Maximalverzögerung zwischen zwei Versuchen.</summary>
    public static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromSeconds(2);

    private const int SqliteBusyErrorCode = 5;
    private const int SqliteLockedErrorCode = 6;

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
    protected override bool ShouldRetryOn(Exception exception)
    {
        return exception is SqliteException sqlite
            && (sqlite.SqliteErrorCode == SqliteBusyErrorCode
                || sqlite.SqliteErrorCode == SqliteLockedErrorCode);
    }
}

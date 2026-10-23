using System.Data.Common;

namespace Themis.Kernel.Contracts.Persistence;

/// <summary>
/// The one connection and one transaction of a command (D-41, R-DB-01). Every statement, raw SQL included, goes
/// through it, and it switches to the owning module's role whenever the current role differs. Nothing opens a
/// second connection. The pipeline owns the lifecycle; handlers only execute statements.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>The role the connection currently runs as; null before the first switch.</summary>
    DatabaseRole? CurrentRole { get; }

    /// <summary>Issues SET LOCAL ROLE when the current role differs (R-DB-01).</summary>
    Task EnsureRoleAsync(DatabaseRole role, CancellationToken cancellationToken);

    /// <summary>
    /// The shared connection and transaction, for a module's data context to enlist in, handed out only after the
    /// connection switched to <paramref name="role"/> (RR-62, R-DB-01). The role holds until the next call into the
    /// kernel, which hands the connection back as themis_app; a module's data context therefore switches again before
    /// each statement through a command interceptor that calls <see cref="EnsureRoleAsync"/>.
    /// </summary>
    Task<UnitOfWorkConnection> ForRoleAsync(DatabaseRole role, CancellationToken cancellationToken);

    Task<int> ExecuteAsync(DatabaseRole role, string sql, IReadOnlyDictionary<string, object?>? parameters, CancellationToken cancellationToken);

    Task<object?> ScalarAsync(DatabaseRole role, string sql, IReadOnlyDictionary<string, object?>? parameters, CancellationToken cancellationToken);

    Task<IReadOnlyList<T>> QueryAsync<T>(DatabaseRole role, string sql, IReadOnlyDictionary<string, object?>? parameters, Func<DbDataReader, T> map, CancellationToken cancellationToken);
}

/// <summary>
/// The pipeline's view of the unit of work: lifecycle, savepoints and the transaction-scoped advisory lock the
/// idempotency check uses so that a concurrent duplicate is refused without blocking (R-API-01). Kernel use only.
/// </summary>
public interface IKernelUnitOfWork : IUnitOfWork, IAsyncDisposable
{
    /// <summary>
    /// SET LOCAL ROLE NONE and forget the tracked role, so the connection runs as themis_app again (RR-62, R-DB-01).
    /// Only on a healthy transaction: after a failed statement PostgreSQL answers 25P02, which would hide the
    /// original SQLSTATE (40P01, 40001, 23505, 22003).
    /// </summary>
    Task ResetRoleAsync(CancellationToken cancellationToken);

    Task SavepointAsync(string name, CancellationToken cancellationToken);

    Task RollbackToSavepointAsync(string name, CancellationToken cancellationToken);

    Task ReleaseSavepointAsync(string name, CancellationToken cancellationToken);

    /// <summary>pg_try_advisory_xact_lock: true when acquired, false when another transaction holds it. Released at commit or rollback.</summary>
    Task<bool> TryAdvisoryLockAsync(long key, CancellationToken cancellationToken);

    Task CommitAsync(CancellationToken cancellationToken);

    Task RollbackAsync(CancellationToken cancellationToken);
}

/// <summary>The shared connection and transaction a module's data context enlists in (RR-62).</summary>
public sealed record UnitOfWorkConnection(DbConnection Connection, DbTransaction Transaction);

/// <summary>Opens the command's connection as themis_app and begins its transaction. Kernel use only.</summary>
public interface IUnitOfWorkFactory
{
    Task<IKernelUnitOfWork> BeginAsync(CancellationToken cancellationToken);
}

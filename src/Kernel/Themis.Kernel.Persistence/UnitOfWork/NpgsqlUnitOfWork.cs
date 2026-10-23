using System.Data;
using System.Data.Common;
using Npgsql;
using Themis.Kernel.Contracts.Persistence;

namespace Themis.Kernel.Persistence.UnitOfWork;

/// <summary>
/// One connection as themis_app and one transaction per command (D-41). Every statement goes through it, and it
/// issues SET LOCAL ROLE to the owning module's role whenever the current role differs (R-DB-01). SET LOCAL is
/// transaction-scoped, so pooled connections return clean, and a rollback to a savepoint also undoes a role switch
/// made after it, which is why the tracked role is forgotten on such a rollback. Modules reach the connection only
/// through <see cref="ForRoleAsync"/>, which switches first; the kernel context factory and the tests read the
/// persistence-internal <see cref="Connection"/> and <see cref="Transaction"/> (RR-62).
/// </summary>
internal sealed class NpgsqlUnitOfWork : IKernelUnitOfWork
{
    private readonly NpgsqlConnection _connection;
    private readonly NpgsqlTransaction _transaction;
    private DatabaseRole? _currentRole;
    private bool _completed;
    private bool _disposed;

    private NpgsqlUnitOfWork(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        _connection = connection;
        _transaction = transaction;
    }

    public DatabaseRole? CurrentRole => _currentRole;

    /// <summary>Persistence-internal: the kernel data context enlists here; not part of <see cref="IUnitOfWork"/> (RR-62).</summary>
    public DbConnection Connection => _connection;

    /// <summary>Persistence-internal, like <see cref="Connection"/>.</summary>
    public DbTransaction Transaction => _transaction;

    public static async Task<NpgsqlUnitOfWork> BeginAsync(NpgsqlDataSource dataSource, CancellationToken cancellationToken, bool readOnly = false)
    {
        NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        try
        {
            NpgsqlTransaction transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            NpgsqlUnitOfWork unitOfWork = new(connection, transaction);
            if (readOnly)
            {
                // RR-81: a query's transaction refuses every write and row lock with 25006, whatever its code tries.
                await using NpgsqlCommand command = unitOfWork.CreateCommand("SET TRANSACTION READ ONLY", null);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            return unitOfWork;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public async Task EnsureRoleAsync(DatabaseRole role, CancellationToken cancellationToken)
    {
        if (_currentRole == role)
        {
            return;
        }

        await using NpgsqlCommand command = CreateCommand($"SET LOCAL ROLE \"{role.Name}\"", null);
        await command.ExecuteNonQueryAsync(cancellationToken);
        _currentRole = role;
    }

    public async Task<UnitOfWorkConnection> ForRoleAsync(DatabaseRole role, CancellationToken cancellationToken)
    {
        await EnsureRoleAsync(role, cancellationToken);
        return new UnitOfWorkConnection(_connection, _transaction);
    }

    public async Task ResetRoleAsync(CancellationToken cancellationToken)
    {
        // RR-62: the caller guarantees a healthy transaction (never after a failed statement, where 25P02 would hide the error).
        await using NpgsqlCommand command = CreateCommand("SET LOCAL ROLE NONE", null);
        await command.ExecuteNonQueryAsync(cancellationToken);
        _currentRole = null;
    }

    /// <summary>Synchronous twin for the data context interceptor's synchronous paths.</summary>
    public void EnsureRole(DatabaseRole role)
    {
        if (_currentRole == role)
        {
            return;
        }

        using NpgsqlCommand command = CreateCommand($"SET LOCAL ROLE \"{role.Name}\"", null);
        command.ExecuteNonQuery();
        _currentRole = role;
    }

    public async Task<int> ExecuteAsync(DatabaseRole role, string sql, IReadOnlyDictionary<string, object?>? parameters, CancellationToken cancellationToken)
    {
        await EnsureRoleAsync(role, cancellationToken);
        await using NpgsqlCommand command = CreateCommand(sql, parameters);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<object?> ScalarAsync(DatabaseRole role, string sql, IReadOnlyDictionary<string, object?>? parameters, CancellationToken cancellationToken)
    {
        await EnsureRoleAsync(role, cancellationToken);
        await using NpgsqlCommand command = CreateCommand(sql, parameters);
        object? value = await command.ExecuteScalarAsync(cancellationToken);
        return value is DBNull ? null : value;
    }

    public async Task<IReadOnlyList<T>> QueryAsync<T>(DatabaseRole role, string sql, IReadOnlyDictionary<string, object?>? parameters, Func<DbDataReader, T> map, CancellationToken cancellationToken)
    {
        await EnsureRoleAsync(role, cancellationToken);
        await using NpgsqlCommand command = CreateCommand(sql, parameters);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        List<T> rows = [];
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(map(reader));
        }

        return rows;
    }

    public Task SavepointAsync(string name, CancellationToken cancellationToken) => _transaction.SaveAsync(name, cancellationToken);

    public async Task RollbackToSavepointAsync(string name, CancellationToken cancellationToken)
    {
        await _transaction.RollbackAsync(name, cancellationToken);
        _currentRole = null;
    }

    public Task ReleaseSavepointAsync(string name, CancellationToken cancellationToken) => _transaction.ReleaseAsync(name, cancellationToken);

    public async Task<bool> TryAdvisoryLockAsync(long key, CancellationToken cancellationToken)
    {
        object? acquired = await ScalarAsync(DatabaseRole.Kernel, "SELECT pg_try_advisory_xact_lock(@key)", new Dictionary<string, object?> { ["key"] = key }, cancellationToken);
        return acquired is true;
    }

    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        // RR-47: a commit that fails (a deferred constraint trigger, a lost connection) ends the transaction on the server
        // side too, so the unit of work counts as completed either way and disposal never tries to roll back a finished
        // transaction and hide the commit's error.
        try
        {
            await _transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            _completed = true;
        }
    }

    public async Task RollbackAsync(CancellationToken cancellationToken)
    {
        if (!_completed)
        {
            await _transaction.RollbackAsync(cancellationToken);
            _completed = true;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await RollbackAsync(CancellationToken.None);
        await _transaction.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private NpgsqlCommand CreateCommand(string sql, IReadOnlyDictionary<string, object?>? parameters)
    {
        NpgsqlCommand command = _connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = _transaction;
        if (parameters is not null)
        {
            foreach ((string name, object? value) in parameters)
            {
                command.Parameters.AddWithValue(name, value ?? DBNull.Value);
            }
        }

        return command;
    }
}

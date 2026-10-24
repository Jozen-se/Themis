using System.Data.Common;
using Themis.Kernel.Contracts.Persistence;

namespace Themis.Kernel.Persistence.Seeding;

/// <summary>
/// The unit of work a seeder hands to a kernel Contracts service (milestone 2 step 3, D-64): the fixture party's
/// number comes from the PARTY series through the same numbering service COMPANY.CREATE uses, run on the seeder's
/// own connection and transaction as themis_migrator, the table owner, which switches to no module role. Role
/// switching and the reset are therefore no-ops here; savepoints, advisory locks and the lifecycle belong to the
/// pipeline and are refused. Seeding only; never registered in a host.
/// </summary>
internal sealed class SeedingUnitOfWork : IKernelUnitOfWork
{
    private readonly DbConnection _connection;
    private readonly DbTransaction _transaction;

    public SeedingUnitOfWork(DbConnection connection, DbTransaction transaction)
    {
        _connection = connection;
        _transaction = transaction;
    }

    public DatabaseRole? CurrentRole => null;

    public Task EnsureRoleAsync(DatabaseRole role, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task ResetRoleAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<UnitOfWorkConnection> ForRoleAsync(DatabaseRole role, CancellationToken cancellationToken) =>
        Task.FromResult(new UnitOfWorkConnection(_connection, _transaction));

    public async Task<int> ExecuteAsync(DatabaseRole role, string sql, IReadOnlyDictionary<string, object?>? parameters, CancellationToken cancellationToken)
    {
        await using DbCommand command = Prepare(sql, parameters);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<object?> ScalarAsync(DatabaseRole role, string sql, IReadOnlyDictionary<string, object?>? parameters, CancellationToken cancellationToken)
    {
        await using DbCommand command = Prepare(sql, parameters);
        object? result = await command.ExecuteScalarAsync(cancellationToken);
        return result is DBNull ? null : result;
    }

    public async Task<IReadOnlyList<T>> QueryAsync<T>(DatabaseRole role, string sql, IReadOnlyDictionary<string, object?>? parameters, Func<DbDataReader, T> map, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(map);
        await using DbCommand command = Prepare(sql, parameters);
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        List<T> rows = [];
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(map(reader));
        }

        return rows;
    }

    public Task SavepointAsync(string name, CancellationToken cancellationToken) => throw NotSeeding();

    public Task RollbackToSavepointAsync(string name, CancellationToken cancellationToken) => throw NotSeeding();

    public Task ReleaseSavepointAsync(string name, CancellationToken cancellationToken) => throw NotSeeding();

    public Task<bool> TryAdvisoryLockAsync(long key, CancellationToken cancellationToken) => throw NotSeeding();

    public Task CommitAsync(CancellationToken cancellationToken) => throw NotSeeding();

    public Task RollbackAsync(CancellationToken cancellationToken) => throw NotSeeding();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private DbCommand Prepare(string sql, IReadOnlyDictionary<string, object?>? parameters)
    {
        DbCommand command = _connection.CreateCommand();
        command.Transaction = _transaction;
        command.CommandText = sql;
        foreach ((string name, object? value) in parameters ?? new Dictionary<string, object?>())
        {
            DbParameter parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }

        return command;
    }

    private static InvalidOperationException NotSeeding() => new("The seeding unit of work runs statements only; the lifecycle belongs to the seeder's own transaction.");
}

using System.Data.Common;
using Themis.FixtureModule.Application;
using Themis.FixtureModule.Contracts;
using Themis.FixtureModule.Domain;
using Themis.Kernel.Contracts.Persistence;

namespace Themis.FixtureModule.Infrastructure;

/// <summary>
/// Raw SQL on the command's unit of work, every statement as themis_fixture (R-DB-01, D-41). The table is created
/// by the integration tests as themis_migrator with the grants of the fixture privilege matrix (I-14).
/// </summary>
internal sealed class SqlCounterStore : ICounterStore
{
    public const string Table = FixtureContracts.SchemaName + ".counter";

    private static readonly DatabaseRole Role = DatabaseRole.From(FixtureContracts.RoleName);

    public async Task<Counter?> FindAsync(IUnitOfWork unitOfWork, Guid id, CancellationToken cancellationToken)
    {
        IReadOnlyList<Counter> rows = await unitOfWork.QueryAsync(Role,
            $"SELECT id, value, version, sample FROM {Table} WHERE id = @id FOR UPDATE",
            new Dictionary<string, object?> { ["id"] = id },
            Read,
            cancellationToken);
        return rows.Count == 0 ? null : rows[0];
    }

    public Task InsertAsync(IUnitOfWork unitOfWork, Counter counter, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteAsync(Role,
            $"INSERT INTO {Table} (id, value, version, sample) VALUES (@id, @value, @version, @sample)",
            Parameters(counter),
            cancellationToken);

    public async Task UpdateAsync(IUnitOfWork unitOfWork, Counter counter, CancellationToken cancellationToken)
    {
        // R-API-02: the row moves from the previous version only; FindAsync locked it, so zero rows is a bug.
        int rows = await unitOfWork.ExecuteAsync(Role,
            $"UPDATE {Table} SET value = @value, version = @version, sample = @sample WHERE id = @id AND version = @version - 1",
            Parameters(counter),
            cancellationToken);
        if (rows != 1)
        {
            throw new InvalidOperationException($"Counter {counter.Id} was not at version {counter.Version - 1}.");
        }
    }

    public Task WriteKernelTableAsync(IUnitOfWork unitOfWork, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteAsync(Role,
            "INSERT INTO kernel.audit_event (id, occurred_at, user_id, command_name, entity_type, entity_id, changes) VALUES (gen_random_uuid(), now(), gen_random_uuid(), 'FIXTURE', 'Fixture', gen_random_uuid(), '{}')",
            null,
            cancellationToken);

    private static Counter Read(DbDataReader reader) =>
        new(reader.GetGuid(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetDecimal(3));

    private static Dictionary<string, object?> Parameters(Counter counter) => new()
    {
        ["id"] = counter.Id,
        ["value"] = counter.Value,
        ["version"] = counter.Version,
        ["sample"] = counter.Sample,
    };
}

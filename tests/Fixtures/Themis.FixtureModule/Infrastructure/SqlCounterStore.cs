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

    private const string Columns = "id, company_id, site_id, value, version, sample, posted";

    private static readonly DatabaseRole Role = DatabaseRole.From(FixtureContracts.RoleName);

    public async Task<Counter?> FindAsync(IUnitOfWork unitOfWork, Guid id, CancellationToken cancellationToken)
    {
        IReadOnlyList<Counter> rows = await unitOfWork.QueryAsync(Role,
            $"SELECT {Columns} FROM {Table} WHERE id = @id FOR UPDATE",
            new Dictionary<string, object?> { ["id"] = id },
            Read,
            cancellationToken);
        return rows.Count == 0 ? null : rows[0];
    }

    public Task InsertAsync(IUnitOfWork unitOfWork, Counter counter, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteAsync(Role,
            $"INSERT INTO {Table} ({Columns}) VALUES (@id, @company, @site, @value, @version, @sample, @posted)",
            Parameters(counter),
            cancellationToken);

    public async Task UpdateAsync(IUnitOfWork unitOfWork, Counter counter, CancellationToken cancellationToken)
    {
        // R-API-02: the row moves from the previous version only; FindAsync locked it, so zero rows is a bug.
        int rows = await unitOfWork.ExecuteAsync(Role,
            $"UPDATE {Table} SET value = @value, version = @version, sample = @sample, posted = @posted WHERE id = @id AND version = @version - 1",
            Parameters(counter),
            cancellationToken);
        if (rows != 1)
        {
            throw new InvalidOperationException($"Counter {counter.Id} was not at version {counter.Version - 1}.");
        }
    }

    public Task WriteKernelTableAsync(IUnitOfWork unitOfWork, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteAsync(Role,
            "INSERT INTO kernel.audit_event (id, command_id, occurred_at, user_id, command_name, entity_type, entity_id, changes) VALUES (gen_random_uuid(), gen_random_uuid(), now(), gen_random_uuid(), 'FIXTURE', 'Fixture', gen_random_uuid(), '{}')",
            null,
            cancellationToken);

    public Task OverflowSampleAsync(IUnitOfWork unitOfWork, Guid id, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteAsync(Role,
            $"UPDATE {Table} SET sample = sample * 1e20 + 1e17 WHERE id = @id",
            new Dictionary<string, object?> { ["id"] = id },
            cancellationToken);

    private static Counter Read(DbDataReader reader) =>
        new(reader.GetGuid(0), reader.GetGuid(1), reader.IsDBNull(2) ? null : reader.GetGuid(2), reader.GetInt64(3), reader.GetInt64(4), reader.GetDecimal(5), reader.GetBoolean(6));

    private static Dictionary<string, object?> Parameters(Counter counter) => new()
    {
        ["id"] = counter.Id,
        ["company"] = counter.CompanyId,
        ["site"] = counter.SiteId,
        ["value"] = counter.Value,
        ["version"] = counter.Version,
        ["sample"] = counter.Sample,
        ["posted"] = counter.Posted,
    };
}

using Npgsql;
using Themis.Kernel.Contracts.Persistence;
using Themis.Kernel.Persistence.UnitOfWork;

namespace Themis.Integration.Tests.Pipeline;

/// <summary>
/// The unit of work switches roles inside one transaction on one connection (D-41, R-DB-01): the login role holds
/// nothing, every module role sees only its own schema, and a rollback to a savepoint restores the role.
/// </summary>
[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Integration")]
public sealed class UnitOfWorkRoleTests
{
    private static readonly DatabaseRole Fixture = DatabaseRole.From("themis_fixture");

    private readonly PostgresFixture _db;

    public UnitOfWorkRoleTests(PostgresFixture db)
    {
        _db = db;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task D_41_one_transaction_switches_between_the_fixture_and_kernel_roles_and_each_sees_its_own_tables()
    {
        await using NpgsqlDataSource dataSource = NpgsqlDataSource.Create(_db.ConnectionStringFor("themis_app"));
        await using NpgsqlUnitOfWork unitOfWork = await NpgsqlUnitOfWork.BeginAsync(dataSource, Ct);

        Assert.Null(unitOfWork.CurrentRole);
        Assert.Equal("themis_fixture", await unitOfWork.ScalarAsync(Fixture, "SELECT current_user", null, Ct));
        Assert.Equal(Fixture, unitOfWork.CurrentRole);
        Assert.Equal("themis_kernel", await unitOfWork.ScalarAsync(DatabaseRole.Kernel, "SELECT current_user", null, Ct));
        Assert.Equal(DatabaseRole.Kernel, unitOfWork.CurrentRole);

        // The kernel role reads its tables; the fixture role is refused on them (I-14).
        Assert.Equal(0L, await unitOfWork.ScalarAsync(DatabaseRole.Kernel, "SELECT count(*) FROM kernel.idempotency_record WHERE key = 'none'", null, Ct));
        await unitOfWork.SavepointAsync("probe", Ct);
        PostgresException refused = await Assert.ThrowsAsync<PostgresException>(() => unitOfWork.ScalarAsync(Fixture, "SELECT count(*) FROM kernel.idempotency_record", null, Ct));
        Assert.Equal("42501", refused.SqlState);

        // Rolling back to the savepoint restores the role that was current when it was taken, and the unit of work
        // forgets its cached role so that the next statement switches explicitly again.
        await unitOfWork.RollbackToSavepointAsync("probe", Ct);
        Assert.Null(unitOfWork.CurrentRole);
        Assert.Equal("themis_kernel", await unitOfWork.ScalarAsync(DatabaseRole.Kernel, "SELECT current_user", null, Ct));
        await unitOfWork.RollbackAsync(Ct);
    }

    [Fact]
    public async Task R_DB_01_the_login_role_sees_nothing_before_the_first_switch()
    {
        await using NpgsqlDataSource dataSource = NpgsqlDataSource.Create(_db.ConnectionStringFor("themis_app"));
        await using NpgsqlUnitOfWork unitOfWork = await NpgsqlUnitOfWork.BeginAsync(dataSource, Ct);

        await using NpgsqlCommand direct = new("SELECT count(*) FROM kernel.idempotency_record", (NpgsqlConnection)unitOfWork.Connection, (NpgsqlTransaction)unitOfWork.Transaction);
        PostgresException refused = await Assert.ThrowsAsync<PostgresException>(() => direct.ExecuteScalarAsync(Ct));
        Assert.Equal("42501", refused.SqlState);
    }

    [Fact]
    public async Task R_API_01_the_advisory_lock_is_held_for_the_transaction_only()
    {
        await using NpgsqlDataSource dataSource = NpgsqlDataSource.Create(_db.ConnectionStringFor("themis_app"));
        await using NpgsqlUnitOfWork first = await NpgsqlUnitOfWork.BeginAsync(dataSource, Ct);
        await using NpgsqlUnitOfWork second = await NpgsqlUnitOfWork.BeginAsync(dataSource, Ct);

        Assert.True(await first.TryAdvisoryLockAsync(424242, Ct));
        Assert.False(await second.TryAdvisoryLockAsync(424242, Ct));
        await first.CommitAsync(Ct);
        Assert.True(await second.TryAdvisoryLockAsync(424242, Ct));
        await second.RollbackAsync(Ct);
    }
}

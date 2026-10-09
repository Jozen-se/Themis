using Npgsql;

namespace Themis.IntegrationTests;

/// <summary>
/// Proves the database role model of db/init/01-roles.sql by attempting forbidden writes (I-14, R-DB-01, D-41,
/// R-LED-06). The tables used here are probes created by the test; the walking skeleton extends the test to the real
/// ledger and audit tables once they exist.
/// </summary>
[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Integration")]
public sealed class DatabaseRoleModelTests : IAsyncLifetime
{
    private const string InsufficientPrivilege = "42501";

    private readonly PostgresFixture _db;

    public DatabaseRoleModelTests(PostgresFixture db)
    {
        _db = db;
    }

    public async ValueTask InitializeAsync()
    {
        // Probe tables are created the way migrations will create real tables: as themis_owner, so that the
        // default privileges of the schema apply. "other_module" stands in for a future module's schema.
        await _db.ExecuteAsSuperuserAsync(
            """
            SET ROLE themis_owner;
            CREATE TABLE IF NOT EXISTS kernel.probe_ledger (id integer PRIMARY KEY, note text);
            RESET ROLE;
            CREATE SCHEMA IF NOT EXISTS other_module AUTHORIZATION themis_owner;
            SET ROLE themis_owner;
            CREATE TABLE IF NOT EXISTS other_module.probe (id integer PRIMARY KEY);
            RESET ROLE;
            """,
            TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _db.ExecuteAsSuperuserAsync(
            """
            DROP TABLE IF EXISTS kernel.probe_ledger;
            DROP SCHEMA IF EXISTS other_module CASCADE;
            """,
            CancellationToken.None);
    }

    [Fact]
    public async Task Login_role_holds_no_table_privileges()
    {
        await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(_db.ConnectionStringFor("themis_app"), TestContext.Current.CancellationToken);

        await AssertRefusedAsync(connection, "INSERT INTO kernel.probe_ledger (id, note) VALUES (1, 'x')");
        await AssertRefusedAsync(connection, "SELECT count(*) FROM kernel.probe_ledger");
    }

    [Fact]
    public async Task Login_role_cannot_create_objects()
    {
        await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(_db.ConnectionStringFor("themis_app"), TestContext.Current.CancellationToken);

        await AssertRefusedAsync(connection, "CREATE TABLE kernel.rogue (id integer)");
        await AssertRefusedAsync(connection, "CREATE TABLE public.rogue (id integer)");
    }

    [Fact]
    public async Task Login_role_cannot_assume_the_owner_role()
    {
        await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(_db.ConnectionStringFor("themis_app"), TestContext.Current.CancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(TestContext.Current.CancellationToken);

        await AssertRefusedAsync(connection, "SET LOCAL ROLE themis_owner", insideTransaction: true);
    }

    [Fact]
    public async Task Module_role_writes_inside_its_own_schema()
    {
        await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(_db.ConnectionStringFor("themis_app"), TestContext.Current.CancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(TestContext.Current.CancellationToken);

        await ExecuteAsync(connection, "SET LOCAL ROLE themis_kernel");
        await ExecuteAsync(connection, "INSERT INTO kernel.probe_ledger (id, note) VALUES (1, 'kernel write')");
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT count(*) FROM kernel.probe_ledger"));

        await transaction.RollbackAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Module_role_is_refused_outside_its_own_schema()
    {
        await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(_db.ConnectionStringFor("themis_app"), TestContext.Current.CancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(TestContext.Current.CancellationToken);

        await ExecuteAsync(connection, "SET LOCAL ROLE themis_kernel");
        await AssertRefusedAsync(connection, "INSERT INTO other_module.probe (id) VALUES (1)", insideTransaction: true);
    }

    [Fact]
    public async Task Module_role_cannot_update_or_delete_by_default()
    {
        // R-LED-06: the schema default grants SELECT and INSERT only. Mutable tables receive UPDATE and DELETE
        // explicitly from their migration; ledger and audit tables never do.
        await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(_db.ConnectionStringFor("themis_app"), TestContext.Current.CancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(TestContext.Current.CancellationToken);

        await ExecuteAsync(connection, "SET LOCAL ROLE themis_kernel");
        await ExecuteAsync(connection, "INSERT INTO kernel.probe_ledger (id, note) VALUES (1, 'posted')");
        await AssertRefusedAsync(connection, "UPDATE kernel.probe_ledger SET note = 'edited' WHERE id = 1", insideTransaction: true);
        await AssertRefusedAsync(connection, "DELETE FROM kernel.probe_ledger WHERE id = 1", insideTransaction: true);
    }

    [Fact]
    public async Task Role_switch_ends_with_the_transaction()
    {
        await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(_db.ConnectionStringFor("themis_app"), TestContext.Current.CancellationToken);

        await using (NpgsqlTransaction transaction = await connection.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            await ExecuteAsync(connection, "SET LOCAL ROLE themis_kernel");
            Assert.Equal("themis_kernel", await ScalarAsync(connection, "SELECT current_user"));
            await transaction.CommitAsync(TestContext.Current.CancellationToken);
        }

        Assert.Equal("themis_app", await ScalarAsync(connection, "SELECT current_user"));
    }

    [Fact]
    public async Task Owner_role_is_a_plain_role_without_superuser_rights()
    {
        await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(_db.SuperuserConnectionString, TestContext.Current.CancellationToken);

        object? superuser = await ScalarAsync(connection, "SELECT rolsuper FROM pg_roles WHERE rolname = 'themis_owner'");
        object? inherits = await ScalarAsync(connection, "SELECT rolinherit FROM pg_roles WHERE rolname = 'themis_app'");

        Assert.Equal(false, superuser);
        Assert.Equal(false, inherits);
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using NpgsqlCommand command = new(sql, connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<object?> ScalarAsync(NpgsqlConnection connection, string sql)
    {
        await using NpgsqlCommand command = new(sql, connection);
        return await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
    }

    // A refused statement aborts the enclosing transaction, so inside one each probe runs under a savepoint.
    private static async Task AssertRefusedAsync(NpgsqlConnection connection, string sql, bool insideTransaction = false)
    {
        if (insideTransaction)
        {
            await ExecuteAsync(connection, "SAVEPOINT probe");
        }

        PostgresException exception = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connection, sql));
        Assert.Equal(InsufficientPrivilege, exception.SqlState);

        if (insideTransaction)
        {
            await ExecuteAsync(connection, "ROLLBACK TO SAVEPOINT probe");
        }
    }
}

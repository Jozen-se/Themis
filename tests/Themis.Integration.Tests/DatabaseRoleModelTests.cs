using Npgsql;
using Themis.Kernel.Persistence.Privileges;

namespace Themis.Integration.Tests;

/// <summary>
/// Proves the role model of db/bootstrap/01-roles.sql by attempting forbidden writes (I-14, R-DB-01, D-41, R-LED-06).
/// Probe tables stand in for the real tables until the first migration; they are created as themis_migrator and
/// granted exactly as migrations will grant, through <see cref="GrantSql"/>.
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
        string grants = string.Join("\n",
            GrantSql.Statements(TableGrant.AppendOnly("kernel", "probe_ledger", "themis_kernel"))
                .Concat(GrantSql.Statements(new TableGrant("kernel", "probe_outbox", "themis_kernel",
                    new HashSet<TablePrivilege> { TablePrivilege.Select, TablePrivilege.Insert, TablePrivilege.Update }, ["published_at", "attempts"])))
                .Concat(GrantSql.Statements(new TableGrant("kernel", "probe_idempotency", "themis_kernel",
                    new HashSet<TablePrivilege> { TablePrivilege.Select, TablePrivilege.Insert, TablePrivilege.Delete })))
                .Concat(GrantSql.Statements(TableGrant.Mutable("fixture", "probe", "themis_fixture"))));

        await _db.ExecuteAsMigratorAsync(
            $"""
            CREATE TABLE IF NOT EXISTS kernel.probe_ledger (id integer PRIMARY KEY, note text);
            CREATE TABLE IF NOT EXISTS kernel.probe_outbox (id integer PRIMARY KEY, payload text, published_at timestamptz, attempts integer NOT NULL DEFAULT 0);
            CREATE TABLE IF NOT EXISTS kernel.probe_idempotency (id integer PRIMARY KEY, expires_at timestamptz);
            CREATE TABLE IF NOT EXISTS kernel.probe_ungranted (id integer PRIMARY KEY);
            CREATE TABLE IF NOT EXISTS fixture.probe (id integer PRIMARY KEY);
            {grants}
            """,
            TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _db.ExecuteAsMigratorAsync(
            """
            DROP TABLE IF EXISTS kernel.probe_ledger, kernel.probe_outbox, kernel.probe_idempotency, kernel.probe_ungranted, fixture.probe;
            """,
            CancellationToken.None);
    }

    [Fact]
    public async Task R_DB_01_login_role_holds_no_table_privileges()
    {
        await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(_db.ConnectionStringFor("themis_app"), TestContext.Current.CancellationToken);

        await AssertRefusedAsync(connection, "INSERT INTO kernel.probe_ledger (id, note) VALUES (1, 'x')");
        await AssertRefusedAsync(connection, "SELECT count(*) FROM kernel.probe_ledger");
        await AssertRefusedAsync(connection, "CREATE TABLE kernel.rogue (id integer)");
        await AssertRefusedAsync(connection, "CREATE TABLE public.rogue (id integer)");
    }

    [Fact]
    public async Task D_41_login_role_may_assume_module_roles_but_not_the_migrator_or_reporting_roles()
    {
        await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(_db.ConnectionStringFor("themis_app"), TestContext.Current.CancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(TestContext.Current.CancellationToken);

        await ExecuteAsync(connection, "SET LOCAL ROLE themis_kernel");
        await ExecuteAsync(connection, "SET LOCAL ROLE themis_fixture");
        await AssertRefusedAsync(connection, "SET LOCAL ROLE themis_migrator", insideTransaction: true);
        await AssertRefusedAsync(connection, "SET LOCAL ROLE themis_reporting", insideTransaction: true);
    }

    [Fact]
    public async Task Module_role_writes_what_its_migration_granted_and_nothing_else()
    {
        await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(_db.ConnectionStringFor("themis_app"), TestContext.Current.CancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(TestContext.Current.CancellationToken);

        await ExecuteAsync(connection, "SET LOCAL ROLE themis_kernel");
        await ExecuteAsync(connection, "INSERT INTO kernel.probe_ledger (id, note) VALUES (1, 'kernel write')");
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT count(*) FROM kernel.probe_ledger"));
        await AssertRefusedAsync(connection, "INSERT INTO kernel.probe_ungranted (id) VALUES (1)", insideTransaction: true);
        await AssertRefusedAsync(connection, "INSERT INTO fixture.probe (id) VALUES (1)", insideTransaction: true);
    }

    [Fact]
    public async Task R_LED_06_append_only_tables_refuse_update_and_delete()
    {
        await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(_db.ConnectionStringFor("themis_app"), TestContext.Current.CancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(TestContext.Current.CancellationToken);

        await ExecuteAsync(connection, "SET LOCAL ROLE themis_kernel");
        await ExecuteAsync(connection, "INSERT INTO kernel.probe_ledger (id, note) VALUES (1, 'posted')");
        await AssertRefusedAsync(connection, "UPDATE kernel.probe_ledger SET note = 'edited' WHERE id = 1", insideTransaction: true);
        await AssertRefusedAsync(connection, "DELETE FROM kernel.probe_ledger WHERE id = 1", insideTransaction: true);
    }

    [Fact]
    public async Task Outbox_update_is_limited_to_the_delivery_columns()
    {
        await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(_db.ConnectionStringFor("themis_app"), TestContext.Current.CancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(TestContext.Current.CancellationToken);

        await ExecuteAsync(connection, "SET LOCAL ROLE themis_kernel");
        await ExecuteAsync(connection, "INSERT INTO kernel.probe_outbox (id, payload) VALUES (1, '{}')");
        await ExecuteAsync(connection, "UPDATE kernel.probe_outbox SET published_at = now(), attempts = attempts + 1 WHERE id = 1");
        await AssertRefusedAsync(connection, "UPDATE kernel.probe_outbox SET payload = 'tampered' WHERE id = 1", insideTransaction: true);
        await AssertRefusedAsync(connection, "DELETE FROM kernel.probe_outbox WHERE id = 1", insideTransaction: true);
    }

    [Fact]
    public async Task Idempotency_records_may_be_deleted_for_expiry()
    {
        await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(_db.ConnectionStringFor("themis_app"), TestContext.Current.CancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(TestContext.Current.CancellationToken);

        await ExecuteAsync(connection, "SET LOCAL ROLE themis_kernel");
        await ExecuteAsync(connection, "INSERT INTO kernel.probe_idempotency (id, expires_at) VALUES (1, now() - interval '1 day')");
        await ExecuteAsync(connection, "DELETE FROM kernel.probe_idempotency WHERE expires_at < now()");
        await AssertRefusedAsync(connection, "UPDATE kernel.probe_idempotency SET expires_at = now() WHERE id = 1", insideTransaction: true);
    }

    [Fact]
    public async Task Fixture_module_role_cannot_write_to_kernel_and_kernel_cannot_write_to_fixture()
    {
        await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(_db.ConnectionStringFor("themis_app"), TestContext.Current.CancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(TestContext.Current.CancellationToken);

        await ExecuteAsync(connection, "SET LOCAL ROLE themis_fixture");
        await ExecuteAsync(connection, "INSERT INTO fixture.probe (id) VALUES (1)");
        await AssertRefusedAsync(connection, "INSERT INTO kernel.probe_ledger (id, note) VALUES (2, 'cross-module')", insideTransaction: true);
        await AssertRefusedAsync(connection, "SELECT count(*) FROM kernel.probe_ledger", insideTransaction: true);
    }

    [Fact]
    public async Task Reporting_role_holds_nothing_in_kernel()
    {
        await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(_db.ConnectionStringFor("themis_reporting"), TestContext.Current.CancellationToken);

        await AssertRefusedAsync(connection, "SELECT count(*) FROM kernel.probe_ledger");
        await AssertRefusedAsync(connection, "CREATE TABLE reporting.rogue (id integer)");
        Assert.Equal(true, await ScalarAsync(connection, "SELECT has_schema_privilege('themis_reporting', 'reporting', 'USAGE')"));
        Assert.Equal(false, await ScalarAsync(connection, "SELECT has_schema_privilege('themis_reporting', 'kernel', 'USAGE')"));
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
    public async Task Role_attributes_match_the_brief()
    {
        await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(_db.SuperuserConnectionString, TestContext.Current.CancellationToken);

        Assert.Equal(false, await ScalarAsync(connection, "SELECT rolsuper OR rolcreaterole OR rolcreatedb FROM pg_roles WHERE rolname = 'themis_migrator'"));
        Assert.Equal(true, await ScalarAsync(connection, "SELECT rolcanlogin AND NOT rolinherit FROM pg_roles WHERE rolname = 'themis_app'"));
        Assert.Equal(false, await ScalarAsync(connection, "SELECT rolcanlogin FROM pg_roles WHERE rolname = 'themis_kernel'"));
        Assert.Equal(true, await ScalarAsync(connection, "SELECT rolcanlogin FROM pg_roles WHERE rolname = 'themis_reporting'"));
        Assert.Equal(false, await ScalarAsync(connection, "SELECT rolcanlogin FROM pg_roles WHERE rolname = 'themis_fixture'"));

        // Membership WITH INHERIT FALSE, SET TRUE (PostgreSQL 16 or later).
        Assert.Equal(true, await ScalarAsync(connection,
            """
            SELECT bool_and(NOT m.inherit_option AND m.set_option)
            FROM pg_auth_members m
            JOIN pg_roles member ON member.oid = m.member
            WHERE member.rolname = 'themis_app'
            """));
        Assert.Equal(2L, await ScalarAsync(connection,
            "SELECT count(*) FROM pg_auth_members m JOIN pg_roles member ON member.oid = m.member WHERE member.rolname = 'themis_app'"));
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

using Npgsql;

namespace Themis.Integration.Tests;

/// <summary>
/// Proves the role model of db/bootstrap/01-roles.sql on the real tables of the first migration by attempting the
/// writes it must refuse (I-14, R-DB-01, D-41, R-LED-06). The tables are created by Themis.Migrator as
/// themis_migrator and granted exactly as <see cref="Themis.Kernel.Persistence.Privileges.KernelPrivilegeMatrix"/> declares.
/// </summary>
[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Integration")]
public sealed class DatabaseRoleModelTests : IAsyncLifetime
{
    private const string InsufficientPrivilege = "42501";

    private const string InsertAudit =
        "INSERT INTO kernel.audit_event (id, command_id, occurred_at, user_id, command_name, entity_type, entity_id, changes) VALUES (gen_random_uuid(), gen_random_uuid(), now(), '00000000-0000-7000-8000-000000000001', 'PROBE.WRITE', 'Probe', gen_random_uuid(), '{}')";

    private readonly PostgresFixture _db;

    public DatabaseRoleModelTests(PostgresFixture db)
    {
        _db = db;
    }

    public async ValueTask InitializeAsync() => await _db.TruncateAsync(TestContext.Current.CancellationToken);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task R_DB_01_login_role_holds_no_table_privileges()
    {
        await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(_db.ConnectionStringFor("themis_app"), TestContext.Current.CancellationToken);

        await AssertRefusedAsync(connection, InsertAudit);
        await AssertRefusedAsync(connection, "SELECT count(*) FROM kernel.idempotency_record");
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
        await ExecuteAsync(connection, InsertAudit);
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT count(*) FROM kernel.audit_event WHERE command_name = 'PROBE.WRITE'"));
        // The migrations history table has no grants: the kernel role cannot read or write it.
        await AssertRefusedAsync(connection, "INSERT INTO kernel.__ef_migrations_history VALUES ('rogue', '0')", insideTransaction: true);
        await AssertRefusedAsync(connection, "INSERT INTO fixture.counter (id, company_id, value, version, sample) VALUES (gen_random_uuid(), gen_random_uuid(), 0, 1, 0)", insideTransaction: true);
    }

    [Fact]
    public async Task R_LED_06_append_only_tables_refuse_update_and_delete()
    {
        await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(_db.ConnectionStringFor("themis_app"), TestContext.Current.CancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(TestContext.Current.CancellationToken);

        await ExecuteAsync(connection, "SET LOCAL ROLE themis_kernel");
        await ExecuteAsync(connection, InsertAudit);
        await AssertRefusedAsync(connection, "UPDATE kernel.audit_event SET changes = '{\"edited\": true}'", insideTransaction: true);
        await AssertRefusedAsync(connection, "DELETE FROM kernel.audit_event", insideTransaction: true);
        // I-11, I-14: the ledgers of step 8 are insert-only too; the privilege check runs before any row is looked at.
        foreach (string table in new[] { "journal_entry", "journal_line", "inventory_entry" })
        {
            await AssertRefusedAsync(connection, $"UPDATE kernel.{table} SET company_id = company_id", insideTransaction: true);
            await AssertRefusedAsync(connection, $"DELETE FROM kernel.{table}", insideTransaction: true);
        }
    }

    [Fact]
    public async Task Outbox_update_is_limited_to_the_delivery_columns()
    {
        await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(_db.ConnectionStringFor("themis_app"), TestContext.Current.CancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(TestContext.Current.CancellationToken);

        await ExecuteAsync(connection, "SET LOCAL ROLE themis_kernel");
        // D-50: the envelope columns are NOT NULL; the probe fills them as the pipeline would.
        await ExecuteAsync(connection, "INSERT INTO kernel.outbox_event (id, event_type, schema_version, aggregate_type, aggregate_id, command_name, correlation_id, payload, occurred_at) VALUES (gen_random_uuid(), 'PROBE_WRITTEN', 1, 'PROBE', gen_random_uuid(), 'PROBE.WRITE', 'probe', '{}', now())");
        await ExecuteAsync(connection, "UPDATE kernel.outbox_event SET published_at = now(), attempts = attempts + 1");
        await AssertRefusedAsync(connection, "UPDATE kernel.outbox_event SET payload = '{\"tampered\": true}'", insideTransaction: true);
        await AssertRefusedAsync(connection, "DELETE FROM kernel.outbox_event", insideTransaction: true);
    }

    [Fact]
    public async Task Idempotency_records_may_be_deleted_for_expiry()
    {
        await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(_db.ConnectionStringFor("themis_app"), TestContext.Current.CancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(TestContext.Current.CancellationToken);

        await ExecuteAsync(connection, "SET LOCAL ROLE themis_kernel");
        await ExecuteAsync(connection, "INSERT INTO kernel.idempotency_record (user_id, key, command_name, request_hash, response, expires_at) VALUES ('00000000-0000-7000-8000-000000000001', 'k1', 'PROBE.WRITE', 'h', '{}', now() - interval '1 day')");
        await ExecuteAsync(connection, "DELETE FROM kernel.idempotency_record WHERE expires_at < now()");
        await AssertRefusedAsync(connection, "UPDATE kernel.idempotency_record SET expires_at = now()", insideTransaction: true);
    }

    [Fact]
    public async Task Fixture_module_role_cannot_write_to_kernel_and_kernel_cannot_write_to_fixture()
    {
        await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(_db.ConnectionStringFor("themis_app"), TestContext.Current.CancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(TestContext.Current.CancellationToken);

        await ExecuteAsync(connection, "SET LOCAL ROLE themis_fixture");
        await ExecuteAsync(connection, "INSERT INTO fixture.counter (id, company_id, value, version, sample) VALUES (gen_random_uuid(), gen_random_uuid(), 0, 1, 0)");
        await AssertRefusedAsync(connection, InsertAudit, insideTransaction: true);
        await AssertRefusedAsync(connection, "SELECT count(*) FROM kernel.audit_event", insideTransaction: true);
    }

    [Fact]
    public async Task Reporting_role_holds_nothing_in_kernel()
    {
        await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(_db.ConnectionStringFor("themis_reporting"), TestContext.Current.CancellationToken);

        await AssertRefusedAsync(connection, "SELECT count(*) FROM kernel.audit_event");
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

        // The migrator owns what it creates (brief, Database model).
        Assert.Equal("themis_migrator", await ScalarAsync(connection, "SELECT tableowner FROM pg_tables WHERE schemaname = 'kernel' AND tablename = 'audit_event'"));
    }

    [Fact]
    public async Task The_migrator_applied_the_first_migration_and_reports_nothing_pending()
    {
        Assert.Equal(0, _db.MigratorExitCode);
        await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(_db.SuperuserConnectionString, TestContext.Current.CancellationToken);

        Assert.Equal(1L, await ScalarAsync(connection, "SELECT count(*) FROM kernel.__ef_migrations_history WHERE \"MigrationId\" LIKE '%_KernelPipelineTables'"));
        Assert.Empty(await Themis.Kernel.Persistence.KernelMigrations.PendingAsync(_db.ConnectionStringFor("themis_migrator"), TestContext.Current.CancellationToken));
        Assert.Equal(0, await Themis.Migrator.Program.Main(["--connection", _db.ConnectionStringFor("themis_migrator")]));
        Assert.Equal(2, await Themis.Migrator.Program.Main(["--connection", _db.ConnectionStringFor("themis_app")]));
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

    /// <summary>Asserts SQLSTATE 42501. Inside a transaction the failed statement is rolled back to a savepoint so the test can continue.</summary>
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

using Npgsql;
using Themis.Kernel.Contracts.Documents;
using Themis.Kernel.Contracts.Identity;
using Themis.Kernel.Contracts.Security;
using Themis.Kernel.Identity;
using Themis.Kernel.Organisation;
using Themis.Kernel.Persistence.Seeding;
using Themis.Kernel.Primitives;

namespace Themis.Integration.Tests.Identity;

/// <summary>
/// Milestone 1 step 4 (approved brief): the security tables exist with their grants (I-14), the permission catalogue
/// and the identity fixture are seeded idempotently with audit events under the system user (R-AUD-01), the two
/// foreign keys to the user are validated, the catalogue in the database equals the catalogue in code in both
/// directions, and a fixture that combines a conflict pair is refused (R-SEC-02).
/// </summary>
[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Integration")]
public sealed class IdentityFixtureTests
{
    private const string InsufficientPrivilege = "42501";
    private const string ForeignKeyViolation = "23503";

    private readonly PostgresFixture _db;

    public IdentityFixtureTests(PostgresFixture db)
    {
        _db = db;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_fixture_holds_the_system_user_the_clerk_the_preparer_three_roles_and_their_assignments_in_SE01()
    {
        await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(_db.SuperuserConnectionString, Ct);

        Assert.Equal(3L, await ScalarAsync(connection, "SELECT count(*) FROM kernel.user_account"));
        Assert.Equal("Active", await ScalarAsync(connection, "SELECT status FROM kernel.user_account WHERE id = @id", ("id", WellKnownUsers.System.Value)));
        Assert.Equal(true, await ScalarAsync(connection, "SELECT is_service_account FROM kernel.user_account WHERE id = @id", ("id", WellKnownUsers.System.Value)));
        Assert.Equal(WellKnownUsers.SystemSubject, await ScalarAsync(connection, "SELECT external_subject FROM kernel.user_account WHERE id = @id", ("id", WellKnownUsers.System.Value)));
        Assert.Equal(Milestone1IdentityFixture.ClerkSubject, await ScalarAsync(connection, "SELECT external_subject FROM kernel.user_account WHERE id = @id", ("id", Milestone1IdentityFixture.ClerkId)));
        Assert.Equal(Milestone1IdentityFixture.PreparerSubject, await ScalarAsync(connection, "SELECT external_subject FROM kernel.user_account WHERE id = @id", ("id", Milestone1IdentityFixture.PreparerId)));

        Assert.Equal(3L, await ScalarAsync(connection, "SELECT count(*) FROM kernel.role"));
        Assert.Equal(9L, await ScalarAsync(connection, "SELECT count(*) FROM kernel.role_permission"));
        Assert.Equal(4L, await ScalarAsync(connection, "SELECT count(*) FROM kernel.role_assignment"));
        Assert.Equal(4L, await ScalarAsync(connection, "SELECT count(*) FROM kernel.role_assignment WHERE company_id = @company AND site_id IS NULL AND valid_to IS NULL", ("company", Milestone1OrganisationFixture.CompanyId)));
        Assert.Equal(new DateOnly(2026, 1, 1), await ScalarAsync(connection, "SELECT min(valid_from) FROM kernel.role_assignment"));
        Assert.Equal(WellKnownUsers.System.Value, await ScalarAsync(connection, "SELECT DISTINCT assigned_by FROM kernel.role_assignment"));

        // The clerk may post and create items, the preparer may do neither (step 4 brief; step 5 brief, acceptance 6).
        const string permissionsOf = "SELECT count(*) FROM kernel.role_assignment a JOIN kernel.role_permission rp ON rp.role_id = a.role_id WHERE a.user_id = @user AND rp.permission_code = @permission";
        Assert.Equal(1L, await ScalarAsync(connection, permissionsOf, ("user", Milestone1IdentityFixture.ClerkId), ("permission", "INVENTORY_ADJUSTMENT.POST")));
        Assert.Equal(0L, await ScalarAsync(connection, permissionsOf, ("user", Milestone1IdentityFixture.PreparerId), ("permission", "INVENTORY_ADJUSTMENT.POST")));
        Assert.Equal(1L, await ScalarAsync(connection, permissionsOf, ("user", Milestone1IdentityFixture.ClerkId), ("permission", "ITEM.CREATE")));
        Assert.Equal(0L, await ScalarAsync(connection, permissionsOf, ("user", Milestone1IdentityFixture.PreparerId), ("permission", "ITEM.CREATE")));
        Assert.Equal(1L, await ScalarAsync(connection, permissionsOf, ("user", Milestone1IdentityFixture.PreparerId), ("permission", "ITEM.READ")));
    }

    [Fact]
    public async Task The_permission_catalogue_in_the_database_equals_the_catalogue_in_code_in_both_directions_with_POST_Sensitive()
    {
        await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(_db.SuperuserConnectionString, Ct);
        List<(string Code, string Risk)> rows = [];
        await using (NpgsqlCommand command = new("SELECT code, risk_level FROM kernel.permission ORDER BY code", connection))
        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(Ct))
        {
            while (await reader.ReadAsync(Ct))
            {
                rows.Add((reader.GetString(0), reader.GetString(1)));
            }
        }

        Assert.Equal(
            PermissionCatalog.Definition.Permissions.Select(p => (p.Code, p.RiskLevel.ToString())).OrderBy(p => p.Code, StringComparer.Ordinal),
            rows);
        Assert.Contains(("INVENTORY_ADJUSTMENT.POST", "Sensitive"), rows);

        List<(string A, string B)> pairs = [];
        await using (NpgsqlCommand command = new("SELECT permission_a, permission_b FROM kernel.sod_conflict ORDER BY permission_a, permission_b", connection))
        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(Ct))
        {
            while (await reader.ReadAsync(Ct))
            {
                pairs.Add((reader.GetString(0), reader.GetString(1)));
            }
        }

        Assert.Equal(PermissionCatalog.Definition.Conflicts.Select(c => (c.PermissionA, c.PermissionB)).Order(), pairs);
    }

    [Fact]
    public async Task R_AUD_01_every_seeded_identity_row_has_an_audit_event_under_the_system_user_and_seeding_again_changes_nothing()
    {
        await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(_db.SuperuserConnectionString, Ct);
        const string fixtureAudit = "FROM kernel.audit_event WHERE command_name = 'SYSTEM.SEED_FIXTURE' AND entity_type IN ('User', 'Role', 'RolePermission', 'RoleAssignment')";
        const string catalogueAudit = "FROM kernel.audit_event WHERE command_name = 'SYSTEM.SEED_CATALOGUE'";

        // 2 users, 3 roles, 9 role permissions, 4 assignments.
        Assert.Equal(2L + 3L + 9L + 4L, await ScalarAsync(connection, $"SELECT count(*) {fixtureAudit}"));
        Assert.Equal(18L, await ScalarAsync(connection, $"SELECT count(DISTINCT entity_id) {fixtureAudit}"));
        // SYSTEM.SEED_CATALOGUE also covers the document type registry since step 7 (D-40): one event per type and per transition.
        long registryRows = DocumentTypes.All.Count + DocumentTypes.All.Sum(type => type.Transitions.Count);
        Assert.Equal(PermissionCatalog.Definition.Permissions.Count + PermissionCatalog.Definition.Conflicts.Count + registryRows, await ScalarAsync(connection, $"SELECT count(*) {catalogueAudit}"));
        Assert.Equal(WellKnownUsers.System.Value, await ScalarAsync(connection, $"SELECT DISTINCT user_id {fixtureAudit}"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT count(*) FROM kernel.audit_event WHERE command_name = 'SYSTEM.MIGRATE' AND entity_type = 'User'"));
        Assert.Equal(Milestone1IdentityFixture.ClerkSubject, await ScalarAsync(connection, $"SELECT changes->'externalSubject'->>'after' {fixtureAudit} AND entity_id = @id", ("id", Milestone1IdentityFixture.ClerkId)));

        Assert.Equal(0, await Themis.Migrator.Program.Main(["--connection", _db.ConnectionStringFor("themis_migrator"), "--fixture", "milestone-1"]));

        Assert.Equal(18L, await ScalarAsync(connection, $"SELECT count(*) {fixtureAudit}"));
        Assert.Equal(3L, await ScalarAsync(connection, "SELECT count(*) FROM kernel.user_account"));
        Assert.Equal((long)PermissionCatalog.Definition.Permissions.Count, await ScalarAsync(connection, "SELECT count(*) FROM kernel.permission"));
    }

    [Fact]
    public async Task The_foreign_keys_from_audit_and_idempotency_records_to_the_user_are_validated_and_enforced()
    {
        await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(_db.SuperuserConnectionString, Ct);

        Assert.Equal(true, await ScalarAsync(connection, "SELECT convalidated FROM pg_constraint WHERE conname = 'fk_audit_event_user'"));
        Assert.Equal(true, await ScalarAsync(connection, "SELECT convalidated FROM pg_constraint WHERE conname = 'fk_idempotency_record_user'"));
        Assert.Equal(0L, await ScalarAsync(connection, "SELECT count(*) FROM kernel.audit_event e LEFT JOIN kernel.user_account u ON u.id = e.user_id WHERE u.id IS NULL"));

        await using NpgsqlConnection kernel = await PostgresFixture.OpenAsync(_db.ConnectionStringFor("themis_app"), Ct);
        PostgresException orphan = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(kernel,
            "SET LOCAL ROLE themis_kernel; INSERT INTO kernel.audit_event (id, occurred_at, user_id, command_name, entity_type, entity_id, changes) VALUES (gen_random_uuid(), now(), gen_random_uuid(), 'TEST.ORPHAN', 'Test', gen_random_uuid(), '{}')"));
        Assert.Equal(ForeignKeyViolation, orphan.SqlState);
    }

    [Fact]
    public async Task I_14_the_kernel_role_reads_inserts_and_updates_the_security_tables_and_never_deletes()
    {
        await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(_db.ConnectionStringFor("themis_app"), Ct);
        foreach (string table in new[] { "user_account", "permission", "role", "role_permission", "role_assignment", "sod_conflict" })
        {
            Assert.NotNull(await ScalarAsync(connection, $"SET LOCAL ROLE themis_kernel; SELECT count(*) FROM kernel.{table}"));
            PostgresException refused = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connection, $"SET LOCAL ROLE themis_kernel; DELETE FROM kernel.{table} WHERE false"));
            Assert.Equal(InsufficientPrivilege, refused.SqlState);
        }

        // UPDATE is granted (a user is Disabled, an assignment gets its valid_to), and refused to the fixture role.
        Assert.Equal(0, await ExecuteAsync(connection, "SET LOCAL ROLE themis_kernel; UPDATE kernel.user_account SET status = status WHERE false"));
        PostgresException fixture = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connection, "SET LOCAL ROLE themis_fixture; SELECT count(*) FROM kernel.user_account"));
        Assert.Equal(InsufficientPrivilege, fixture.SqlState);
    }

    [Fact]
    public async Task R_SEC_02_a_fixture_whose_role_combines_a_conflict_pair_is_refused_and_nothing_is_written()
    {
        await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(_db.SuperuserConnectionString, Ct);
        long rolesBefore = (long)(await ScalarAsync(connection, "SELECT count(*) FROM kernel.role"))!;
        RoleDefinition conflicting = new(Guid.NewGuid(), Code.From("CONFLICTING"), "Reopens and posts", ["FISCAL_PERIOD.REOPEN", "MANUAL_JOURNAL.POST"]);
        IdentityFixtureDefinition fixture = new([], [conflicting], []);

        Result<int> refused = await IdentitySeeder.ApplyFixtureAsync(
            _db.ConnectionStringFor("themis_migrator"), fixture,
            definition => SegregationOfDuties.CheckFixture(definition, PermissionCatalog.Definition.Conflicts),
            TimeProvider.System.GetUtcNow(), WellKnownUsers.System, Ct);

        Assert.True(refused.IsFailure);
        Assert.Equal("R-SEC-02", refused.Primary!.RuleId);
        Assert.Equal(rolesBefore, await ScalarAsync(connection, "SELECT count(*) FROM kernel.role"));
    }

    [Fact]
    public async Task The_database_keeps_the_security_model_consistent()
    {
        await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(_db.ConnectionStringFor("themis_migrator"), Ct);
        string system = WellKnownUsers.System.Value.ToString();

        // A role permission must name a catalogue permission; an assignment's site must belong to its company; valid_to follows valid_from; a pair is stored in order.
        PostgresException unknownPermission = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connection,
            $"INSERT INTO kernel.role_permission (id, role_id, permission_code, created_at, created_by, updated_at, updated_by) VALUES (gen_random_uuid(), '{Milestone1IdentityFixture.PosterRoleId}', 'NO.SUCH', now(), '{system}', now(), '{system}')"));
        Assert.Equal(ForeignKeyViolation, unknownPermission.SqlState);
        PostgresException foreignSite = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connection,
            $"INSERT INTO kernel.role_assignment (id, user_id, role_id, company_id, site_id, valid_from, assigned_by, created_at, created_by, updated_at, updated_by) VALUES (gen_random_uuid(), '{Milestone1IdentityFixture.ClerkId}', '{Milestone1IdentityFixture.PosterRoleId}', '{Milestone1OrganisationFixture.CompanyId}', gen_random_uuid(), '2026-01-01', '{system}', now(), '{system}', now(), '{system}')"));
        Assert.Equal(ForeignKeyViolation, foreignSite.SqlState);
        PostgresException backwards = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connection,
            $"INSERT INTO kernel.role_assignment (id, user_id, role_id, company_id, valid_from, valid_to, assigned_by, created_at, created_by, updated_at, updated_by) VALUES (gen_random_uuid(), '{Milestone1IdentityFixture.ClerkId}', '{Milestone1IdentityFixture.PosterRoleId}', '{Milestone1OrganisationFixture.CompanyId}', '2026-02-01', '2026-01-01', '{system}', now(), '{system}', now(), '{system}')"));
        Assert.Equal("23514", backwards.SqlState);
        PostgresException unordered = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connection,
            $"INSERT INTO kernel.sod_conflict (id, permission_a, permission_b, created_at, created_by, updated_at, updated_by) VALUES (gen_random_uuid(), 'B.X', 'A.Y', now(), '{system}', now(), '{system}')"));
        Assert.Equal("23514", unordered.SqlState);
    }

    private static async Task<object?> ScalarAsync(NpgsqlConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        await using NpgsqlCommand command = new(sql, connection);
        foreach ((string name, object value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        object? result = await command.ExecuteScalarAsync(Ct);
        return result is DBNull ? null : result;
    }

    private static async Task<int> ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(Ct);
        await using NpgsqlCommand command = new(sql, connection, transaction);
        int rows = await command.ExecuteNonQueryAsync(Ct);
        await transaction.RollbackAsync(Ct);
        return rows;
    }
}

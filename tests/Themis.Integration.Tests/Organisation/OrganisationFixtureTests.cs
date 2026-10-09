using Npgsql;
using Themis.Kernel.Organisation;

namespace Themis.Integration.Tests.Organisation;

/// <summary>
/// Milestone 1 step 3 (brief): the organisation fixture exists after Themis.Migrator ran with the fixture flag, is
/// idempotent, and the database enforces the Phase 0 Specification's constraints on it: the company scope
/// convention, enum values, code uniqueness per company (R-MD-05), no deletion of master data (R-MD-03).
/// </summary>
[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Integration")]
public sealed class OrganisationFixtureTests
{
    private const string InsufficientPrivilege = "42501";
    private const string ForeignKeyViolation = "23503";
    private const string UniqueViolation = "23505";
    private const string CheckViolation = "23514";

    private readonly PostgresFixture _db;

    public OrganisationFixtureTests(PostgresFixture db)
    {
        _db = db;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_fixture_holds_one_installation_party_chart_company_site_warehouse_and_fiscal_year_2026()
    {
        await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(_db.SuperuserConnectionString, Ct);

        Assert.Equal(1L, await ScalarAsync(connection, "SELECT count(*) FROM kernel.installation"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT count(*) FROM kernel.party"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT count(*) FROM kernel.chart_of_accounts"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT count(*) FROM kernel.company"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT count(*) FROM kernel.site"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT count(*) FROM kernel.warehouse"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT count(*) FROM kernel.fiscal_year"));
        Assert.Equal(12L, await ScalarAsync(connection, "SELECT count(*) FROM kernel.fiscal_period"));

        // The company: SEK, Europe/Stockholm, tax rounding level Line as a test value until D-10.
        Assert.Equal("SEK", await ScalarAsync(connection, "SELECT base_currency FROM kernel.company"));
        Assert.Equal("Europe/Stockholm", await ScalarAsync(connection, "SELECT time_zone FROM kernel.company"));
        Assert.Equal("Line", await ScalarAsync(connection, "SELECT tax_rounding_level FROM kernel.company"));
        Assert.Equal("HalfAwayFromZero", await ScalarAsync(connection, "SELECT rounding_mode FROM kernel.company"));
        Assert.Equal(Milestone1OrganisationFixture.CompanyId, await ScalarAsync(connection, "SELECT id FROM kernel.company"));

        // The company's own party row: name, organisation number, country (R-MD-01 duplicate key derived).
        Assert.Equal("Themis Demo AB", await ScalarAsync(connection, "SELECT name FROM kernel.party"));
        Assert.Equal(Milestone1OrganisationFixture.OrganisationNumber, await ScalarAsync(connection, "SELECT organisation_number FROM kernel.party"));
        Assert.Equal("SE", await ScalarAsync(connection, "SELECT country_code FROM kernel.party"));
        Assert.Equal("5566778899|SE556677889901|THEMISDEMOAB|", await ScalarAsync(connection, "SELECT duplicate_key FROM kernel.party"));
        Assert.Equal(Milestone1OrganisationFixture.CompanyPartyId, await ScalarAsync(connection, "SELECT party_id FROM kernel.company"));

        // Period 1 Closed, the rest Open; periods cover 2026 exactly.
        Assert.Equal("Closed", await ScalarAsync(connection, "SELECT status FROM kernel.fiscal_period WHERE period_number = 1"));
        Assert.Equal(11L, await ScalarAsync(connection, "SELECT count(*) FROM kernel.fiscal_period WHERE status = 'Open' AND period_number > 1"));
        Assert.Equal(new DateOnly(2026, 1, 1), await ScalarAsync(connection, "SELECT min(start_date) FROM kernel.fiscal_period"));
        Assert.Equal(new DateOnly(2026, 12, 31), await ScalarAsync(connection, "SELECT max(end_date) FROM kernel.fiscal_period"));
        Assert.Equal(0L, await ScalarAsync(connection,
            "SELECT count(*) FROM kernel.fiscal_period p JOIN kernel.fiscal_period n ON n.period_number = p.period_number + 1 WHERE n.start_date <> p.end_date + 1"));

        // Company scope: the derived company is stored on the warehouse and the periods.
        Assert.Equal(Milestone1OrganisationFixture.CompanyId, await ScalarAsync(connection, "SELECT company_id FROM kernel.warehouse"));
        Assert.Equal(12L, await ScalarAsync(connection, "SELECT count(*) FROM kernel.fiscal_period WHERE company_id = @company", ("company", Milestone1OrganisationFixture.CompanyId)));
        Assert.Equal(true, await ScalarAsync(connection, "SELECT is_default FROM kernel.warehouse"));
    }

    [Fact]
    public async Task Seeding_again_inserts_nothing_and_changes_nothing()
    {
        await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(_db.SuperuserConnectionString, Ct);
        object? before = await ScalarAsync(connection, "SELECT max(created_at) FROM kernel.fiscal_period");

        Assert.Equal(0, await Themis.Migrator.Program.Main(["--connection", _db.ConnectionStringFor("themis_migrator"), "--fixture", "milestone-1"]));

        Assert.Equal(12L, await ScalarAsync(connection, "SELECT count(*) FROM kernel.fiscal_period"));
        Assert.Equal(before, await ScalarAsync(connection, "SELECT max(created_at) FROM kernel.fiscal_period"));
    }

    [Fact]
    public async Task An_unknown_fixture_name_is_a_usage_error()
    {
        Assert.Equal(1, await Themis.Migrator.Program.Main(["--connection", _db.ConnectionStringFor("themis_migrator"), "--fixture", "milestone-9"]));
    }

    [Fact]
    public async Task Company_scope_a_warehouse_cannot_store_another_company_than_its_site()
    {
        await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(_db.ConnectionStringFor("themis_migrator"), Ct);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(Ct);

        await AssertFailsAsync(connection, ForeignKeyViolation,
            $"""
            INSERT INTO kernel.warehouse (id, site_id, company_id, code, name, is_default, status, created_at, created_by, updated_at, updated_by)
            VALUES (gen_random_uuid(), '{Milestone1OrganisationFixture.SiteId}', gen_random_uuid(), 'OTHER', 'Wrong company', false, 'Active', now(), gen_random_uuid(), now(), gen_random_uuid())
            """);
        await AssertFailsAsync(connection, ForeignKeyViolation,
            $"""
            INSERT INTO kernel.fiscal_period (id, fiscal_year_id, company_id, period_number, start_date, end_date, status, created_at, created_by, updated_at, updated_by)
            VALUES (gen_random_uuid(), '{Milestone1OrganisationFixture.FiscalYearId}', gen_random_uuid(), 13, '2027-01-01', '2027-01-31', 'Open', now(), gen_random_uuid(), now(), gen_random_uuid())
            """);
        await transaction.RollbackAsync(Ct);
    }

    [Fact]
    public async Task R_MD_05_codes_are_unique_within_their_scope_and_statuses_within_their_enum()
    {
        await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(_db.ConnectionStringFor("themis_migrator"), Ct);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(Ct);

        string siteRow = $"(gen_random_uuid(), '{Milestone1OrganisationFixture.CompanyId}', 'MAIN', 'Second main', now(), gen_random_uuid(), now(), gen_random_uuid())";
        await AssertFailsAsync(connection, UniqueViolation, $"INSERT INTO kernel.site (id, company_id, code, name, created_at, created_by, updated_at, updated_by) VALUES {siteRow}");
        await AssertFailsAsync(connection, UniqueViolation, $"INSERT INTO kernel.fiscal_period (id, fiscal_year_id, company_id, period_number, start_date, end_date, status, created_at, created_by, updated_at, updated_by) VALUES (gen_random_uuid(), '{Milestone1OrganisationFixture.FiscalYearId}', '{Milestone1OrganisationFixture.CompanyId}', 1, '2026-01-01', '2026-01-31', 'Open', now(), gen_random_uuid(), now(), gen_random_uuid())");
        await AssertFailsAsync(connection, UniqueViolation, $"INSERT INTO kernel.warehouse (id, site_id, company_id, code, name, is_default, status, created_at, created_by, updated_at, updated_by) VALUES (gen_random_uuid(), '{Milestone1OrganisationFixture.SiteId}', '{Milestone1OrganisationFixture.CompanyId}', 'SECOND', 'Second default', true, 'Active', now(), gen_random_uuid(), now(), gen_random_uuid())");
        await AssertFailsAsync(connection, CheckViolation, "UPDATE kernel.fiscal_period SET status = 'Bogus' WHERE period_number = 2");
        await AssertFailsAsync(connection, CheckViolation, "UPDATE kernel.company SET fiscal_year_start_month = 13");
        await transaction.RollbackAsync(Ct);
    }

    [Fact]
    public async Task R_MD_03_the_kernel_role_updates_master_data_but_never_deletes_it_and_the_fixture_role_reads_none_of_it()
    {
        await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(_db.ConnectionStringFor("themis_app"), Ct);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(Ct);

        await ExecuteAsync(connection, "SET LOCAL ROLE themis_kernel");
        await ExecuteAsync(connection, "UPDATE kernel.warehouse SET name = 'Renamed', version = version + 1 WHERE code = 'MAIN'");
        Assert.Equal(2L, await ScalarAsync(connection, "SELECT version FROM kernel.warehouse WHERE code = 'MAIN'"));
        await AssertFailsAsync(connection, InsufficientPrivilege, "DELETE FROM kernel.warehouse");
        await AssertFailsAsync(connection, InsufficientPrivilege, "DELETE FROM kernel.fiscal_period");

        await ExecuteAsync(connection, "SET LOCAL ROLE themis_fixture");
        await AssertFailsAsync(connection, InsufficientPrivilege, "SELECT count(*) FROM kernel.company");
        await transaction.RollbackAsync(Ct);
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using NpgsqlCommand command = new(sql, connection);
        await command.ExecuteNonQueryAsync(Ct);
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

    /// <summary>Asserts the SQLSTATE inside a savepoint so the transaction stays usable.</summary>
    private static async Task AssertFailsAsync(NpgsqlConnection connection, string sqlState, string sql)
    {
        await ExecuteAsync(connection, "SAVEPOINT probe");
        PostgresException exception = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connection, sql));
        Assert.Equal(sqlState, exception.SqlState);
        await ExecuteAsync(connection, "ROLLBACK TO SAVEPOINT probe");
    }
}

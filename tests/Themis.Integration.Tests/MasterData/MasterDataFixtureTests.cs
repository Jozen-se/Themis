using Npgsql;
using Themis.Kernel.Contracts.Security;
using Themis.Kernel.MasterData;
using Themis.Kernel.Organisation;

namespace Themis.Integration.Tests.MasterData;

/// <summary>
/// Milestone 1 step 5 (brief, acceptance 1, 2 and 7): the reference units and currencies exist after a plain
/// migration with an audit event each under the system user, the posting group TRADE_GOODS comes only with the
/// fixture, the references by code are validated foreign keys, and the database enforces uniqueness (R-MD-05), the
/// enum values and no deletion of master data (R-MD-03).
/// </summary>
[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Integration")]
public sealed class MasterDataFixtureTests
{
    private const string InsufficientPrivilege = "42501";
    private const string ForeignKeyViolation = "23503";
    private const string UniqueViolation = "23505";
    private const string CheckViolation = "23514";
    private const string MigrateAudit = "FROM kernel.audit_event WHERE command_name = 'SYSTEM.MIGRATE' AND entity_type IN ('UnitOfMeasure', 'Currency')";

    private static readonly string[] MasterDataTables = ["unit_of_measure", "currency", "item_posting_group", "item", "item_company"];

    private readonly PostgresFixture _db;

    public MasterDataFixtureTests(PostgresFixture db)
    {
        _db = db;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_reference_units_and_currencies_are_seeded_by_the_migration_with_one_SYSTEM_MIGRATE_audit_event_each()
    {
        await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(_db.SuperuserConnectionString, Ct);

        Assert.Equal(3L, await ScalarAsync(connection, "SELECT count(*) FROM kernel.unit_of_measure"));
        Assert.Equal(2L, await ScalarAsync(connection, "SELECT count(*) FROM kernel.currency"));
        Assert.Equal("H87,KGM,MTR", await ScalarAsync(connection, "SELECT string_agg(code, ',' ORDER BY code) FROM kernel.unit_of_measure"));
        Assert.Equal("EUR,SEK", await ScalarAsync(connection, "SELECT string_agg(code, ',' ORDER BY code) FROM kernel.currency"));
        Assert.Equal(0, await ScalarAsync(connection, "SELECT decimals FROM kernel.unit_of_measure WHERE code = 'H87'"));
        Assert.Equal("Count", await ScalarAsync(connection, "SELECT dimension FROM kernel.unit_of_measure WHERE code = 'H87'"));
        Assert.Equal(3, await ScalarAsync(connection, "SELECT decimals FROM kernel.unit_of_measure WHERE code = 'KGM'"));
        Assert.Equal("Mass", await ScalarAsync(connection, "SELECT dimension FROM kernel.unit_of_measure WHERE code = 'KGM'"));
        Assert.Equal("Length", await ScalarAsync(connection, "SELECT dimension FROM kernel.unit_of_measure WHERE code = 'MTR'"));
        Assert.Equal(2, await ScalarAsync(connection, "SELECT minor_units FROM kernel.currency WHERE code = 'SEK'"));
        Assert.Equal(2, await ScalarAsync(connection, "SELECT minor_units FROM kernel.currency WHERE code = 'EUR'"));
        Assert.Equal(Guid.Parse(Themis.Kernel.Persistence.Migrations.MasterDataTables.ReferenceRowId("unit_of_measure", "H87")), await ScalarAsync(connection, "SELECT id FROM kernel.unit_of_measure WHERE code = 'H87'"));
        Assert.Equal(Guid.Parse(Themis.Kernel.Persistence.Migrations.MasterDataTables.ReferenceRowId("currency", "SEK")), await ScalarAsync(connection, "SELECT id FROM kernel.currency WHERE code = 'SEK'"));
        Assert.Equal(WellKnownUsers.System.Value, await ScalarAsync(connection, "SELECT DISTINCT created_by FROM kernel.unit_of_measure"));
        Assert.Equal(WellKnownUsers.System.Value, await ScalarAsync(connection, "SELECT DISTINCT created_by FROM kernel.currency"));

        // R-AUD-01: one audit event per reference row, under the system user, with the created values.
        Assert.Equal(5L, await ScalarAsync(connection, $"SELECT count(*) {MigrateAudit}"));
        Assert.Equal(5L, await ScalarAsync(connection, $"SELECT count(DISTINCT entity_id) {MigrateAudit}"));
        Assert.Equal(WellKnownUsers.System.Value, await ScalarAsync(connection, $"SELECT DISTINCT user_id {MigrateAudit}"));
        Assert.Equal("KGM", await ScalarAsync(connection, $"SELECT changes->'code'->>'after' {MigrateAudit} AND entity_id = (SELECT id FROM kernel.unit_of_measure WHERE code = 'KGM')"));
        Assert.Equal("3", await ScalarAsync(connection, $"SELECT changes->'decimals'->>'after' {MigrateAudit} AND entity_id = (SELECT id FROM kernel.unit_of_measure WHERE code = 'KGM')"));
        Assert.Equal("2", await ScalarAsync(connection, $"SELECT changes->'minorUnits'->>'after' {MigrateAudit} AND entity_id = (SELECT id FROM kernel.currency WHERE code = 'EUR')"));
        Assert.Null(await ScalarAsync(connection, $"SELECT changes->'code'->>'before' {MigrateAudit} AND entity_id = (SELECT id FROM kernel.currency WHERE code = 'EUR')"));
    }

    [Fact]
    public async Task The_fixture_seeds_TRADE_GOODS_with_one_SYSTEM_SEED_FIXTURE_audit_event_and_a_second_run_inserts_nothing()
    {
        await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(_db.SuperuserConnectionString, Ct);
        const string seedAudit = "FROM kernel.audit_event WHERE command_name = 'SYSTEM.SEED_FIXTURE' AND entity_type = 'ItemPostingGroup'";

        Assert.Equal(1L, await ScalarAsync(connection, "SELECT count(*) FROM kernel.item_posting_group"));
        Assert.Equal(Milestone1MasterDataFixture.TradeGoodsCode, await ScalarAsync(connection, "SELECT code FROM kernel.item_posting_group"));
        Assert.Equal(Milestone1MasterDataFixture.TradeGoodsId, await ScalarAsync(connection, "SELECT id FROM kernel.item_posting_group"));
        Assert.Equal(1L, await ScalarAsync(connection, $"SELECT count(*) {seedAudit}"));
        Assert.Equal(Milestone1MasterDataFixture.TradeGoodsId, await ScalarAsync(connection, $"SELECT entity_id {seedAudit}"));
        Assert.Equal(WellKnownUsers.System.Value, await ScalarAsync(connection, $"SELECT user_id {seedAudit}"));
        Assert.Equal("TRADE_GOODS", await ScalarAsync(connection, $"SELECT changes->'code'->>'after' {seedAudit}"));

        Assert.Equal(0, await Themis.Migrator.Program.Main(["--connection", _db.ConnectionStringFor("themis_migrator"), "--fixture", "milestone-1"]));

        Assert.Equal(1L, await ScalarAsync(connection, "SELECT count(*) FROM kernel.item_posting_group"));
        Assert.Equal(1L, await ScalarAsync(connection, $"SELECT count(*) {seedAudit}"));
        Assert.Equal(5L, await ScalarAsync(connection, $"SELECT count(*) {MigrateAudit}"));
    }

    [Fact]
    public async Task A_plain_migration_without_the_fixture_holds_the_reference_rows_and_no_posting_group_company_or_item()
    {
        string fresh = await _db.CreateEmptyDatabaseAsync(Ct);
        try
        {
            string migrator = new NpgsqlConnectionStringBuilder(fresh) { Username = "themis_migrator", Password = PostgresFixture.TestRolePassword }.ConnectionString;
            Assert.Equal(0, await Themis.Migrator.Program.Main(["--connection", migrator]));

            await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(fresh, Ct);
            Assert.Equal(3L, await ScalarAsync(connection, "SELECT count(*) FROM kernel.unit_of_measure"));
            Assert.Equal(2L, await ScalarAsync(connection, "SELECT count(*) FROM kernel.currency"));
            Assert.Equal(5L, await ScalarAsync(connection, $"SELECT count(*) {MigrateAudit}"));
            Assert.Equal(0L, await ScalarAsync(connection, "SELECT count(*) FROM kernel.item_posting_group"));
            Assert.Equal(0L, await ScalarAsync(connection, "SELECT count(*) FROM kernel.company"));
            Assert.Equal(0L, await ScalarAsync(connection, "SELECT count(*) FROM kernel.item"));
            Assert.Equal(0L, await ScalarAsync(connection, "SELECT count(*) FROM kernel.audit_event WHERE command_name = 'SYSTEM.SEED_FIXTURE'"));

            // A second plain run applies nothing and seeds nothing twice.
            Assert.Equal(0, await Themis.Migrator.Program.Main(["--connection", migrator]));
            Assert.Equal(3L, await ScalarAsync(connection, "SELECT count(*) FROM kernel.unit_of_measure"));
            Assert.Equal(5L, await ScalarAsync(connection, $"SELECT count(*) {MigrateAudit}"));
        }
        finally
        {
            await _db.DropDatabaseAsync(fresh, CancellationToken.None);
        }
    }

    [Fact]
    public async Task The_references_by_code_are_validated_foreign_keys_and_the_company_scope_holds_for_the_default_warehouse()
    {
        await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(_db.ConnectionStringFor("themis_migrator"), Ct);

        Assert.Equal(true, await ScalarAsync(connection, "SELECT convalidated FROM pg_constraint WHERE conname = 'fk_item_base_unit'"));
        Assert.Equal(true, await ScalarAsync(connection, "SELECT convalidated FROM pg_constraint WHERE conname = 'fk_company_base_currency'"));
        Assert.Equal(true, await ScalarAsync(connection, "SELECT convalidated FROM pg_constraint WHERE conname = 'fk_item_company_item_posting_group'"));
        Assert.Equal(true, await ScalarAsync(connection, "SELECT convalidated FROM pg_constraint WHERE conname = 'fk_item_company_default_warehouse_company'"));

        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(Ct);
        string audit = $"now(), '{WellKnownUsers.System.Value}', now(), '{WellKnownUsers.System.Value}'";
        await AssertFailsAsync(connection, ForeignKeyViolation, "UPDATE kernel.company SET base_currency = 'XXX' WHERE code = 'SE01'");
        await AssertFailsAsync(connection, ForeignKeyViolation, $"INSERT INTO kernel.item (id, item_number, description, item_type, base_unit, lot_tracking, created_at, created_by, updated_at, updated_by) VALUES (gen_random_uuid(), 'FK-UNIT', 'Unknown unit', 'Stocked', 'XXX', 'None', {audit})");

        Guid itemId = Guid.NewGuid();
        await ExecuteAsync(connection, $"INSERT INTO kernel.item (id, item_number, description, item_type, base_unit, lot_tracking, created_at, created_by, updated_at, updated_by) VALUES ('{itemId}', 'FK-ITEM', 'Probe', 'Stocked', 'H87', 'None', {audit})");
        string companyRow = $"INSERT INTO kernel.item_company (id, item_id, company_id, status, item_posting_group, tax_class, costing_method, sales_allowed, purchase_allowed, default_warehouse_id, created_at, created_by, updated_at, updated_by) VALUES";
        await AssertFailsAsync(connection, ForeignKeyViolation, $"{companyRow} (gen_random_uuid(), '{itemId}', '{Milestone1OrganisationFixture.CompanyId}', 'Active', 'NOPE', 'Standard', 'MovingAverage', true, true, NULL, {audit})");
        await AssertFailsAsync(connection, ForeignKeyViolation, $"{companyRow} (gen_random_uuid(), '{itemId}', gen_random_uuid(), 'Active', 'TRADE_GOODS', 'Standard', 'MovingAverage', true, true, '{Milestone1OrganisationFixture.WarehouseId}', {audit})");
        await ExecuteAsync(connection, $"{companyRow} (gen_random_uuid(), '{itemId}', '{Milestone1OrganisationFixture.CompanyId}', 'Active', 'TRADE_GOODS', 'Standard', 'MovingAverage', true, true, '{Milestone1OrganisationFixture.WarehouseId}', {audit})");

        // R-MD-05: one item number per installation, one row per item and company; enum values are checked.
        await AssertFailsAsync(connection, UniqueViolation, $"INSERT INTO kernel.item (id, item_number, description, item_type, base_unit, lot_tracking, created_at, created_by, updated_at, updated_by) VALUES (gen_random_uuid(), 'FK-ITEM', 'Duplicate', 'Stocked', 'H87', 'None', {audit})");
        await AssertFailsAsync(connection, UniqueViolation, $"{companyRow} (gen_random_uuid(), '{itemId}', '{Milestone1OrganisationFixture.CompanyId}', 'Active', 'TRADE_GOODS', 'Standard', 'MovingAverage', true, true, NULL, {audit})");
        await AssertFailsAsync(connection, UniqueViolation, $"INSERT INTO kernel.unit_of_measure (id, code, name, dimension, decimals, created_at, created_by, updated_at, updated_by) VALUES (gen_random_uuid(), 'H87', 'Piece again', 'Count', 0, {audit})");
        await AssertFailsAsync(connection, CheckViolation, $"UPDATE kernel.item SET item_type = 'Bogus' WHERE id = '{itemId}'");
        await AssertFailsAsync(connection, CheckViolation, $"UPDATE kernel.item_company SET costing_method = 'Fifo' WHERE item_id = '{itemId}'");
        await AssertFailsAsync(connection, CheckViolation, $"UPDATE kernel.item SET net_weight_kg = -1 WHERE id = '{itemId}'");
        await AssertFailsAsync(connection, CheckViolation, "UPDATE kernel.unit_of_measure SET decimals = 7 WHERE code = 'H87'");
        await transaction.RollbackAsync(Ct);
    }

    [Fact]
    public async Task R_MD_03_the_kernel_role_updates_master_data_but_never_deletes_it()
    {
        await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(_db.ConnectionStringFor("themis_app"), Ct);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(Ct);

        await ExecuteAsync(connection, "SET LOCAL ROLE themis_kernel");
        await ExecuteAsync(connection, "UPDATE kernel.item_posting_group SET name = 'Renamed', version = version + 1 WHERE code = 'TRADE_GOODS'");
        Assert.Equal(2L, await ScalarAsync(connection, "SELECT version FROM kernel.item_posting_group WHERE code = 'TRADE_GOODS'"));
        foreach (string table in MasterDataTables)
        {
            await AssertFailsAsync(connection, InsufficientPrivilege, $"DELETE FROM kernel.{table}");
        }

        await ExecuteAsync(connection, "SET LOCAL ROLE themis_fixture");
        await AssertFailsAsync(connection, InsufficientPrivilege, "SELECT count(*) FROM kernel.item");
        await transaction.RollbackAsync(Ct);
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using NpgsqlCommand command = new(sql, connection);
        await command.ExecuteNonQueryAsync(Ct);
    }

    private static async Task<object?> ScalarAsync(NpgsqlConnection connection, string sql)
    {
        await using NpgsqlCommand command = new(sql, connection);
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

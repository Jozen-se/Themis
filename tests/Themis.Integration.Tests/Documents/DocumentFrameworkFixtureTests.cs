using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Themis.Kernel.Contracts.Documents;
using Themis.Kernel.Contracts.Security;
using Themis.Kernel.Documents;
using Themis.Kernel.Organisation;
using Themis.Kernel.Persistence;

namespace Themis.Integration.Tests.Documents;

/// <summary>
/// Milestone 1 step 7 (brief, Tests): the registry equals the catalogue after Themis.Migrator ran and a second run
/// changes nothing (D-40); the numbering-only check holds in the database; the number series fixture exists with
/// an audit event each and no outbox event; the series constraints of the Specification hold (fiscal year exactly
/// when PerFiscalYear and of the same company, one series per company, type and fiscal year, gapless where the type
/// requires it, JOURNAL_ENTRY gapless and PerFiscalYear); the company scope of lines is enforced by the database as
/// the second line of defence; the envelope columns are NOT NULL and the migration stops on a database with outbox
/// rows (D-50); the kernel role never deletes a document and only reads the registry (I-14, R-DOC-06).
/// </summary>
[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Integration")]
public sealed class DocumentFrameworkFixtureTests : IAsyncLifetime
{
    private const string InsufficientPrivilege = "42501";
    private const string NotNullViolation = "23502";
    private const string ForeignKeyViolation = "23503";
    private const string UniqueViolation = "23505";
    private const string CheckViolation = "23514";
    private const string RaiseException = "P0001";

    private static readonly Guid Company = Milestone1OrganisationFixture.CompanyId;
    private static readonly Guid FiscalYear = Milestone1OrganisationFixture.FiscalYearId;
    private static readonly Guid Warehouse = Milestone1OrganisationFixture.WarehouseId;
    private static readonly string System = WellKnownUsers.System.Value.ToString();

    private readonly PostgresFixture _db;

    public DocumentFrameworkFixtureTests(PostgresFixture db)
    {
        _db = db;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Audit => $"now(), '{System}', now(), '{System}'";

    public async ValueTask InitializeAsync() => await _db.TruncateAsync(Ct);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task D_40_the_registry_equals_the_catalogue_after_the_migrator_ran_INVENTORY_ADJUSTED_included_and_a_second_run_changes_nothing()
    {
        await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(_db.SuperuserConnectionString, Ct);
        const string registryAudit = "FROM kernel.audit_event WHERE command_name = 'SYSTEM.SEED_CATALOGUE' AND entity_type IN ('DocumentType', 'DocumentTransition')";
        long expectedRows = DocumentTypes.All.Count + DocumentTypes.All.Sum(type => type.Transitions.Count);

        Assert.Equal((long)DocumentTypes.All.Count, await ScalarAsync(connection, "SELECT count(*) FROM kernel.document_type"));
        foreach (DocumentTypeDefinition type in DocumentTypes.All)
        {
            Assert.Equal(type.NumberAssigned.ToString(), await ScalarAsync(connection, "SELECT number_assigned FROM kernel.document_type WHERE code = @code", ("code", type.Code)));
            Assert.Equal(type.Gapless, await ScalarAsync(connection, "SELECT gapless FROM kernel.document_type WHERE code = @code", ("code", type.Code)));
            Assert.Equal(type.PostsInventory, await ScalarAsync(connection, "SELECT posts_inventory FROM kernel.document_type WHERE code = @code", ("code", type.Code)));
            Assert.Equal(type.PostsGeneralLedger, await ScalarAsync(connection, "SELECT posts_general_ledger FROM kernel.document_type WHERE code = @code", ("code", type.Code)));
            Assert.Equal(type.PartyRole, await ScalarAsync(connection, "SELECT party_role FROM kernel.document_type WHERE code = @code", ("code", type.Code)));
            Assert.Equal((long)type.Transitions.Count, await ScalarAsync(connection, "SELECT count(*) FROM kernel.document_transition WHERE document_type = @code", ("code", type.Code)));
            foreach (DocumentTransition transition in type.Transitions)
            {
                Assert.Equal(transition.To.ToString(), await ScalarAsync(connection, "SELECT to_status FROM kernel.document_transition WHERE document_type = @code AND command = @command AND from_status = @from", ("code", type.Code), ("command", transition.Command), ("from", transition.From.ToString())));
                Assert.Equal(transition.EventName, await ScalarAsync(connection, "SELECT event_name FROM kernel.document_transition WHERE document_type = @code AND command = @command AND from_status = @from", ("code", type.Code), ("command", transition.Command), ("from", transition.From.ToString())));
            }
        }

        Assert.Equal("INVENTORY_ADJUSTED", await ScalarAsync(connection, "SELECT event_name FROM kernel.document_transition WHERE document_type = 'INVENTORY_ADJUSTMENT' AND command = 'POST'"));
        Assert.Equal(0L, await ScalarAsync(connection, "SELECT count(*) FROM kernel.document_transition WHERE document_type = 'JOURNAL_ENTRY'"));
        Assert.Equal(WellKnownUsers.System.Value, await ScalarAsync(connection, "SELECT DISTINCT created_by FROM kernel.document_type"));
        // R-AUD-01: one audit event per row written under the system user; no outbox event, because no command ran.
        Assert.Equal(expectedRows, await ScalarAsync(connection, $"SELECT count(*) {registryAudit}"));
        Assert.Equal(expectedRows, await ScalarAsync(connection, $"SELECT count(DISTINCT entity_id) {registryAudit}"));
        Assert.Equal(0L, await ScalarAsync(connection, "SELECT count(*) FROM kernel.outbox_event WHERE aggregate_type IN ('DocumentType', 'DocumentTransition')"));

        Assert.Equal(0, await Themis.Migrator.Program.Main(["--connection", _db.ConnectionStringFor("themis_migrator"), "--fixture", "milestone-1"]));

        Assert.Equal((long)DocumentTypes.All.Count, await ScalarAsync(connection, "SELECT count(*) FROM kernel.document_type"));
        Assert.Equal(expectedRows, await ScalarAsync(connection, $"SELECT count(*) {registryAudit}"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT DISTINCT version FROM kernel.document_type"));
    }

    [Fact]
    public async Task A_numbering_only_row_with_a_posting_flag_is_refused_by_the_database_and_the_registry_is_read_only_for_the_kernel_role()
    {
        await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(_db.ConnectionStringFor("themis_migrator"), Ct);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(Ct);

        await AssertFailsAsync(connection, CheckViolation, $"INSERT INTO kernel.document_type (id, code, number_assigned, gapless, posts_inventory, posts_general_ledger, created_at, created_by, updated_at, updated_by) VALUES (gen_random_uuid(), 'PROBE_ONLY', 'NumberingOnly', true, false, true, {Audit})");
        await AssertFailsAsync(connection, CheckViolation, $"INSERT INTO kernel.document_type (id, code, number_assigned, gapless, posts_inventory, posts_general_ledger, party_role, created_at, created_by, updated_at, updated_by) VALUES (gen_random_uuid(), 'PROBE_ONLY', 'NumberingOnly', true, false, false, 'Customer', {Audit})");
        await AssertFailsAsync(connection, CheckViolation, $"INSERT INTO kernel.document_type (id, code, number_assigned, gapless, posts_inventory, posts_general_ledger, created_at, created_by, updated_at, updated_by) VALUES (gen_random_uuid(), 'PROBE_ONLY', 'Sometimes', true, false, false, {Audit})");
        await ExecuteAsync(connection, $"INSERT INTO kernel.document_type (id, code, number_assigned, gapless, posts_inventory, posts_general_ledger, created_at, created_by, updated_at, updated_by) VALUES (gen_random_uuid(), 'PROBE_ONLY', 'NumberingOnly', true, false, false, {Audit})");
        // R-DOC-01: one transition per type, command and from-status.
        await AssertFailsAsync(connection, UniqueViolation, $"INSERT INTO kernel.document_transition (id, document_type, command, from_status, to_status, event_name, created_at, created_by, updated_at, updated_by) VALUES (gen_random_uuid(), 'INVENTORY_ADJUSTMENT', 'DISCARD', 'Draft', 'Posted', 'X', {Audit})");
        await transaction.RollbackAsync(Ct);

        await using NpgsqlConnection app = await PostgresFixture.OpenAsync(_db.ConnectionStringFor("themis_app"), Ct);
        await using NpgsqlTransaction appTransaction = await app.BeginTransactionAsync(Ct);
        await ExecuteAsync(app, "SET LOCAL ROLE themis_kernel");
        Assert.Equal(2L, await ScalarAsync(app, "SELECT count(*) FROM kernel.document_type"));
        await AssertFailsAsync(app, InsufficientPrivilege, $"INSERT INTO kernel.document_type (id, code, number_assigned, gapless, posts_inventory, posts_general_ledger, created_at, created_by, updated_at, updated_by) VALUES (gen_random_uuid(), 'PROBE', 'OnPost', false, false, false, {Audit})");
        await AssertFailsAsync(app, InsufficientPrivilege, "UPDATE kernel.document_transition SET event_name = 'TAMPERED'");
        await AssertFailsAsync(app, InsufficientPrivilege, "DELETE FROM kernel.document_type");
        await appTransaction.RollbackAsync(Ct);
    }

    [Fact]
    public async Task The_fixture_holds_the_IA_and_JE2026_series_with_an_audit_event_each_and_no_outbox_event_and_the_series_constraints_hold()
    {
        await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(_db.SuperuserConnectionString, Ct);
        const string seedAudit = "FROM kernel.audit_event WHERE command_name = 'SYSTEM.SEED_FIXTURE' AND entity_type = 'NumberSeries'";

        Assert.Equal(2L, await ScalarAsync(connection, "SELECT count(*) FROM kernel.number_series"));
        Assert.Equal(("IA-", 6, 1L, false, "Continuous", null, false), await SeriesAsync(connection, Milestone1NumberSeriesFixture.InventoryAdjustmentSeriesId));
        Assert.Equal(("JE2026-", 6, 1L, true, "PerFiscalYear", (Guid?)FiscalYear, true), await SeriesAsync(connection, Milestone1NumberSeriesFixture.JournalEntrySeriesId));
        Assert.Equal(Company, await ScalarAsync(connection, "SELECT DISTINCT company_id FROM kernel.number_series"));
        Assert.Equal(2L, await ScalarAsync(connection, $"SELECT count(*) {seedAudit}"));
        Assert.Equal("JE2026-", await ScalarAsync(connection, $"SELECT changes->'prefix'->>'after' {seedAudit} AND entity_id = @id", ("id", Milestone1NumberSeriesFixture.JournalEntrySeriesId)));
        Assert.Equal(0L, await ScalarAsync(connection, "SELECT count(*) FROM kernel.outbox_event WHERE aggregate_id IN (SELECT id FROM kernel.number_series)"));

        await using NpgsqlConnection migrator = await PostgresFixture.OpenAsync(_db.ConnectionStringFor("themis_migrator"), Ct);
        await using NpgsqlTransaction transaction = await migrator.BeginTransactionAsync(Ct);
        string otherCompanyYear = Guid.NewGuid().ToString();
        // Specification, NumberSeries: the fiscal year exactly when the scope is PerFiscalYear.
        await AssertFailsAsync(migrator, CheckViolation, Series("INVENTORY_ADJUSTMENT", "PerFiscalYear", null, gapless: false, typeRequiresGapless: false, prefix: "X-"));
        await AssertFailsAsync(migrator, CheckViolation, Series("INVENTORY_ADJUSTMENT", "Continuous", FiscalYear, gapless: false, typeRequiresGapless: false, prefix: "X-"));
        // One series per company, type and fiscal year, also where the fiscal year is empty (NULLS NOT DISTINCT).
        await AssertFailsAsync(migrator, UniqueViolation, Series("INVENTORY_ADJUSTMENT", "Continuous", null, gapless: false, typeRequiresGapless: false, prefix: "X-"));
        await AssertFailsAsync(migrator, UniqueViolation, Series("JOURNAL_ENTRY", "PerFiscalYear", FiscalYear, gapless: true, typeRequiresGapless: true, prefix: "X-"));
        // Gapless cannot be switched off where the type requires it, and the stored type flag must be the registry's.
        await AssertFailsAsync(migrator, CheckViolation, Series("JOURNAL_ENTRY", "PerFiscalYear", FiscalYear, gapless: false, typeRequiresGapless: true, prefix: "X-", company: Guid.NewGuid()));
        await AssertFailsAsync(migrator, ForeignKeyViolation, Series("JOURNAL_ENTRY", "PerFiscalYear", FiscalYear, gapless: true, typeRequiresGapless: false, prefix: "X-", company: Guid.NewGuid()));
        await AssertFailsAsync(migrator, ForeignKeyViolation, Series("INVENTORY_ADJUSTMENT", "Continuous", null, gapless: true, typeRequiresGapless: true, prefix: "X-", company: Guid.NewGuid()));
        // JOURNAL_ENTRY series are always gapless and PerFiscalYear; the fiscal year belongs to the series' company.
        await AssertFailsAsync(migrator, CheckViolation, Series("JOURNAL_ENTRY", "Continuous", null, gapless: true, typeRequiresGapless: true, prefix: "X-", company: Guid.NewGuid()));
        await AssertFailsAsync(migrator, ForeignKeyViolation, Series("JOURNAL_ENTRY", "PerFiscalYear", Guid.Parse(otherCompanyYear), gapless: true, typeRequiresGapless: true, prefix: "X-"));
        await AssertFailsAsync(migrator, CheckViolation, Series("INVENTORY_ADJUSTMENT", "Continuous", null, gapless: false, typeRequiresGapless: false, prefix: "X-", nextNumber: 0, company: Guid.NewGuid()));
        await AssertFailsAsync(migrator, CheckViolation, Series("INVENTORY_ADJUSTMENT", "Continuous", null, gapless: false, typeRequiresGapless: false, prefix: "X-", padding: 21, company: Guid.NewGuid()));
        // A gapless INVENTORY_ADJUSTMENT series per fiscal year is allowed: the type does not require gapless, a company may still choose it.
        await ExecuteAsync(migrator, Series("INVENTORY_ADJUSTMENT", "PerFiscalYear", FiscalYear, gapless: true, typeRequiresGapless: false, prefix: "IA2026-"));
        await transaction.RollbackAsync(Ct);
    }

    [Fact]
    public async Task The_company_scope_of_a_line_and_the_D_50_envelope_hold_in_the_database_and_the_kernel_role_never_deletes_a_document()
    {
        await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(_db.ConnectionStringFor("themis_migrator"), Ct);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(Ct);
        Guid otherCompany = Guid.NewGuid();
        Guid document = Guid.NewGuid();
        Guid item = Guid.NewGuid();
        await ExecuteAsync(connection, $"INSERT INTO kernel.company (id, code, party_id, legal_name, organisation_number, country_code, base_currency, time_zone, fiscal_year_start_month, chart_of_accounts_id, rounding_mode, tax_rounding_level, status, created_at, created_by, updated_at, updated_by) VALUES ('{otherCompany}', 'SE09', '{Milestone1OrganisationFixture.CompanyPartyId}', 'Other', '1', 'SE', 'SEK', 'Europe/Stockholm', 1, '{Milestone1OrganisationFixture.ChartOfAccountsId}', 'HalfAwayFromZero', 'Line', 'Active', {Audit})");
        await ExecuteAsync(connection, $"INSERT INTO kernel.item (id, item_number, description, item_type, base_unit, lot_tracking, created_at, created_by, updated_at, updated_by) VALUES ('{item}', 'SCOPE-001', 'Scope probe', 'Stocked', 'H87', 'None', {Audit})");
        await ExecuteAsync(connection, $"INSERT INTO kernel.item_company (id, item_id, company_id, status, item_posting_group, tax_class, costing_method, sales_allowed, purchase_allowed, created_at, created_by, updated_at, updated_by) VALUES (gen_random_uuid(), '{item}', '{otherCompany}', 'Active', 'TRADE_GOODS', 'Standard', 'MovingAverage', true, true, {Audit})");
        await ExecuteAsync(connection, $"INSERT INTO kernel.document (id, company_id, document_type, status, draft_reference, document_date, currency, created_at, created_by, updated_at, updated_by) VALUES ('{document}', '{Company}', 'INVENTORY_ADJUSTMENT', 'Draft', '{document}', '2026-06-15', 'SEK', {Audit})");

        string line = $"INSERT INTO kernel.document_line (id, company_id, document_id, line_number, line_type, item_id, description, quantity, unit_code, quantity_base, warehouse_id, stock_status, line_status, created_at, created_by, updated_at, updated_by) VALUES";
        // D-43: the item is set up in another company only; the line's company is the document's.
        await AssertFailsAsync(connection, ForeignKeyViolation, $"{line} (gen_random_uuid(), '{Company}', '{document}', 1, 'Item', '{item}', 'x', 1, 'H87', 1, '{Warehouse}', 'Available', 'Open', {Audit})");
        await AssertFailsAsync(connection, ForeignKeyViolation, $"{line} (gen_random_uuid(), '{otherCompany}', '{document}', 1, 'Item', '{item}', 'x', 1, 'H87', 1, '{Warehouse}', 'Available', 'Open', {Audit})");
        // The warehouse belongs to the document's company; the header's too.
        await ExecuteAsync(connection, $"INSERT INTO kernel.item_company (id, item_id, company_id, status, item_posting_group, tax_class, costing_method, sales_allowed, purchase_allowed, created_at, created_by, updated_at, updated_by) VALUES (gen_random_uuid(), '{item}', '{Company}', 'Active', 'TRADE_GOODS', 'Standard', 'MovingAverage', true, true, {Audit})");
        await AssertFailsAsync(connection, ForeignKeyViolation, $"{line} (gen_random_uuid(), '{Company}', '{document}', 1, 'Item', '{item}', 'x', 1, 'H87', 1, '{Guid.NewGuid()}', 'Available', 'Open', {Audit})");
        await AssertFailsAsync(connection, ForeignKeyViolation, $"UPDATE kernel.document SET warehouse_id = '{Guid.NewGuid()}' WHERE id = '{document}'");
        // Shared rules: positive line numbers, unique per document, a quantity never 0, an Item line complete, enum values checked.
        await AssertFailsAsync(connection, CheckViolation, $"{line} (gen_random_uuid(), '{Company}', '{document}', 0, 'Item', '{item}', 'x', 1, 'H87', 1, '{Warehouse}', 'Available', 'Open', {Audit})");
        await AssertFailsAsync(connection, CheckViolation, $"{line} (gen_random_uuid(), '{Company}', '{document}', 1, 'Item', '{item}', 'x', 0, 'H87', 0, '{Warehouse}', 'Available', 'Open', {Audit})");
        await AssertFailsAsync(connection, CheckViolation, $"{line} (gen_random_uuid(), '{Company}', '{document}', 1, 'Item', '{item}', 'x', 1, 'H87', 1, NULL, 'Available', 'Open', {Audit})");
        await AssertFailsAsync(connection, CheckViolation, $"{line} (gen_random_uuid(), '{Company}', '{document}', 1, 'Item', '{item}', 'x', 1, 'H87', 1, '{Warehouse}', 'Reserved', 'Open', {Audit})");
        await ExecuteAsync(connection, $"{line} (gen_random_uuid(), '{Company}', '{document}', 1, 'Item', '{item}', 'x', 1, 'H87', 1, '{Warehouse}', 'Available', 'Open', {Audit})");
        await AssertFailsAsync(connection, UniqueViolation, $"{line} (gen_random_uuid(), '{Company}', '{document}', 1, 'Item', '{item}', 'x', 1, 'H87', 1, '{Warehouse}', 'Available', 'Open', {Audit})");
        await AssertFailsAsync(connection, CheckViolation, $"UPDATE kernel.document SET status = 'Approved' WHERE id = '{document}'");
        await AssertFailsAsync(connection, CheckViolation, $"INSERT INTO kernel.inventory_adjustment (id, company_id, reason_code, created_at, created_by, updated_at, updated_by) VALUES ('{document}', '{Company}', 'Bogus', {Audit})");
        await AssertFailsAsync(connection, ForeignKeyViolation, $"INSERT INTO kernel.inventory_adjustment (id, company_id, reason_code, created_at, created_by, updated_at, updated_by) VALUES ('{document}', '{otherCompany}', 'Scrap', {Audit})");
        // A document number, once assigned, is unique per company and type.
        Guid second = Guid.NewGuid();
        await ExecuteAsync(connection, $"INSERT INTO kernel.document (id, company_id, document_type, status, document_number, draft_reference, document_date, currency, created_at, created_by, updated_at, updated_by) VALUES ('{second}', '{Company}', 'INVENTORY_ADJUSTMENT', 'Draft', 'IA-000001', '{second}', '2026-06-15', 'SEK', {Audit})");
        await AssertFailsAsync(connection, UniqueViolation, $"UPDATE kernel.document SET document_number = 'IA-000001' WHERE id = '{document}'");

        // D-50: the envelope columns are NOT NULL; a company named must exist.
        await AssertFailsAsync(connection, NotNullViolation, "INSERT INTO kernel.outbox_event (id, event_type, schema_version, aggregate_type, aggregate_id, correlation_id, payload, occurred_at) VALUES (gen_random_uuid(), 'PROBE', 1, 'PROBE', gen_random_uuid(), 'c', '{}', now())");
        await AssertFailsAsync(connection, NotNullViolation, "INSERT INTO kernel.outbox_event (id, event_type, schema_version, aggregate_id, command_name, correlation_id, payload, occurred_at) VALUES (gen_random_uuid(), 'PROBE', 1, gen_random_uuid(), 'PROBE.WRITE', 'c', '{}', now())");
        await AssertFailsAsync(connection, NotNullViolation, "INSERT INTO kernel.outbox_event (id, event_type, schema_version, aggregate_type, aggregate_id, command_name, payload, occurred_at) VALUES (gen_random_uuid(), 'PROBE', 1, 'PROBE', gen_random_uuid(), 'PROBE.WRITE', '{}', now())");
        await AssertFailsAsync(connection, ForeignKeyViolation, $"INSERT INTO kernel.outbox_event (id, event_type, schema_version, aggregate_type, aggregate_id, company_id, command_name, correlation_id, payload, occurred_at) VALUES (gen_random_uuid(), 'PROBE', 1, 'PROBE', gen_random_uuid(), '{Guid.NewGuid()}', 'PROBE.WRITE', 'c', '{{}}', now())");
        await transaction.RollbackAsync(Ct);

        // I-14 and R-DOC-06: the kernel role updates documents, lines, extensions and series but never deletes them.
        await using NpgsqlConnection app = await PostgresFixture.OpenAsync(_db.ConnectionStringFor("themis_app"), Ct);
        await using NpgsqlTransaction appTransaction = await app.BeginTransactionAsync(Ct);
        await ExecuteAsync(app, "SET LOCAL ROLE themis_kernel");
        await ExecuteAsync(app, "UPDATE kernel.number_series SET next_number = next_number WHERE document_type = 'INVENTORY_ADJUSTMENT'");
        foreach (string table in new[] { "document", "document_line", "inventory_adjustment", "number_series" })
        {
            await AssertFailsAsync(app, InsufficientPrivilege, $"DELETE FROM kernel.{table}");
        }

        await appTransaction.RollbackAsync(Ct);
    }

    [Fact]
    public async Task D_50_the_envelope_migration_stops_with_exit_code_6_on_a_database_that_holds_outbox_rows_and_runs_once_they_are_gone()
    {
        string fresh = await _db.CreateEmptyDatabaseAsync(Ct);
        try
        {
            string migrator = new NpgsqlConnectionStringBuilder(fresh) { Username = "themis_migrator", Password = PostgresFixture.TestRolePassword }.ConnectionString;
            // The database as step 6 left it: migrated up to the general ledger tables, with one outbox row of the old shape.
            await using (KernelDbContext context = KernelDbContextFactory.ForMigrations(migrator))
            {
                await context.GetService<IMigrator>().MigrateAsync("20261013121553_GeneralLedgerTables", Ct);
            }

            await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(migrator, Ct);
            await ExecuteAsync(connection, "INSERT INTO kernel.outbox_event (id, event_type, schema_version, aggregate_id, payload, occurred_at) VALUES (gen_random_uuid(), 'OLD_EVENT', 1, gen_random_uuid(), '{}', now())");

            Assert.Equal(6, await Themis.Migrator.Program.Main(["--connection", migrator]));
            Assert.Equal(0L, await ScalarAsync(connection, "SELECT count(*) FROM kernel.__ef_migrations_history WHERE \"MigrationId\" LIKE '%DocumentFrameworkTables'"));
            // The migration itself refuses too, as the second line of defence.
            await AssertFailsAsync(connection, RaiseException, Themis.Kernel.Persistence.Migrations.DocumentFrameworkTables.OutboxGuardSql, insideTransaction: false);

            await ExecuteAsync(connection, "DELETE FROM kernel.outbox_event");
            Assert.Equal(0, await Themis.Migrator.Program.Main(["--connection", migrator]));
            Assert.Equal(1L, await ScalarAsync(connection, "SELECT count(*) FROM kernel.__ef_migrations_history WHERE \"MigrationId\" LIKE '%DocumentFrameworkTables'"));
            Assert.Equal(2L, await ScalarAsync(connection, "SELECT count(*) FROM kernel.document_type"));
            Assert.Equal(0L, await ScalarAsync(connection, "SELECT count(*) FROM kernel.number_series"));
        }
        finally
        {
            await _db.DropDatabaseAsync(fresh, CancellationToken.None);
        }
    }

    private static string Series(string type, string scope, Guid? fiscalYear, bool gapless, bool typeRequiresGapless, string prefix, long nextNumber = 1, int padding = 6, Guid? company = null) =>
        "INSERT INTO kernel.number_series (id, company_id, document_type, scope, fiscal_year_id, prefix, padding, next_number, gapless, type_requires_gapless, created_at, created_by, updated_at, updated_by) " +
        $"VALUES (gen_random_uuid(), '{company ?? Company}', '{type}', '{scope}', {(fiscalYear is null ? "NULL" : $"'{fiscalYear}'")}, '{prefix}', {padding}, {nextNumber}, {gapless.ToString().ToLowerInvariant()}, {typeRequiresGapless.ToString().ToLowerInvariant()}, {Audit})";

    private static async Task<(string? Prefix, int Padding, long NextNumber, bool Gapless, string Scope, Guid? FiscalYear, bool TypeRequiresGapless)> SeriesAsync(NpgsqlConnection connection, Guid id)
    {
        await using NpgsqlCommand command = new("SELECT prefix, padding, next_number, gapless, scope, fiscal_year_id, type_requires_gapless FROM kernel.number_series WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", id);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(Ct);
        Assert.True(await reader.ReadAsync(Ct));
        return (reader.IsDBNull(0) ? null : reader.GetString(0), reader.GetInt32(1), reader.GetInt64(2), reader.GetBoolean(3), reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetGuid(5), reader.GetBoolean(6));
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

    /// <summary>Asserts the SQLSTATE inside a savepoint so the transaction stays usable; outside a transaction the statement fails on its own.</summary>
    private static async Task AssertFailsAsync(NpgsqlConnection connection, string sqlState, string sql, bool insideTransaction = true)
    {
        if (insideTransaction)
        {
            await ExecuteAsync(connection, "SAVEPOINT probe");
        }

        PostgresException exception = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connection, sql));
        Assert.Equal(sqlState, exception.SqlState);
        if (insideTransaction)
        {
            await ExecuteAsync(connection, "ROLLBACK TO SAVEPOINT probe");
        }
    }
}

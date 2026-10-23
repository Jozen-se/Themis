using Npgsql;
using Themis.Kernel.Contracts.Security;
using Themis.Kernel.Contracts.Settings;
using Themis.Kernel.Persistence.Seeding;
using Themis.Kernel.Settings;

namespace Themis.Integration.Tests.Settings;

/// <summary>
/// The migrator's catalogue run (D-58): every definition in code is in kernel.setting_definition after a run; a stored
/// value a tightened range left behind is reported and never changed; a definition that left the catalogue is marked
/// retired and reported, its values stay, and it comes back when the catalogue names it again. Data and Planned rows
/// carry no value type, default or validation, which the database checks (D-63).
/// </summary>
[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Integration")]
public sealed class SettingCatalogSeederTests
{
    private readonly PostgresFixture _db;

    public SettingCatalogSeederTests(PostgresFixture db)
    {
        _db = db;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task D_58_the_migrator_writes_every_definition_reports_an_out_of_range_value_and_an_orphaned_definition_and_changes_neither()
    {
        string database = await _db.CreateSeededDatabaseAsync(Ct);
        try
        {
            string migrator = new NpgsqlConnectionStringBuilder(database) { Username = "themis_migrator", Password = PostgresFixture.TestRolePassword }.ConnectionString;
            await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(database, Ct);

            Assert.Equal((long)SettingCatalog.Definitions.Count, await ScalarAsync(connection, "SELECT count(*) FROM kernel.setting_definition"));
            Assert.Equal(0L, await ScalarAsync(connection, "SELECT count(*) FROM kernel.setting_definition WHERE retired"));
            Assert.Equal("Installation", await ScalarAsync(connection, "SELECT levels FROM kernel.setting_definition WHERE key = 'idempotency.retention_days'"));
            Assert.Equal("Company,Warehouse", await ScalarAsync(connection, "SELECT levels FROM kernel.setting_definition WHERE key = 'inventory.negative_stock_allowed'"));
            Assert.Equal((long)SettingCatalog.Definitions.Count, await ScalarAsync(connection, "SELECT count(*) FROM kernel.audit_event WHERE command_name = 'SYSTEM.SEED_CATALOGUE' AND entity_type = 'SettingDefinition'"));

            // A second run with the same catalogue changes nothing and reports nothing.
            SettingCatalogReport same = await SettingDefinitionSeeder.ApplyCatalogueAsync(migrator, SettingCatalog.Definitions, SettingValueFormat.IsValid, TimeProvider.System.GetUtcNow(), WellKnownUsers.System, Ct);
            Assert.Equal((0, 0), (same.Changed, same.Retired));
            Assert.Empty(same.Warnings);

            // A value of 30 days, then a catalogue whose maximum dropped to 10: reported, not changed.
            await ExecuteAsync(connection, "INSERT INTO kernel.setting_value (id, definition_key, level, kind, value, version, recorded_at, recorded_by, command_id) VALUES (gen_random_uuid(), 'idempotency.retention_days', 'Installation', 'Set', '30', 1, now(), @system, gen_random_uuid())", ("system", WellKnownUsers.System.Value));
            SettingDefinition retention = SettingCatalog.Find(SettingKeys.IdempotencyRetentionDays)!;
            List<SettingDefinition> tightened = [.. SettingCatalog.Definitions.Where(definition => definition.Key != retention.Key), retention with { Max = "10" }];
            SettingCatalogReport report = await SettingDefinitionSeeder.ApplyCatalogueAsync(migrator, tightened, SettingValueFormat.IsValid, TimeProvider.System.GetUtcNow(), WellKnownUsers.System, Ct);
            Assert.Equal(1, report.Changed);
            string warning = Assert.Single(report.Warnings);
            Assert.Contains("idempotency.retention_days", warning, StringComparison.Ordinal);
            Assert.Contains("'30'", warning, StringComparison.Ordinal);
            Assert.Contains("outside", warning, StringComparison.Ordinal);
            Assert.Equal("10", await ScalarAsync(connection, "SELECT max FROM kernel.setting_definition WHERE key = 'idempotency.retention_days'"));
            Assert.Equal("30", await ScalarAsync(connection, "SELECT value FROM kernel.setting_value WHERE definition_key = 'idempotency.retention_days'"));

            // A catalogue without the definition: the row is retired and reported with its value count; nothing is deleted.
            SettingCatalogReport orphaned = await SettingDefinitionSeeder.ApplyCatalogueAsync(migrator, [.. SettingCatalog.Definitions.Where(definition => definition.Key != retention.Key)], SettingValueFormat.IsValid, TimeProvider.System.GetUtcNow(), WellKnownUsers.System, Ct);
            Assert.Equal(1, orphaned.Retired);
            Assert.Contains(orphaned.Warnings, line => line.Contains("has left the catalogue", StringComparison.Ordinal) && line.Contains("1 value row(s) stay", StringComparison.Ordinal));
            Assert.Equal(true, await ScalarAsync(connection, "SELECT retired FROM kernel.setting_definition WHERE key = 'idempotency.retention_days'"));
            Assert.Equal(1L, await ScalarAsync(connection, "SELECT count(*) FROM kernel.setting_value"));

            // Back in the catalogue: the row is aligned again and no longer retired.
            SettingCatalogReport restored = await SettingDefinitionSeeder.ApplyCatalogueAsync(migrator, SettingCatalog.Definitions, SettingValueFormat.IsValid, TimeProvider.System.GetUtcNow(), WellKnownUsers.System, Ct);
            Assert.Equal((1, 0), (restored.Changed, restored.Retired));
            Assert.Equal(false, await ScalarAsync(connection, "SELECT retired FROM kernel.setting_definition WHERE key = 'idempotency.retention_days'"));
            Assert.Equal("90", await ScalarAsync(connection, "SELECT max FROM kernel.setting_definition WHERE key = 'idempotency.retention_days'"));

            // The migrator prints the warnings and still exits 0: a report, never a stop.
            await ExecuteAsync(connection, "UPDATE kernel.setting_value SET value = '30' WHERE definition_key = 'idempotency.retention_days'");
            Assert.Equal(0, await Themis.Migrator.Program.Main(["--connection", migrator]));
        }
        finally
        {
            await _db.DropDatabaseAsync(database, CancellationToken.None);
        }
    }

    [Fact]
    public async Task D_63_Data_and_Planned_rows_carry_no_value_type_default_or_validation_the_database_refuses_one_that_does_and_a_retired_row_keeps_the_shape()
    {
        string database = await _db.CreateSeededDatabaseAsync(Ct);
        try
        {
            string migrator = new NpgsqlConnectionStringBuilder(database) { Username = "themis_migrator", Password = PostgresFixture.TestRolePassword }.ConnectionString;
            await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(database, Ct);

            // RR-98, RR-99: every Data and Planned row is bare; every Setting and Column row has its value type.
            Assert.Equal(0L, await ScalarAsync(connection, "SELECT count(*) FROM kernel.setting_definition WHERE backing IN ('Data', 'Planned') AND (value_type IS NOT NULL OR default_value IS NOT NULL OR min IS NOT NULL OR max IS NOT NULL OR allowed_values IS NOT NULL)"));
            Assert.Equal(0L, await ScalarAsync(connection, "SELECT count(*) FROM kernel.setting_definition WHERE backing IN ('Setting', 'Column') AND value_type IS NULL"));
            Assert.Equal(true, await ScalarAsync(connection, "SELECT default_value IS NULL FROM kernel.setting_definition WHERE key = 'security.sod_conflicts'"));

            // The check refuses a Planned row with a value type, and a Setting row without one, whoever writes it.
            PostgresException typed = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connection, "UPDATE kernel.setting_definition SET value_type = 'Code' WHERE key = 'sales.credit_check_mode'"));
            Assert.Equal(PostgresErrorCodes.CheckViolation, typed.SqlState);
            Assert.Equal("ck_setting_definition_shape", typed.ConstraintName);
            PostgresException untyped = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connection, "UPDATE kernel.setting_definition SET value_type = NULL WHERE key = 'document.max_lines'"));
            Assert.Equal("ck_setting_definition_shape", untyped.ConstraintName);

            // A catalogue that gives a Planned definition a type is refused by the database; nothing of the run is kept.
            SettingDefinition planned = SettingCatalog.Find("sales.credit_check_mode")!;
            List<SettingDefinition> invented = [.. SettingCatalog.Definitions.Where(definition => definition.Key != planned.Key), planned with { ValueType = SettingValueType.Code, DefaultValue = "Block" }];
            Exception refused = await Assert.ThrowsAnyAsync<Exception>(() => SettingDefinitionSeeder.ApplyCatalogueAsync(migrator, invented, SettingValueFormat.IsValid, TimeProvider.System.GetUtcNow(), WellKnownUsers.System, Ct));
            Assert.Contains("ck_setting_definition_shape", refused.ToString(), StringComparison.Ordinal);
            Assert.Equal(true, await ScalarAsync(connection, "SELECT value_type IS NULL FROM kernel.setting_definition WHERE key = 'sales.credit_check_mode'"));

            // A Planned definition that leaves the catalogue is retired and keeps the shape the check demands.
            SettingCatalogReport retired = await SettingDefinitionSeeder.ApplyCatalogueAsync(migrator, [.. SettingCatalog.Definitions.Where(definition => definition.Key != planned.Key)], SettingValueFormat.IsValid, TimeProvider.System.GetUtcNow(), WellKnownUsers.System, Ct);
            Assert.Equal(1, retired.Retired);
            Assert.Equal(true, await ScalarAsync(connection, "SELECT retired FROM kernel.setting_definition WHERE key = 'sales.credit_check_mode'"));
            Assert.Equal(0L, await ScalarAsync(connection, "SELECT count(*) FROM kernel.setting_definition WHERE key = 'sales.credit_check_mode' AND (value_type IS NOT NULL OR default_value IS NOT NULL OR allowed_values IS NOT NULL)"));
        }
        finally
        {
            await _db.DropDatabaseAsync(database, CancellationToken.None);
        }
    }

    [Fact]
    public async Task RR_95_the_key_of_a_setting_definition_has_one_unique_structure_the_alternate_key_and_no_duplicate_index()
    {
        await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(_db.SuperuserConnectionString, Ct);

        Assert.Equal(0L, await ScalarAsync(connection, "SELECT count(*) FROM pg_indexes WHERE schemaname = 'kernel' AND indexname = 'ux_setting_definition_key'"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT count(*) FROM pg_constraint WHERE conname = 'ak_setting_definition_key' AND contype = 'u'"));
        // Exactly one unique index covers kernel.setting_definition.key alone: the one behind the alternate key.
        Assert.Equal(1L, await ScalarAsync(connection,
            "SELECT count(*) FROM pg_index i JOIN pg_class t ON t.oid = i.indrelid JOIN pg_namespace n ON n.oid = t.relnamespace " +
            "JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = i.indkey[0] " +
            "WHERE n.nspname = 'kernel' AND t.relname = 'setting_definition' AND i.indisunique AND i.indnatts = 1 AND a.attname = 'key'"));
    }

    private static async Task<object?> ScalarAsync(NpgsqlConnection connection, string sql, params (string Name, object? Value)[] parameters)
    {
        await using NpgsqlCommand command = new(sql, connection);
        foreach ((string name, object? value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return await command.ExecuteScalarAsync(Ct);
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, params (string Name, object? Value)[] parameters)
    {
        await using NpgsqlCommand command = new(sql, connection);
        foreach ((string name, object? value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        await command.ExecuteNonQueryAsync(Ct);
    }
}

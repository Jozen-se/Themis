using Npgsql;
using Themis.Kernel.Contracts.Security;
using Themis.Kernel.Contracts.Settings;
using Themis.Kernel.Persistence.Seeding;
using Themis.Kernel.Settings;

namespace Themis.Integration.Tests.Settings;

/// <summary>
/// The migrator's catalogue run (D-58): every definition in code is in kernel.setting_definition after a run; a stored
/// value a tightened range left behind is reported and never changed; a definition that left the catalogue is marked
/// retired and reported, its values stay, and it comes back when the catalogue names it again.
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

using System.Text.RegularExpressions;
using Npgsql;
using Themis.Kernel.Contracts.Security;
using Themis.Kernel.Organisation;
using Themis.Kernel.Persistence;
using Themis.Kernel.Persistence.Seeding;

namespace Themis.Integration.Tests.Hardening;

/// <summary>
/// RR-39 (Build Brief, Migrations): a database built by the migrator of the git tag milestone-1 and migrated to head
/// equals a database built from scratch at head, compared on the PostgreSQL catalog (tables, columns, constraints,
/// indexes, views, functions, triggers, ownership, grants, comments). The tag's database is the committed schema-only
/// dump with the rows that migrator run wrote, audit events excepted (db/migrations/drift/milestone-1.sql); each later
/// milestone tag adds its own.
/// Also RR-44 and RR-48: the hardening migration stops on a database with overlapping fiscal periods, the migrator
/// reports the PostgreSQL error with its SQLSTATE under exit code 8, nothing is applied, and the run is safe to repeat.
/// </summary>
[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Integration")]
public sealed partial class MigrationDriftTests
{
    private const string HardeningMigration = "20261018232940_HardeningBackstops";
    private const string MigrationBeforeHardening = "20261018212855_ReportingStockByWarehouse";

    private readonly PostgresFixture _db;

    public MigrationDriftTests(PostgresFixture db)
    {
        _db = db;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task RR_39_the_milestone_1_database_migrated_to_head_has_the_same_catalog_as_a_database_built_from_scratch()
    {
        string restored = await _db.CreateBareDatabaseAsync();
        string fresh = await _db.CreateEmptyDatabaseAsync(Ct);
        try
        {
            string dump = await File.ReadAllTextAsync(Path.Combine(DriftDirectory(), "milestone-1.sql"), Ct);
            await using (NpgsqlConnection connection = await PostgresFixture.OpenAsync(restored, Ct))
            {
                await using NpgsqlCommand restore = new(dump, connection) { CommandTimeout = 120 };
                await restore.ExecuteNonQueryAsync(Ct);
            }

            Assert.Equal(8L, await ScalarAsync(restored, "SELECT count(*) FROM kernel.__ef_migrations_history"));
            Assert.Equal(0, await Themis.Migrator.Program.RunAsync(["--connection", Migrator(restored)], PostgresFixture.MigratorEnvironment));
            Assert.Equal(0, await Themis.Migrator.Program.RunAsync(["--connection", Migrator(fresh)], PostgresFixture.MigratorEnvironment));

            IReadOnlyList<string> migrated = await CatalogAsync(restored);
            IReadOnlyList<string> scratch = await CatalogAsync(fresh);
            Assert.True(migrated.Count > 200, "The catalog snapshot looks empty.");
            string[] onlyMigrated = [.. migrated.Except(scratch, StringComparer.Ordinal)];
            string[] onlyScratch = [.. scratch.Except(migrated, StringComparer.Ordinal)];
            Assert.True(onlyMigrated.Length == 0 && onlyScratch.Length == 0,
                $"The migrated milestone-1 database and the fresh database differ (RR-39).{Environment.NewLine}Only after migrating from the tag:{Environment.NewLine}{string.Join(Environment.NewLine, onlyMigrated)}{Environment.NewLine}Only from scratch:{Environment.NewLine}{string.Join(Environment.NewLine, onlyScratch)}");
        }
        finally
        {
            await _db.DropDatabaseAsync(restored, CancellationToken.None);
            await _db.DropDatabaseAsync(fresh, CancellationToken.None);
        }
    }

    [Fact]
    public async Task RR_44_the_hardening_migration_stops_on_overlapping_fiscal_periods_and_RR_48_the_migrator_reports_the_error_with_its_SQLSTATE_under_exit_code_8_then_applies_cleanly_once_resolved()
    {
        string database = await _db.CreateEmptyDatabaseAsync(Ct);
        try
        {
            await KernelMigrations.ApplyUpToAsync(Migrator(database), MigrationBeforeHardening, Ct);
            await OrganisationFixtureSeeder.ApplyAsync(Migrator(database), Milestone1OrganisationFixture.Definition, TimeProvider.System.GetUtcNow(), WellKnownUsers.System, Ct);
            Guid overlap = Guid.NewGuid();
            string system = WellKnownUsers.System.Value.ToString();
            await ExecuteAsync(Migrator(database), $"INSERT INTO kernel.fiscal_period (id, fiscal_year_id, company_id, period_number, start_date, end_date, status, created_at, created_by, updated_at, updated_by) VALUES ('{overlap}', '{Milestone1OrganisationFixture.FiscalYearId}', '{Milestone1OrganisationFixture.CompanyId}', 13, '2026-06-20', '2026-07-10', 'Open', now(), '{system}', now(), '{system}')");

            (int exitCode, string error) = await RunMigratorAsync(database);

            Assert.Equal(8, exitCode);
            Assert.Contains("PostgreSQL error P0001", error, StringComparison.Ordinal);
            Assert.Contains("overlapping periods", error, StringComparison.Ordinal);
            Assert.Contains("RR-44", error, StringComparison.Ordinal);
            Assert.Equal(0L, await ScalarAsync(database, $"SELECT count(*) FROM kernel.__ef_migrations_history WHERE \"MigrationId\" = '{HardeningMigration}'"));
            Assert.Equal(0L, await ScalarAsync(database, "SELECT count(*) FROM pg_constraint WHERE conname = 'ex_fiscal_period_no_overlap'"));

            // Resolved by the operator, the same run applies the migration: every step is safe to re-run.
            await ExecuteAsync(Migrator(database), $"DELETE FROM kernel.fiscal_period WHERE id = '{overlap}'");
            (int resolvedExitCode, _) = await RunMigratorAsync(database);
            Assert.Equal(0, resolvedExitCode);
            Assert.Equal(1L, await ScalarAsync(database, "SELECT count(*) FROM pg_constraint WHERE conname = 'ex_fiscal_period_no_overlap'"));
        }
        finally
        {
            await _db.DropDatabaseAsync(database, CancellationToken.None);
        }
    }

    private static readonly SemaphoreSlim ConsoleLock = new(1, 1);

    /// <summary>Runs the migrator in process with its error output captured.</summary>
    private static async Task<(int ExitCode, string Error)> RunMigratorAsync(string database)
    {
        await ConsoleLock.WaitAsync(Ct);
        TextWriter original = Console.Error;
        try
        {
            await using StringWriter captured = new();
            Console.SetError(captured);
            int exitCode = await Themis.Migrator.Program.RunAsync(["--connection", Migrator(database)], PostgresFixture.MigratorEnvironment);
            Console.SetError(original);
            return (exitCode, captured.ToString());
        }
        finally
        {
            Console.SetError(original);
            ConsoleLock.Release();
        }
    }

    private static string Migrator(string connectionString) => new NpgsqlConnectionStringBuilder(connectionString) { Username = "themis_migrator", Password = PostgresFixture.TestRolePassword }.ConnectionString;

    private static async Task<object?> ScalarAsync(string connectionString, string sql)
    {
        await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(connectionString, Ct);
        await using NpgsqlCommand command = new(sql, connection);
        return await command.ExecuteScalarAsync(Ct);
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(connectionString, Ct);
        await using NpgsqlCommand command = new(sql, connection);
        await command.ExecuteNonQueryAsync(Ct);
    }

    /// <summary>One line per catalog fact of the kernel and reporting schemas, sorted, so two databases compare as sets.</summary>
    private static async Task<IReadOnlyList<string>> CatalogAsync(string connectionString)
    {
        const string Query =
            """
            SELECT 'schema ' || n.nspname || ' owner=' || pg_get_userbyid(n.nspowner) || ' acl=' || coalesce(n.nspacl::text, 'default')
            FROM pg_namespace n WHERE n.nspname IN ('kernel', 'reporting')
            UNION ALL
            SELECT 'table ' || schemaname || '.' || tablename || ' owner=' || tableowner FROM pg_tables WHERE schemaname IN ('kernel', 'reporting')
            UNION ALL
            SELECT 'column ' || table_schema || '.' || table_name || '.' || column_name || ' ' || data_type
                   || coalesce('(' || character_maximum_length || ')', '') || coalesce('(' || numeric_precision || ',' || numeric_scale || ')', '')
                   || ' null=' || is_nullable || ' default=' || coalesce(column_default, '') || ' position=' || ordinal_position
            FROM information_schema.columns WHERE table_schema IN ('kernel', 'reporting')
            UNION ALL
            SELECT 'constraint ' || n.nspname || '.' || c.relname || ' ' || con.conname || ' ' || pg_get_constraintdef(con.oid) || ' validated=' || con.convalidated || ' deferrable=' || con.condeferrable
            FROM pg_constraint con JOIN pg_class c ON c.oid = con.conrelid JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname IN ('kernel', 'reporting')
            UNION ALL
            SELECT 'index ' || schemaname || '.' || tablename || ' ' || indexdef FROM pg_indexes WHERE schemaname IN ('kernel', 'reporting')
            UNION ALL
            SELECT 'view ' || schemaname || '.' || viewname || ' owner=' || viewowner || ' ' || definition FROM pg_views WHERE schemaname IN ('kernel', 'reporting')
            UNION ALL
            SELECT 'function ' || n.nspname || '.' || p.proname || ' owner=' || pg_get_userbyid(p.proowner) || ' acl=' || coalesce(p.proacl::text, 'default') || ' ' || pg_get_functiondef(p.oid)
            FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
            WHERE n.nspname IN ('kernel', 'reporting') AND NOT EXISTS (SELECT 1 FROM pg_depend d WHERE d.objid = p.oid AND d.deptype = 'e')
            UNION ALL
            SELECT 'trigger ' || n.nspname || '.' || c.relname || ' ' || t.tgname || ' ' || pg_get_triggerdef(t.oid)
            FROM pg_trigger t JOIN pg_class c ON c.oid = t.tgrelid JOIN pg_namespace n ON n.oid = c.relnamespace WHERE NOT t.tgisinternal AND n.nspname IN ('kernel', 'reporting')
            UNION ALL
            SELECT 'grant ' || grantee || ' ' || table_schema || '.' || table_name || ' ' || privilege_type || ' grantable=' || is_grantable
            FROM information_schema.role_table_grants WHERE table_schema IN ('kernel', 'reporting')
            UNION ALL
            SELECT 'column-grant ' || grantee || ' ' || table_schema || '.' || table_name || '.' || column_name || ' ' || privilege_type
            FROM information_schema.role_column_grants WHERE table_schema IN ('kernel', 'reporting')
            UNION ALL
            SELECT 'comment ' || n.nspname || '.' || c.relname || ' ' || obj_description(c.oid, 'pg_class')
            FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname IN ('kernel', 'reporting') AND obj_description(c.oid, 'pg_class') IS NOT NULL
            UNION ALL
            SELECT 'sequence ' || schemaname || '.' || sequencename || ' owner=' || sequenceowner || ' ' || data_type FROM pg_sequences WHERE schemaname IN ('kernel', 'reporting')
            """;
        List<string> lines = [];
        await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(connectionString, Ct);
        await using NpgsqlCommand command = new(Query, connection);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(Ct);
        while (await reader.ReadAsync(Ct))
        {
            lines.Add(NormaliseCheckList(reader.GetString(0)));
        }

        lines.Sort(StringComparer.Ordinal);
        return lines;
    }

    /// <summary>
    /// pg_dump writes a CHECK list as PostgreSQL deparses it, <c>ARRAY[('A'::character varying)::text, ...]</c>, and
    /// re-parsing that text stores the casts per element, while the migration's <c>IN ('A', ...)</c> is stored as one
    /// array cast, <c>(ARRAY['A'::character varying, ...])::text[]</c>. The two are the same constraint; the restored form
    /// is rewritten to the original one so that only real drift remains.
    /// </summary>
    private static string NormaliseCheckList(string line) =>
        line.StartsWith("constraint ", StringComparison.Ordinal)
            ? DumpedCheckList().Replace(line, match => "(ARRAY[" + DumpedCheckElement().Replace(match.Groups[1].Value, "'$1'::character varying") + "])::text[]")
            : line;

    [GeneratedRegex(@"ARRAY\[((?:\('[^']*'::character varying\)::text(?:, )?)+)\]")]
    private static partial Regex DumpedCheckList();

    [GeneratedRegex(@"\('([^']*)'::character varying\)::text")]
    private static partial Regex DumpedCheckElement();

    private static string DriftDirectory()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Themis.sln")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(directory?.FullName ?? throw new InvalidOperationException("Themis.sln not found above the test output directory."), "db", "migrations", "drift");
    }
}

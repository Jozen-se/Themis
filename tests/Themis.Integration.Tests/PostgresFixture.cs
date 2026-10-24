using Npgsql;
using Testcontainers.PostgreSql;
using Themis.Kernel.Organisation;

namespace Themis.Integration.Tests;

/// <summary>
/// One PostgreSQL database per test run with the Themis bootstrap applied from db/bootstrap/01-roles.sql, followed
/// by the fixture module's test-only bootstrap, the kernel migrations and the milestone 1 organisation fixture applied
/// by Themis.Migrator exactly as an operator runs it, and the fixture table. Uses an existing server when THEMIS_TEST_CONNECTION (a superuser
/// connection string to PostgreSQL 16 or later) is set, otherwise a Testcontainers PostgreSQL container.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private const string ConnectionEnvironmentVariable = "THEMIS_TEST_CONNECTION";
    private const string Image = "postgres:18";

    /// <summary>
    /// The test-only placeholder password of the three LOGIN roles. 01-roles.sql sets no password (RR-65), so the
    /// fixture sets this one after every run of it; the psql script db/bootstrap/02-role-passwords.psql, which sets
    /// real values from variables, is proven by CI's compose job instead.
    /// </summary>
    public const string TestRolePassword = "themis";

    /// <summary>The environment every in-process migrator run names, so the fixture guard of RR-70 lets the test fixture load.</summary>
    public const string MigratorEnvironment = "Testing";

    private const string TestRolePasswordsSql =
        "ALTER ROLE themis_migrator PASSWORD '" + TestRolePassword + "'; " +
        "ALTER ROLE themis_app PASSWORD '" + TestRolePassword + "'; " +
        "ALTER ROLE themis_reporting PASSWORD '" + TestRolePassword + "';";

    private PostgreSqlContainer? _container;

    /// <summary>Superuser connection string, used only to set up and inspect the database.</summary>
    public string SuperuserConnectionString { get; private set; } = string.Empty;

    public async ValueTask InitializeAsync()
    {
        string? existing = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(existing))
        {
            SuperuserConnectionString = await CreateDatabaseAsync(existing);
        }
        else
        {
            _container = new PostgreSqlBuilder(Image).WithDatabase("themis").Build();
            await _container.StartAsync();
            SuperuserConnectionString = _container.GetConnectionString();
        }

        await ExecuteAsSuperuserAsync(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "db", "bootstrap", "01-roles.sql")), CancellationToken.None);
        await ExecuteAsSuperuserAsync(TestRolePasswordsSql, CancellationToken.None);
        await ExecuteAsSuperuserAsync(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "fixture-bootstrap.sql")), CancellationToken.None);

        // Only Themis.Migrator applies migrations (brief, Migrations); the tests take the same path.
        MigratorExitCode = await Themis.Migrator.Program.RunAsync(["--connection", ConnectionStringFor("themis_migrator"), "--fixture", "milestone-1"], MigratorEnvironment);
        if (MigratorExitCode != 0)
        {
            throw new InvalidOperationException($"Themis.Migrator exited with {MigratorExitCode}.");
        }

        await ExecuteAsMigratorAsync(FixtureSchema.CreateSql, CancellationToken.None);
    }

    /// <summary>Exit code of the migrator run that prepared the database; 0 when the migrations applied.</summary>
    public int MigratorExitCode { get; private set; } = -1;

    /// <summary>
    /// Removes every row the pipeline tests wrote, the ledgers and caches of step 8 included, through the cleanup seam
    /// (the D-60 triggers refuse even the table owner), so each test class starts from empty tables; the seeded rows stay,
    /// and the seeded number series are wound back to their first number (version 1) since numbering tests advance them.
    /// Since milestone 2 step 3 it also removes the companies, parties, sites, warehouses, charts and role assignments
    /// the organisation command tests create, restores SE01, its party, site, warehouse and chart to the fixture's values
    /// at version 1, and winds the PARTY series back to the number after the fixture's party, not to 1, since the fixture
    /// party took P-000001 from it (D-64).
    /// </summary>
    public async Task TruncateAsync(CancellationToken cancellationToken) =>
        await CleanupAsync(
            "TRUNCATE kernel.idempotency_record, kernel.outbox_event, kernel.setting_value, kernel.journal_line, kernel.journal_entry, kernel.inventory_entry, kernel.stock_balance, kernel.item_cost_state, kernel.document_line, kernel.inventory_adjustment, kernel.document, kernel.item_company, kernel.item, fixture.counter; " +
            "DELETE FROM kernel.audit_event WHERE command_name NOT LIKE 'SYSTEM.%'; " +
            OrganisationCleanupSql +
            "UPDATE kernel.number_series SET next_number = CASE WHEN company_id IS NULL AND document_type = 'PARTY' THEN 2 ELSE 1 END, version = 1", cancellationToken);

    /// <summary>The organisation rows the step 3 tests create, removed, and the fixture's own rows restored (milestone 2 step 3).</summary>
    private static string OrganisationCleanupSql
    {
        get
        {
            Guid company = Milestone1OrganisationFixture.CompanyId;
            Guid party = Milestone1OrganisationFixture.CompanyPartyId;
            Guid chart = Milestone1OrganisationFixture.ChartOfAccountsId;
            Guid site = Milestone1OrganisationFixture.SiteId;
            Guid warehouse = Milestone1OrganisationFixture.WarehouseId;
            string legalName = Milestone1OrganisationFixture.LegalName.Replace("'", "''", StringComparison.Ordinal);
            string organisationNumber = Milestone1OrganisationFixture.OrganisationNumber;
            string vatNumber = Milestone1OrganisationFixture.VatNumber;
            string normalised = DuplicateKeys.Normalise(organisationNumber);
            string duplicateKey = DuplicateKeys.ForParty(organisationNumber, vatNumber, Milestone1OrganisationFixture.LegalName, postalCode: null);
            return
                $"DELETE FROM kernel.role_assignment WHERE company_id <> '{company}'; " +
                $"DELETE FROM kernel.number_series WHERE company_id IS NOT NULL AND company_id <> '{company}'; " +
                $"DELETE FROM kernel.posting_profile_rule WHERE company_id <> '{company}'; " +
                $"DELETE FROM kernel.account WHERE chart_id <> '{chart}'; " +
                $"DELETE FROM kernel.fiscal_period WHERE company_id <> '{company}'; " +
                $"DELETE FROM kernel.fiscal_year WHERE company_id <> '{company}'; " +
                $"DELETE FROM kernel.warehouse WHERE id <> '{warehouse}'; " +
                $"DELETE FROM kernel.site WHERE id <> '{site}'; " +
                $"DELETE FROM kernel.company WHERE id <> '{company}'; " +
                $"DELETE FROM kernel.party WHERE id <> '{party}'; " +
                $"DELETE FROM kernel.chart_of_accounts WHERE id <> '{chart}'; " +
                $"UPDATE kernel.company SET legal_name = '{legalName}', organisation_number = '{organisationNumber}', organisation_number_normalised = '{normalised}', vat_number = '{vatNumber}', country_code = 'SE', base_currency = 'SEK', time_zone = 'Europe/Stockholm', fiscal_year_start_month = 1, chart_of_accounts_id = '{chart}', rounding_mode = 'HalfAwayFromZero', tax_rounding_level = 'Line', status = 'Active', version = 1 WHERE id = '{company}'; " +
                $"UPDATE kernel.party SET name = '{legalName}', organisation_number = '{organisationNumber}', vat_number = '{vatNumber}', country_code = 'SE', duplicate_key = '{duplicateKey}', status = 'Active', version = 1 WHERE id = '{party}'; " +
                $"UPDATE kernel.site SET name = 'Main site', time_zone = NULL, version = 1 WHERE id = '{site}'; " +
                $"UPDATE kernel.warehouse SET name = 'Main warehouse', is_default = true, status = 'Active', version = 1 WHERE id = '{warehouse}'; " +
                $"UPDATE kernel.chart_of_accounts SET name = 'BAS-kontoplan', template = 'BAS 2026', version = 1 WHERE id = '{chart}'; ";
        }
    }

    /// <summary>
    /// The test cleanup seam of milestone 2 step 2b (docs/development/test-seams.md; D-60): runs the statements as the
    /// superuser in one explicit transaction after <c>SET LOCAL session_replication_role = replica</c>, so the D-60
    /// triggers, which a superuser stays outside of, do not fire on the test's own cleanup. Foreign key triggers do not
    /// fire either, so the statements must keep the references whole themselves. Production code never sets
    /// session_replication_role (an architecture test checks src), and no role but a superuser may (pg_parameter_acl).
    /// </summary>
    public async Task CleanupAsync(string sql, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await OpenAsync(SuperuserConnectionString, cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (NpgsqlCommand replica = new("SET LOCAL session_replication_role = replica", connection, transaction))
        {
            await replica.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (NpgsqlCommand command = new(sql, connection, transaction))
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>A database with nothing in it, not even the bootstrap, for the drift test that restores a schema dump into it (RR-39); the caller drops it.</summary>
    public async Task<string> CreateBareDatabaseAsync() => await CreateDatabaseAsync(SuperuserConnectionString);

    /// <summary>A second, empty database on the same server for a test that needs the migrator's work from scratch; the caller drops it.</summary>
    public async Task<string> CreateEmptyDatabaseAsync(CancellationToken cancellationToken)
    {
        string connectionString = await CreateDatabaseAsync(SuperuserConnectionString);
        await using NpgsqlConnection connection = await OpenAsync(connectionString, cancellationToken);
        await using NpgsqlCommand bootstrap = new(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "db", "bootstrap", "01-roles.sql"), cancellationToken), connection);
        await bootstrap.ExecuteNonQueryAsync(cancellationToken);
        await using NpgsqlCommand passwords = new(TestRolePasswordsSql, connection);
        await passwords.ExecuteNonQueryAsync(cancellationToken);
        return connectionString;
    }

    /// <summary>
    /// A second database on the same server, bootstrapped, migrated and seeded with the milestone 1 fixture exactly as
    /// this fixture's own, to serve as the template of the property-based runs (step 11); the caller drops it.
    /// </summary>
    public async Task<string> CreateSeededDatabaseAsync(CancellationToken cancellationToken)
    {
        string connectionString = await CreateEmptyDatabaseAsync(cancellationToken);
        string migrator = new NpgsqlConnectionStringBuilder(connectionString) { Username = "themis_migrator", Password = TestRolePassword }.ConnectionString;
        int exitCode = await Themis.Migrator.Program.RunAsync(["--connection", migrator, "--fixture", "milestone-1"], MigratorEnvironment);
        if (exitCode != 0)
        {
            throw new InvalidOperationException($"Themis.Migrator exited with {exitCode} on {new NpgsqlConnectionStringBuilder(connectionString).Database}.");
        }

        return connectionString;
    }

    /// <summary>
    /// A fresh database cloned from a template with <c>CREATE DATABASE ... TEMPLATE</c> (step 11: every property
    /// sequence starts from a clean clone). PostgreSQL refuses the clone while any other session is connected to the
    /// template, so the pooled connections are released first and any remaining session on it is ended. Clones are
    /// created one at a time; the caller drops the clone.
    /// </summary>
    public async Task<string> CloneDatabaseAsync(string templateConnectionString, CancellationToken cancellationToken)
    {
        string template = new NpgsqlConnectionStringBuilder(templateConnectionString).Database!;
        string clone = "themis_clone_" + Guid.NewGuid().ToString("N")[..12];
        await CloneLock.WaitAsync(cancellationToken);
        try
        {
            NpgsqlConnection.ClearAllPools();
            await using NpgsqlConnection connection = await OpenAsync(SuperuserConnectionString, cancellationToken);
            await using (NpgsqlCommand terminate = new("SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = @template AND pid <> pg_backend_pid()", connection))
            {
                terminate.Parameters.AddWithValue("template", template);
                await terminate.ExecuteNonQueryAsync(cancellationToken);
            }

            await using NpgsqlCommand create = new($"CREATE DATABASE \"{clone}\" TEMPLATE \"{template}\"", connection);
            await create.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            CloneLock.Release();
        }

        return new NpgsqlConnectionStringBuilder(SuperuserConnectionString) { Database = clone }.ConnectionString;
    }

    private static readonly SemaphoreSlim CloneLock = new(1, 1);

    public async Task DropDatabaseAsync(string connectionString, CancellationToken cancellationToken)
    {
        string name = new NpgsqlConnectionStringBuilder(connectionString).Database!;
        NpgsqlConnection.ClearAllPools();
        await ExecuteAsSuperuserAsync($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)", cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    /// <summary>Connection string for a login role against the test database (the test-only placeholder password).</summary>
    public string ConnectionStringFor(string role) =>
        new NpgsqlConnectionStringBuilder(SuperuserConnectionString) { Username = role, Password = TestRolePassword }.ConnectionString;

    public static async Task<NpgsqlConnection> OpenAsync(string connectionString, CancellationToken cancellationToken)
    {
        NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    public async Task ExecuteAsSuperuserAsync(string sql, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await OpenAsync(SuperuserConnectionString, cancellationToken);
        await using NpgsqlCommand command = new(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Runs DDL the way a migration does: as themis_migrator, so that ownership and grants are real.</summary>
    public async Task ExecuteAsMigratorAsync(string sql, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await OpenAsync(ConnectionStringFor("themis_migrator"), cancellationToken);
        await using NpgsqlCommand command = new(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<string> CreateDatabaseAsync(string superuserConnectionString)
    {
        string databaseName = "themis_test_" + Guid.NewGuid().ToString("N")[..12];
        await using (NpgsqlConnection connection = new(superuserConnectionString))
        {
            await connection.OpenAsync();
            await using NpgsqlCommand command = new($"CREATE DATABASE \"{databaseName}\"", connection);
            await command.ExecuteNonQueryAsync();
        }

        return new NpgsqlConnectionStringBuilder(superuserConnectionString) { Database = databaseName }.ConnectionString;
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresTestGroup : ICollectionFixture<PostgresFixture>
{
    public const string Name = "PostgreSQL";
}

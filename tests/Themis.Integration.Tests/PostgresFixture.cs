using Npgsql;
using Testcontainers.PostgreSql;

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
        await ExecuteAsSuperuserAsync(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "fixture-bootstrap.sql")), CancellationToken.None);

        // Only Themis.Migrator applies migrations (brief, Migrations); the tests take the same path.
        MigratorExitCode = await Themis.Migrator.Program.Main(["--connection", ConnectionStringFor("themis_migrator"), "--fixture", "milestone-1"]);
        if (MigratorExitCode != 0)
        {
            throw new InvalidOperationException($"Themis.Migrator exited with {MigratorExitCode}.");
        }

        await ExecuteAsMigratorAsync(FixtureSchema.CreateSql, CancellationToken.None);
    }

    /// <summary>Exit code of the migrator run that prepared the database; 0 when the migrations applied.</summary>
    public int MigratorExitCode { get; private set; } = -1;

    /// <summary>
    /// Removes every row the pipeline tests wrote, the ledgers and caches of step 8 included (the migrator, not the kernel role, may truncate them), so each test class starts from empty tables; the seeded rows stay,
    /// and the seeded number series are wound back to their first number (version 1) since numbering tests advance them.
    /// </summary>
    public async Task TruncateAsync(CancellationToken cancellationToken) =>
        await ExecuteAsMigratorAsync(
            "TRUNCATE kernel.idempotency_record, kernel.outbox_event, kernel.journal_line, kernel.journal_entry, kernel.inventory_entry, kernel.stock_balance, kernel.item_cost_state, kernel.document_line, kernel.inventory_adjustment, kernel.document, kernel.item_company, kernel.item, fixture.counter; " +
            "DELETE FROM kernel.audit_event WHERE command_name NOT LIKE 'SYSTEM.%'; " +
            "UPDATE kernel.number_series SET next_number = 1, version = 1", cancellationToken);

    /// <summary>A database with nothing in it, not even the bootstrap, for the drift test that restores a schema dump into it (RR-39); the caller drops it.</summary>
    public async Task<string> CreateBareDatabaseAsync() => await CreateDatabaseAsync(SuperuserConnectionString);

    /// <summary>A second, empty database on the same server for a test that needs the migrator's work from scratch; the caller drops it.</summary>
    public async Task<string> CreateEmptyDatabaseAsync(CancellationToken cancellationToken)
    {
        string connectionString = await CreateDatabaseAsync(SuperuserConnectionString);
        await using NpgsqlConnection connection = await OpenAsync(connectionString, cancellationToken);
        await using NpgsqlCommand bootstrap = new(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "db", "bootstrap", "01-roles.sql"), cancellationToken), connection);
        await bootstrap.ExecuteNonQueryAsync(cancellationToken);
        return connectionString;
    }

    /// <summary>
    /// A second database on the same server, bootstrapped, migrated and seeded with the milestone 1 fixture exactly as
    /// this fixture's own, to serve as the template of the property-based runs (step 11); the caller drops it.
    /// </summary>
    public async Task<string> CreateSeededDatabaseAsync(CancellationToken cancellationToken)
    {
        string connectionString = await CreateEmptyDatabaseAsync(cancellationToken);
        string migrator = new NpgsqlConnectionStringBuilder(connectionString) { Username = "themis_migrator", Password = "themis" }.ConnectionString;
        int exitCode = await Themis.Migrator.Program.Main(["--connection", migrator, "--fixture", "milestone-1"]);
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

    /// <summary>Connection string for a login role against the test database (development password).</summary>
    public string ConnectionStringFor(string role) =>
        new NpgsqlConnectionStringBuilder(SuperuserConnectionString) { Username = role, Password = "themis" }.ConnectionString;

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

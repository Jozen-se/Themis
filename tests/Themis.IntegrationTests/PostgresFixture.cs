using Npgsql;
using Testcontainers.PostgreSql;

namespace Themis.IntegrationTests;

/// <summary>
/// One PostgreSQL server per test run with the Themis role model applied from db/init/01-roles.sql.
/// Uses an existing server when THEMIS_TEST_CONNECTION (a superuser connection string) is set, otherwise a
/// Testcontainers PostgreSQL container.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private const string ConnectionEnvironmentVariable = "THEMIS_TEST_CONNECTION";
    private const string Image = "postgres:17";

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

        await ApplyRoleScriptAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    /// <summary>Connection string for <paramref name="role"/> against the test database.</summary>
    public string ConnectionStringFor(string role, string password = "themis")
    {
        NpgsqlConnectionStringBuilder builder = new(SuperuserConnectionString)
        {
            Username = role,
            Password = password,
        };
        return builder.ConnectionString;
    }

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

    private async Task ApplyRoleScriptAsync()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "db", "init", "01-roles.sql");
        string script = await File.ReadAllTextAsync(path);
        await ExecuteAsSuperuserAsync(script, CancellationToken.None);
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresTestGroup : ICollectionFixture<PostgresFixture>
{
    public const string Name = "PostgreSQL";
}

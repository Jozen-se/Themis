using Npgsql;

namespace Themis.Migrator;

/// <summary>
/// Applies the kernel migrations as themis_migrator. Connection string: argument <c>--connection</c> or the
/// environment variable <c>THEMIS_MIGRATOR_CONNECTION</c>. Exit codes: 0 up to date, 1 usage error,
/// 2 connected as the wrong role, 3 database unreachable.
/// </summary>
public static class Program
{
    private const string ConnectionVariable = "THEMIS_MIGRATOR_CONNECTION";
    private const string RequiredRole = "themis_migrator";

    public static async Task<int> Main(string[] args)
    {
        string? connectionString = ReadConnectionString(args);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.Error.WriteLine($"Usage: themis-migrator --connection \"<connection string>\" (or set {ConnectionVariable}).");
            return 1;
        }

        try
        {
            await using NpgsqlConnection connection = new(connectionString);
            await connection.OpenAsync();

            await using (NpgsqlCommand whoAmI = new("SELECT current_user", connection))
            {
                string? role = (string?)await whoAmI.ExecuteScalarAsync();
                if (!string.Equals(role, RequiredRole, StringComparison.Ordinal))
                {
                    Console.Error.WriteLine($"Connected as '{role}'. Migrations run only as '{RequiredRole}' (D-41).");
                    return 2;
                }
            }

            await using (NpgsqlCommand schemas = new("SELECT string_agg(nspname, ', ' ORDER BY nspname) FROM pg_namespace WHERE nspname IN ('kernel', 'reporting')", connection))
            {
                string? found = (string?)await schemas.ExecuteScalarAsync();
                Console.WriteLine($"Connected to '{connection.Database}' as {RequiredRole}. Bootstrapped schemas: {found ?? "none"}.");
            }

            Console.WriteLine("No migrations are defined yet; the kernel data context arrives with milestone 1 step 2. Nothing to apply.");
            return 0;
        }
        catch (NpgsqlException exception)
        {
            Console.Error.WriteLine($"PostgreSQL is not reachable: {exception.Message}");
            return 3;
        }
    }

    private static string? ReadConnectionString(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--connection", StringComparison.Ordinal))
            {
                return args[i + 1];
            }
        }

        return Environment.GetEnvironmentVariable(ConnectionVariable);
    }
}

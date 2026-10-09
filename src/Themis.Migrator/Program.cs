using Npgsql;
using Themis.Kernel.Contracts.Organisation;
using Themis.Kernel.Contracts.Security;
using Themis.Kernel.Organisation;
using Themis.Kernel.Persistence;
using Themis.Kernel.Persistence.Seeding;

namespace Themis.Migrator;

/// <summary>
/// Applies the kernel migrations as themis_migrator, and on request seeds a named fixture. Connection string:
/// argument <c>--connection</c> or the environment variable <c>THEMIS_MIGRATOR_CONNECTION</c>. Fixture: argument
/// <c>--fixture milestone-1</c> or the environment variable <c>THEMIS_MIGRATOR_FIXTURE</c>; the milestone 1
/// organisation fixture (brief, step 3) is seeded idempotently after the migrations. Exit codes: 0 up to date,
/// 1 usage error, 2 connected as the wrong role, 3 database unreachable.
/// </summary>
public static class Program
{
    private const string ConnectionVariable = "THEMIS_MIGRATOR_CONNECTION";
    private const string FixtureVariable = "THEMIS_MIGRATOR_FIXTURE";
    private const string RequiredRole = "themis_migrator";
    private const string Milestone1Fixture = "milestone-1";

    public static async Task<int> Main(string[] args)
    {
        string? connectionString = ReadOption(args, "--connection", ConnectionVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.Error.WriteLine($"Usage: themis-migrator --connection \"<connection string>\" [--fixture {Milestone1Fixture}] (or set {ConnectionVariable} and {FixtureVariable}).");
            return 1;
        }

        string? fixture = ReadOption(args, "--fixture", FixtureVariable);
        if (!string.IsNullOrWhiteSpace(fixture) && !string.Equals(fixture, Milestone1Fixture, StringComparison.Ordinal))
        {
            Console.Error.WriteLine($"Unknown fixture '{fixture}'. The only fixture is '{Milestone1Fixture}'.");
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

            // Only Themis.Migrator applies migrations; the application never calls Migrate() (brief, Migrations).
            IReadOnlyList<string> pending = await KernelMigrations.PendingAsync(connectionString, CancellationToken.None);
            if (pending.Count == 0)
            {
                Console.WriteLine("Kernel schema is up to date. Nothing to apply.");
            }
            else
            {
                Console.WriteLine($"Applying {pending.Count} kernel migration(s): {string.Join(", ", pending)}.");
                IReadOnlyList<string> applied = await KernelMigrations.ApplyAsync(connectionString, CancellationToken.None);
                Console.WriteLine($"Applied {applied.Count} migration(s) as {RequiredRole}.");
            }

            if (!string.IsNullOrWhiteSpace(fixture))
            {
                // The milestone 1 organisation fixture (brief, step 3), written by the migrator only and idempotent.
                OrganisationFixtureDefinition definition = Milestone1OrganisationFixture.Definition;
                int inserted = await OrganisationFixtureSeeder.ApplyAsync(connectionString, definition, TimeProvider.System.GetUtcNow(), WellKnownUsers.System, CancellationToken.None);
                Console.WriteLine($"Fixture '{fixture}': {inserted} row(s) inserted, the rest already present.");
            }

            return 0;
        }
        catch (NpgsqlException exception)
        {
            Console.Error.WriteLine($"PostgreSQL is not reachable: {exception.Message}");
            return 3;
        }
    }

    private static string? ReadOption(string[] args, string option, string environmentVariable)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], option, StringComparison.Ordinal))
            {
                return args[i + 1];
            }
        }

        return Environment.GetEnvironmentVariable(environmentVariable);
    }
}

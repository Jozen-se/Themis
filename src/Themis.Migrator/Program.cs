using Npgsql;
using Themis.Kernel.Contracts.Documents;
using Themis.Kernel.Contracts.Identity;
using Themis.Kernel.Contracts.Organisation;
using Themis.Kernel.Contracts.Posting;
using Themis.Kernel.Contracts.Security;
using Themis.Kernel.Documents;
using Themis.Kernel.Identity;
using Themis.Kernel.MasterData;
using Themis.Kernel.Organisation;
using Themis.Kernel.Persistence;
using Themis.Kernel.Persistence.Seeding;
using Themis.Kernel.Posting;
using Themis.Kernel.Primitives;
using Themis.Kernel.Settings;

namespace Themis.Migrator;

/// <summary>
/// Applies the kernel migrations as themis_migrator, seeds the system catalogues (permissions, the document type
/// registry) and, on request, a named fixture. Connection string: argument <c>--connection</c> or the environment
/// variable <c>THEMIS_MIGRATOR_CONNECTION</c>. Fixture: argument <c>--fixture milestone-1</c> or the environment
/// variable <c>THEMIS_MIGRATOR_FIXTURE</c>; the milestone 1 fixtures (brief, steps 3 to 7) are seeded idempotently
/// after the migrations. Exit codes: 0 up to date, 1 usage error, 2 connected as the wrong role, 3 database
/// unreachable, 4 the fixture violates segregation of duties (R-SEC-02), 5 the fixture violates the posting profile
/// rules (R-PP-04, R-PP-06, or an account outside the company's chart or not Active), 6 the database holds outbox
/// rows written before the event envelope of D-50 and must be recreated before the step 7 migration, 7 the
/// document type catalogue in code breaks a registry rule, 8 PostgreSQL refused a statement (reported with its
/// SQLSTATE and message, RR-48). A run is not one transaction: each migration and each seeder commits on its own,
/// and every step is safe to re-run. The settings catalogue (D-58) is written on every run; a stored value outside a
/// tightened range and a definition that left the catalogue are printed as warnings and never changed.
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
                // D-50: the envelope migration adds NOT NULL columns to kernel.outbox_event that existing rows cannot carry; the
                // migration itself stops too, but this check gives the owner the message before anything is applied.
                if (pending.Any(name => name.EndsWith(EnvelopeMigration, StringComparison.Ordinal)) && await OutboxHoldsRowsAsync(connection))
                {
                    Console.Error.WriteLine("kernel.outbox_event holds rows written before the D-50 event envelope. Recreate the database before running Themis.Migrator (step 7 brief); development and test databases hold nothing worth keeping before milestone 1 is done.");
                    return 6;
                }

                Console.WriteLine($"Applying {pending.Count} kernel migration(s): {string.Join(", ", pending)}.");
                IReadOnlyList<string> applied = await KernelMigrations.ApplyAsync(connectionString, CancellationToken.None);
                Console.WriteLine($"Applied {applied.Count} migration(s) as {RequiredRole}.");
            }

            DateTimeOffset now = TimeProvider.System.GetUtcNow();

            // The permission catalogue is system data (Phase 0 Specification, Security entities): seeded on every run.
            int catalogued = await IdentitySeeder.ApplyCatalogueAsync(connectionString, PermissionCatalog.Definition, now, WellKnownUsers.System, CancellationToken.None);
            Console.WriteLine($"Permission catalogue: {catalogued} row(s) inserted or updated, the rest already present.");

            // D-40: the document type registry is system data too, written from the catalogue in code on every run.
            Result<int> registry = await DocumentTypeSeeder.ApplyCatalogueAsync(connectionString, DocumentTypes.All, now, WellKnownUsers.System, CancellationToken.None);
            if (registry.IsFailure)
            {
                Console.Error.WriteLine($"Document type registry refused: {string.Join(" ", registry.Errors.Select(error => error.Message))}");
                return 7;
            }

            Console.WriteLine($"Document type registry: {registry.Value} row(s) inserted or updated, the rest already present.");

            // D-58: the settings catalogue is system data too, written from the code catalogue on every run; stored values a
            // tightened range left behind and definitions that left the catalogue are reported, never changed or deleted.
            SettingCatalogReport settings = await SettingDefinitionSeeder.ApplyCatalogueAsync(connectionString, SettingCatalog.Definitions, SettingValueFormat.IsValid, now, WellKnownUsers.System, CancellationToken.None);
            Console.WriteLine($"Settings catalogue: {settings.Changed} row(s) inserted or updated, {settings.Retired} retired, the rest already present.");
            foreach (string warning in settings.Warnings)
            {
                Console.WriteLine($"Settings catalogue warning: {warning}");
            }

            if (!string.IsNullOrWhiteSpace(fixture))
            {
                // The milestone 1 fixtures (brief, steps 3 and 4), written by the migrator only and idempotent.
                OrganisationFixtureDefinition organisation = Milestone1OrganisationFixture.Definition;
                int inserted = await OrganisationFixtureSeeder.ApplyAsync(connectionString, organisation, now, WellKnownUsers.System, CancellationToken.None);
                Console.WriteLine($"Fixture '{fixture}' (organisation): {inserted} row(s) inserted, the rest already present.");

                // R-SEC-02: a role or an assignment that would give one user both permissions of a conflict pair is refused.
                IdentityFixtureDefinition identity = Milestone1IdentityFixture.For(organisation.Company.Id);
                Result<int> identityRows = await IdentitySeeder.ApplyFixtureAsync(connectionString, identity, definition => SegregationOfDuties.CheckFixture(definition, PermissionCatalog.Definition.Conflicts), now, WellKnownUsers.System, CancellationToken.None);
                if (identityRows.IsFailure)
                {
                    Console.Error.WriteLine($"Fixture '{fixture}' refused: {identityRows.Primary}");
                    return 4;
                }

                Console.WriteLine($"Fixture '{fixture}' (identity): {identityRows.Value} row(s) inserted, the rest already present.");

                // Step 5: the item posting group is company configuration, fixture-seeded; units and currencies came with the migration.
                int masterDataRows = await MasterDataFixtureSeeder.ApplyAsync(connectionString, Milestone1MasterDataFixture.Definition, now, WellKnownUsers.System, CancellationToken.None);
                Console.WriteLine($"Fixture '{fixture}' (master data): {masterDataRows} row(s) inserted, the rest already present.");

                // Step 6: accounts and posting profile rules are company configuration, fixture-seeded after the rule check
                // (account in the company's chart, Active, R-PP-04, R-PP-06); the database constraints are the second line of defence.
                GeneralLedgerFixtureDefinition generalLedger = Milestone1GeneralLedgerFixture.For(organisation.Company.Id, organisation.Company.ChartOfAccountsId);
                Dictionary<Guid, Guid> chartByCompany = new() { [organisation.Company.Id] = organisation.Company.ChartOfAccountsId };
                Result<int> ledgerRows = await GeneralLedgerFixtureSeeder.ApplyAsync(connectionString, generalLedger, definition => PostingProfileValidator.CheckFixture(definition, chartByCompany), now, WellKnownUsers.System, CancellationToken.None);
                if (ledgerRows.IsFailure)
                {
                    Console.Error.WriteLine($"Fixture '{fixture}' refused: {ledgerRows.Primary}");
                    return 5;
                }

                Console.WriteLine($"Fixture '{fixture}' (general ledger): {ledgerRows.Value} row(s) inserted, the rest already present.");

                // Step 7: number series are company configuration, fixture-seeded (IA- continuous, JE2026- per fiscal year, gapless).
                int seriesRows = await NumberSeriesFixtureSeeder.ApplyAsync(connectionString, Milestone1NumberSeriesFixture.For(organisation.Company.Id, organisation.FiscalYear.Id), now, WellKnownUsers.System, CancellationToken.None);
                Console.WriteLine($"Fixture '{fixture}' (number series): {seriesRows} row(s) inserted, the rest already present.");
            }

            return 0;
        }
        catch (Exception exception) when (FindPostgresError(exception) is { } postgres)
        {
            // RR-48: a database error is reported with its SQLSTATE and message under its own exit code; the run is not one
            // transaction, every step is safe to re-run (docs/development.md, Migrator and host).
            Console.Error.WriteLine($"PostgreSQL error {postgres.SqlState}: {postgres.MessageText}{(postgres.Detail is { } detail ? $" {detail}" : string.Empty)}");
            return 8;
        }
        catch (NpgsqlException exception)
        {
            Console.Error.WriteLine($"PostgreSQL is not reachable: {exception.Message}");
            return 3;
        }
    }

    /// <summary>The PostgreSQL error in the chain, also when Entity Framework wraps it (RR-40, RR-48).</summary>
    private static PostgresException? FindPostgresError(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres)
            {
                return postgres;
            }
        }

        return null;
    }

    /// <summary>The name of the step 7 migration that adds the D-50 envelope columns.</summary>
    private const string EnvelopeMigration = "_DocumentFrameworkTables";

    private static async Task<bool> OutboxHoldsRowsAsync(NpgsqlConnection connection)
    {
        // A fresh database has no outbox table yet; the existence check runs first, since PostgreSQL parses a subquery before a CASE guards it.
        await using NpgsqlCommand exists = new("SELECT to_regclass('kernel.outbox_event') IS NOT NULL", connection);
        if (await exists.ExecuteScalarAsync() is not true)
        {
            return false;
        }

        await using NpgsqlCommand rows = new("SELECT EXISTS (SELECT 1 FROM kernel.outbox_event)", connection);
        return await rows.ExecuteScalarAsync() is true;
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

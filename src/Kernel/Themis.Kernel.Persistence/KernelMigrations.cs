using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Themis.Kernel.Persistence;

/// <summary>
/// Applies the kernel migrations. Only Themis.Migrator calls this, with the themis_migrator connection string; the
/// application never calls Migrate() (brief, Migrations). The script generation serves the golden migration scripts
/// of RR-39: a migration's SQL is fixed once merged, and the test compares every migration's generated script with
/// the committed one.
/// </summary>
internal static class KernelMigrations
{
    public static async Task<IReadOnlyList<string>> PendingAsync(string migratorConnectionString, CancellationToken cancellationToken)
    {
        await using KernelDbContext context = KernelDbContextFactory.ForMigrations(migratorConnectionString);
        return [.. await context.Database.GetPendingMigrationsAsync(cancellationToken)];
    }

    public static async Task<IReadOnlyList<string>> ApplyAsync(string migratorConnectionString, CancellationToken cancellationToken)
    {
        await using KernelDbContext context = KernelDbContextFactory.ForMigrations(migratorConnectionString);
        List<string> pending = [.. await context.Database.GetPendingMigrationsAsync(cancellationToken)];
        await context.Database.MigrateAsync(cancellationToken);
        return pending;
    }

    /// <summary>Applies the migrations up to and including the named one (tests only: a database at an earlier step, RR-44).</summary>
    public static async Task ApplyUpToAsync(string migratorConnectionString, string targetMigration, CancellationToken cancellationToken)
    {
        await using KernelDbContext context = KernelDbContextFactory.ForMigrations(migratorConnectionString);
        await context.Database.MigrateAsync(targetMigration, cancellationToken);
    }

    /// <summary>The ids of every migration the assembly holds, in order; needs no database.</summary>
    public static IReadOnlyList<string> All()
    {
        using KernelDbContext context = KernelDbContextFactory.ForMigrations(DesignTimeConnectionString);
        return [.. context.Database.GetMigrations()];
    }

    /// <summary>
    /// The SQL of one migration on its own, from the migration before it (none for the first) to it, not idempotent
    /// and without a database (RR-39): what the migrator would send when applying exactly that step.
    /// </summary>
    public static string GenerateScript(string? fromMigration, string toMigration)
    {
        using KernelDbContext context = KernelDbContextFactory.ForMigrations(DesignTimeConnectionString);
        IMigrator migrator = context.GetService<IMigrator>();
        return migrator.GenerateScript(fromMigration ?? Migration.InitialDatabase, toMigration, MigrationsSqlGenerationOptions.Default);
    }

    private const string DesignTimeConnectionString = "Host=localhost;Database=themis;Username=themis_migrator;Password=design-time";
}

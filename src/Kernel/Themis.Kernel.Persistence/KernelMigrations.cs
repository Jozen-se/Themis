using Microsoft.EntityFrameworkCore;

namespace Themis.Kernel.Persistence;

/// <summary>
/// Applies the kernel migrations. Only Themis.Migrator calls this, with the themis_migrator connection string; the
/// application never calls Migrate() (brief, Migrations).
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
}

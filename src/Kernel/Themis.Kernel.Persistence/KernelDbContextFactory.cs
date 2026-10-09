using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Themis.Kernel.Contracts.Persistence;
using Themis.Kernel.Persistence.UnitOfWork;

namespace Themis.Kernel.Persistence;

/// <summary>Builds the kernel data context on the command's shared connection and transaction, with the role interceptor (D-41).</summary>
internal static class KernelDbContextFactory
{
    public static KernelDbContext OnUnitOfWork(IUnitOfWork unitOfWork)
    {
        if (unitOfWork is not NpgsqlUnitOfWork npgsqlUnitOfWork)
        {
            throw new ArgumentException("The kernel data context enlists only in the Npgsql unit of work.", nameof(unitOfWork));
        }

        DbContextOptions<KernelDbContext> options = new DbContextOptionsBuilder<KernelDbContext>()
            .UseNpgsql(npgsqlUnitOfWork.Connection, npgsql => npgsql.MigrationsHistoryTable(KernelDbContext.MigrationsHistoryTable, KernelDbContext.Schema))
            .AddInterceptors(new RoleSwitchInterceptor(npgsqlUnitOfWork, DatabaseRole.Kernel))
            .Options;

        KernelDbContext context = new(options);
        context.Database.UseTransaction(npgsqlUnitOfWork.Transaction);
        return context;
    }

    /// <summary>The context Themis.Migrator uses: its own connection as themis_migrator, no role switching.</summary>
    public static KernelDbContext ForMigrations(string migratorConnectionString)
    {
        DbContextOptions<KernelDbContext> options = new DbContextOptionsBuilder<KernelDbContext>()
            .UseNpgsql(migratorConnectionString, npgsql => npgsql.MigrationsHistoryTable(KernelDbContext.MigrationsHistoryTable, KernelDbContext.Schema))
            .Options;
        return new KernelDbContext(options);
    }
}

/// <summary>Lets the EF tooling (dotnet ef migrations add) build the context without a database.</summary>
internal sealed class KernelDbContextDesignTimeFactory : IDesignTimeDbContextFactory<KernelDbContext>
{
    public KernelDbContext CreateDbContext(string[] args) =>
        KernelDbContextFactory.ForMigrations("Host=localhost;Database=themis;Username=themis_migrator;Password=design-time");
}

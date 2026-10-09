using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Themis.Kernel.Contracts.Persistence;
using Themis.Kernel.Persistence.Stores;
using Themis.Kernel.Persistence.UnitOfWork;

namespace Themis.Kernel.Persistence;

/// <summary>Registers the application data source (themis_app), the unit of work factory and the kernel stores. Called by the host only.</summary>
internal static class KernelPersistenceServices
{
    public static IServiceCollection AddKernelPersistence(this IServiceCollection services, string applicationConnectionString)
    {
        services.AddSingleton(_ => NpgsqlDataSource.Create(applicationConnectionString));
        services.AddSingleton<IUnitOfWorkFactory, UnitOfWorkFactory>();
        services.AddSingleton<IIdempotencyStore, KernelIdempotencyStore>();
        services.AddSingleton<IAuditStore, KernelAuditStore>();
        services.AddSingleton<IOutboxStore, KernelOutboxStore>();
        return services;
    }
}

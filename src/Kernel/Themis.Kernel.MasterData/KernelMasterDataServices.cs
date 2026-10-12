using Microsoft.Extensions.DependencyInjection;
using Themis.Kernel.Contracts.Commands;
using Themis.Kernel.Contracts.MasterData;
using Themis.Kernel.Contracts.Queries;

namespace Themis.Kernel.MasterData;

/// <summary>Registers ITEM.CREATE and the item queries. Called by the composition root only.</summary>
internal static class KernelMasterDataServices
{
    public static IServiceCollection AddKernelMasterData(this IServiceCollection services)
    {
        services.AddScoped<ICommandHandler<CreateItem, ItemView>, CreateItemHandler>();
        services.AddSingleton<ICommandValidator<CreateItem>, CreateItemValidator>();
        services.AddScoped<IQueryHandler<GetItem, ItemView>, GetItemHandler>();
        services.AddScoped<IQueryHandler<GetItemByNumber, ItemView>, GetItemByNumberHandler>();
        return services;
    }
}

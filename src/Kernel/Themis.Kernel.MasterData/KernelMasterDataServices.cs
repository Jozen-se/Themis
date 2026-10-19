using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
        // RR-45: the unique index behind R-MD-05, so a race past the handler's check is the rule's rejection, not a 500.
        services.AddSingleton(new UniqueConstraintRule(CreateItemHandler.ItemNumberIndex, "R-MD-05", CreateItemHandler.ItemNumberDuplicateCode, "The item number already exists."));
        // The test seam of RR-45; the integration tests register theirs first (docs/development/test-seams.md).
        services.TryAddSingleton<IItemCreateSeam, NoItemCreateSeam>();
        services.AddScoped<IQueryHandler<GetItem, ItemView>, GetItemHandler>();
        services.AddScoped<IQueryHandler<GetItemByNumber, ItemView>, GetItemByNumberHandler>();
        return services;
    }
}

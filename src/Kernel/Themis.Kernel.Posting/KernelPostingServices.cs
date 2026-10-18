using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Themis.Kernel.Contracts.Posting;

namespace Themis.Kernel.Posting;

/// <summary>Registers the account resolver of step 6 and the posting service of step 8 (R-LED-07). Called by the composition root only; the integration tests register their own <see cref="IPostingHook"/> first (Build Brief, Test seams).</summary>
internal static class KernelPostingServices
{
    public static IServiceCollection AddKernelPosting(this IServiceCollection services)
    {
        services.AddSingleton<IAccountResolver, PostingProfileAccountResolver>();
        services.TryAddSingleton<IPostingHook, NoPostingHook>();
        services.AddSingleton<IPostingService, InventoryPostingService>();
        return services;
    }
}

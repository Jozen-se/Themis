using Microsoft.Extensions.DependencyInjection;
using Themis.Kernel.Contracts.Posting;

namespace Themis.Kernel.Posting;

/// <summary>Registers the account resolver of step 6; the posting service of step 8 is added here later. Called by the composition root only.</summary>
internal static class KernelPostingServices
{
    public static IServiceCollection AddKernelPosting(this IServiceCollection services)
    {
        services.AddSingleton<IAccountResolver, PostingProfileAccountResolver>();
        return services;
    }
}

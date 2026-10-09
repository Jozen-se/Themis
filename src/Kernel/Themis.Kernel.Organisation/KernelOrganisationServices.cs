using Microsoft.Extensions.DependencyInjection;
using Themis.Kernel.Contracts.Organisation;
using Themis.Kernel.Contracts.Queries;
using Themis.Kernel.Contracts.Security;

namespace Themis.Kernel.Organisation;

/// <summary>Registers the organisation reads and the company query. Called by the composition root only.</summary>
internal static class KernelOrganisationServices
{
    public static IServiceCollection AddKernelOrganisation(this IServiceCollection services)
    {
        services.AddSingleton<IOrganisationReader, OrganisationReader>();
        services.AddScoped<IQueryHandler<GetCompany, CompanyInfo>, GetCompanyHandler>();
        services.AddScoped<IScopeResolver<GetCompany>, GetCompanyScopeResolver>();
        return services;
    }
}

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Themis.Kernel.Contracts.Commands;
using Themis.Kernel.Contracts.Organisation;
using Themis.Kernel.Contracts.Queries;
using Themis.Kernel.Contracts.Security;

namespace Themis.Kernel.Organisation;

/// <summary>
/// Registers the organisation reads, the company query and, since milestone 2 step 3, the organisation commands and
/// queries (chart of accounts header, company, site, warehouse). Called by the composition root only; the integration
/// tests register their own seams first (Build Brief, Test seams).
/// </summary>
internal static class KernelOrganisationServices
{
    public static IServiceCollection AddKernelOrganisation(this IServiceCollection services)
    {
        services.AddSingleton<IOrganisationReader, OrganisationReader>();
        services.AddScoped<IQueryHandler<GetCompany, CompanyInfo>, GetCompanyHandler>();
        services.AddScoped<IScopeResolver<GetCompany>, GetCompanyScopeResolver>();

        // The test seams of step 3 (docs/development/test-seams.md); the production host has no other registration.
        services.TryAddSingleton<IOrganisationCreateSeam, NoOrganisationCreateSeam>();
        services.TryAddSingleton<ICompanyUpdateSeam, NoCompanyUpdateSeam>();

        services.AddScoped<ICommandHandler<CreateChartOfAccounts, ChartOfAccountsView>, CreateChartOfAccountsHandler>();
        services.AddScoped<ICommandHandler<UpdateChartOfAccounts, ChartOfAccountsView>, UpdateChartOfAccountsHandler>();
        services.AddScoped<ICommandHandler<CreateCompany, CompanyInfo>, CreateCompanyHandler>();
        services.AddScoped<ICommandHandler<UpdateCompany, CompanyInfo>, UpdateCompanyHandler>();
        services.AddScoped<ICommandHandler<CreateSite, SiteInfo>, CreateSiteHandler>();
        services.AddScoped<ICommandHandler<UpdateSite, SiteInfo>, UpdateSiteHandler>();
        services.AddScoped<ICommandHandler<CreateWarehouse, WarehouseView>, CreateWarehouseHandler>();
        services.AddScoped<ICommandHandler<UpdateWarehouse, WarehouseView>, UpdateWarehouseHandler>();

        services.AddSingleton<ICommandValidator<CreateChartOfAccounts>, CreateChartOfAccountsValidator>();
        services.AddSingleton<ICommandValidator<UpdateChartOfAccounts>, UpdateChartOfAccountsValidator>();
        services.AddSingleton<ICommandValidator<CreateCompany>, CreateCompanyValidator>();
        services.AddSingleton<ICommandValidator<UpdateCompany>, UpdateCompanyValidator>();
        services.AddSingleton<ICommandValidator<CreateSite>, CreateSiteValidator>();
        services.AddSingleton<ICommandValidator<UpdateSite>, UpdateSiteValidator>();
        services.AddSingleton<ICommandValidator<CreateWarehouse>, CreateWarehouseValidator>();
        services.AddSingleton<ICommandValidator<UpdateWarehouse>, UpdateWarehouseValidator>();

        // Build Brief, Authorisation: a command or read on an existing record takes its scope from the record.
        services.AddSingleton<IScopeResolver<UpdateChartOfAccounts>, ChartOfAccountsScopeResolver>();
        services.AddSingleton<IScopeResolver<UpdateCompany>, UpdateCompanyScopeResolver>();
        services.AddSingleton<IScopeResolver<UpdateSite>, UpdateSiteScopeResolver>();
        services.AddSingleton<IScopeResolver<CreateWarehouse>, WarehouseScopeResolver>();
        services.AddSingleton<IScopeResolver<UpdateWarehouse>, WarehouseScopeResolver>();
        services.AddSingleton<IScopeResolver<GetWarehouse>, WarehouseScopeResolver>();

        // RR-45: the unique indexes behind R-MD-05, so a race past the handler's check is the rule's rejection, not a 500;
        // the partial index of the site's default is the backstop of the default move (a guard, R-DOC-01).
        services.AddSingleton(new UniqueConstraintRule("ux_company_code", OrganisationErrors.RuleUnique, OrganisationErrors.CompanyCodeDuplicate, "The company code already exists."));
        services.AddSingleton(new UniqueConstraintRule("ux_company_country_organisation_number", OrganisationErrors.RuleUnique, OrganisationErrors.OrganisationNumberDuplicate, "A company of the country with this organisation number already exists."));
        services.AddSingleton(new UniqueConstraintRule("ux_chart_of_accounts_code", OrganisationErrors.RuleUnique, OrganisationErrors.ChartCodeDuplicate, "The chart of accounts code already exists."));
        services.AddSingleton(new UniqueConstraintRule("ux_site_company_code", OrganisationErrors.RuleUnique, OrganisationErrors.SiteCodeDuplicate, "The site code already exists in the company."));
        services.AddSingleton(new UniqueConstraintRule("ux_warehouse_company_code", OrganisationErrors.RuleUnique, OrganisationErrors.WarehouseCodeDuplicate, "The warehouse code already exists in the company."));
        services.AddSingleton(new UniqueConstraintRule("ux_warehouse_site_default", OrganisationErrors.RuleGuard, OrganisationErrors.WarehouseDefaultTaken, "The site already has a default warehouse."));

        services.AddScoped<IQueryHandler<GetChartOfAccounts, ChartOfAccountsView>, GetChartOfAccountsHandler>();
        services.AddScoped<IQueryHandler<ListChartsOfAccounts, IReadOnlyList<ChartOfAccountsView>>, ListChartsOfAccountsHandler>();
        services.AddScoped<IQueryHandler<GetSite, SiteInfo>, GetSiteHandler>();
        services.AddScoped<IQueryHandler<ListSites, IReadOnlyList<SiteInfo>>, ListSitesHandler>();
        services.AddScoped<IQueryHandler<GetWarehouse, WarehouseView>, GetWarehouseHandler>();
        services.AddScoped<IQueryHandler<ListWarehouses, IReadOnlyList<WarehouseView>>, ListWarehousesHandler>();
        return services;
    }
}

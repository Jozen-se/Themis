using Themis.Kernel.Contracts.Commands;
using Themis.Kernel.Contracts.Queries;
using Themis.Kernel.Contracts.Security;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Contracts.Organisation;

/// <summary>
/// CHART_OF_ACCOUNTS.CREATE (milestone 2 step 3 brief, item 1): the header of a chart, installation-level, so
/// companies can share it (Specification, ChartOfAccounts). The code is unique per installation (R-MD-05) and never
/// changes (R-MD-06). Installation scope (D-47, RR-52).
/// </summary>
[Command("CHART_OF_ACCOUNTS.CREATE")]
public sealed record CreateChartOfAccounts(Code Code, string Name, string? Template = null) : ICommand<ChartOfAccountsView>, IScopedRequest
{
    public RequestScope Scope => RequestScope.Installation;
}

/// <summary>
/// CHART_OF_ACCOUNTS.UPDATE (step 3 brief, item 1): name and template on the current version (R-API-02). A code in
/// the body that differs from the stored one is rejected under R-MD-06; an equal one is ignored.
/// </summary>
[Command("CHART_OF_ACCOUNTS.UPDATE")]
public sealed record UpdateChartOfAccounts(Guid Id, string Name, long ExpectedVersion, string? Template = null, Code? Code = null) : ICommand<ChartOfAccountsView>, IVersionedCommand;

/// <summary>A chart of accounts header as the API returns it; the ETag is the version (R-API-02).</summary>
public sealed record ChartOfAccountsView(Guid Id, Code Code, string Name, string? Template, long Version) : IVersionedResult;

/// <summary>
/// COMPANY.CREATE (step 3 brief, item 3; D-64, D-65): every attribute of the Specification's Company except
/// payment_tolerance (Phase 2). The rounding mode defaults to the company.rounding_mode setting definition when
/// left out; the VAT rounding level is required while D-10 is open. The command also creates the company's own
/// party from the legal name, organisation number, VAT number and country, numbered from the installation-level
/// PARTY series (D-64). Installation scope (D-47, RR-52).
/// </summary>
[Command("COMPANY.CREATE")]
public sealed record CreateCompany(
    Code Code,
    string LegalName,
    string OrganisationNumber,
    CountryCode CountryCode,
    CurrencyCode BaseCurrency,
    string TimeZone,
    int FiscalYearStartMonth,
    Guid ChartOfAccountsId,
    TaxRoundingLevel? TaxRoundingLevel,
    string? VatNumber = null,
    RoundingMode? RoundingMode = null) : ICommand<CompanyInfo>, IScopedRequest
{
    public RequestScope Scope => RequestScope.Installation;
}

/// <summary>
/// COMPANY.UPDATE (step 3 brief, item 4; D-65, R-MD-06): the whole set of changeable attributes on the current
/// version (R-API-02). An attribute R-MD-06 has fixed is rejected with 422 and a code naming it; the code never
/// changes (a differing code in the body is rejected under R-MD-06, an equal one ignored). The company's own party
/// follows the legal name, organisation number, VAT number and country (D-64). Status changes come with step 7.
/// </summary>
[Command("COMPANY.UPDATE")]
public sealed record UpdateCompany(
    Guid Id,
    string LegalName,
    string OrganisationNumber,
    CountryCode CountryCode,
    CurrencyCode BaseCurrency,
    string TimeZone,
    int FiscalYearStartMonth,
    Guid ChartOfAccountsId,
    RoundingMode? RoundingMode,
    TaxRoundingLevel? TaxRoundingLevel,
    long ExpectedVersion,
    string? VatNumber = null,
    Code? Code = null) : ICommand<CompanyInfo>, IVersionedCommand;

/// <summary>
/// SITE.CREATE (step 3 brief, item 6): a site of the company in the body, which is the command's scope (R-SEC-01).
/// The code is unique per company (R-MD-05) and never changes (R-MD-06); the time zone is optional and must be an
/// IANA name (empty means the company's). A site has no status; its address waits for Address (D-55).
/// </summary>
[Command("SITE.CREATE")]
public sealed record CreateSite(Guid CompanyId, Code Code, string Name, string? TimeZone = null) : ICommand<SiteInfo>, IScopedRequest
{
    public RequestScope Scope => RequestScope.ForCompany(CompanyId);
}

/// <summary>SITE.UPDATE (step 3 brief, item 6): name and time zone on the current version; the code never changes (R-MD-06).</summary>
[Command("SITE.UPDATE")]
public sealed record UpdateSite(Guid Id, string Name, long ExpectedVersion, string? TimeZone = null, Code? Code = null) : ICommand<SiteInfo>, IVersionedCommand;

/// <summary>
/// WAREHOUSE.CREATE (step 3 brief, item 7): a warehouse of the site in the body, in the company the body names. The
/// scope is built from the loaded site row (its company and its id), never from the company id in the body; a site
/// that does not exist or belongs to another company than the body names declares the body's company and is a 400
/// (<c>site_unknown</c>, <c>site_other_company</c>) stored as the key's result, never a foreign-key 500 (the RR-102
/// pattern of the settings). The code is unique per company (R-MD-05) and never changes (R-MD-06); status Active at
/// create. Making it the default moves the site's default (Specification, Organisation commands).
/// </summary>
[Command("WAREHOUSE.CREATE")]
public sealed record CreateWarehouse(Guid CompanyId, Guid SiteId, Code Code, string Name, bool IsDefault = false) : ICommand<WarehouseView>;

/// <summary>
/// WAREHOUSE.UPDATE (step 3 brief, item 7): name and default flag on the current version; the site and the code
/// never change (R-MD-06). Setting the default moves it from the site's previous default, which gets its own version,
/// audit event and WAREHOUSE_UPDATED event; clearing it leaves the site without one. BLOCK and the rest of the
/// lifecycle come with step 7.
/// </summary>
[Command("WAREHOUSE.UPDATE")]
public sealed record UpdateWarehouse(Guid Id, string Name, bool IsDefault, long ExpectedVersion, Code? Code = null) : ICommand<WarehouseView>, IVersionedCommand;

/// <summary>A warehouse as the commands and queries return it: its site and company, code, name, default flag, status and version (R-API-02).</summary>
public sealed record WarehouseView(Guid Id, Guid SiteId, Guid CompanyId, Code Code, string Name, bool IsDefault, MasterDataStatus Status, long Version) : IVersionedResult;

/// <summary>GET /v1/kernel/charts-of-accounts/{id} (CHART_OF_ACCOUNTS.READ, D-47): installation scope (RR-52).</summary>
[Query("CHART_OF_ACCOUNTS.READ")]
public sealed record GetChartOfAccounts(Guid Id) : IQuery<ChartOfAccountsView>, IScopedRequest
{
    public RequestScope Scope => RequestScope.Installation;
}

/// <summary>GET /v1/kernel/charts-of-accounts (CHART_OF_ACCOUNTS.READ): every chart, ordered by code, at most <see cref="OrganisationRoutes.ListLimit"/> rows.</summary>
[Query("CHART_OF_ACCOUNTS.READ")]
public sealed record ListChartsOfAccounts : IQuery<IReadOnlyList<ChartOfAccountsView>>, IScopedRequest
{
    public RequestScope Scope => RequestScope.Installation;
}

/// <summary>GET /v1/kernel/companies/{companyId}/sites/{id} (SITE.READ, D-47): the scope is the company in the route, checked before anything is loaded.</summary>
[Query("SITE.READ")]
public sealed record GetSite(Guid CompanyId, Guid Id) : IQuery<SiteInfo>, IScopedRequest
{
    public RequestScope Scope => RequestScope.ForCompany(CompanyId);
}

/// <summary>GET /v1/kernel/companies/{companyId}/sites (SITE.READ): the company's sites, ordered by code, at most <see cref="OrganisationRoutes.ListLimit"/> rows.</summary>
[Query("SITE.READ")]
public sealed record ListSites(Guid CompanyId) : IQuery<IReadOnlyList<SiteInfo>>, IScopedRequest
{
    public RequestScope Scope => RequestScope.ForCompany(CompanyId);
}

/// <summary>
/// GET /v1/kernel/companies/{companyId}/warehouses/{id} (WAREHOUSE.READ, D-47): the scope is the warehouse's site,
/// read from the row, so a site-limited assignment reads the warehouses of its site; a warehouse outside the
/// company is 404.
/// </summary>
[Query("WAREHOUSE.READ")]
public sealed record GetWarehouse(Guid CompanyId, Guid Id) : IQuery<WarehouseView>;

/// <summary>
/// GET /v1/kernel/companies/{companyId}/warehouses?siteId= (WAREHOUSE.READ): the company's warehouses, or one
/// site's when siteId is given, ordered by code, at most <see cref="OrganisationRoutes.ListLimit"/> rows. With a
/// site the scope is that site, so a site-limited assignment lists its own site's warehouses and nothing else
/// (R-SEC-01: without a site it never satisfies the company-scoped list).
/// </summary>
[Query("WAREHOUSE.READ")]
public sealed record ListWarehouses(Guid CompanyId, Guid? SiteId = null) : IQuery<IReadOnlyList<WarehouseView>>, IScopedRequest
{
    public RequestScope Scope => SiteId is { } site && site != Guid.Empty ? RequestScope.ForSite(CompanyId, site) : RequestScope.ForCompany(CompanyId);
}

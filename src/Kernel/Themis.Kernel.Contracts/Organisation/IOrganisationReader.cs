using Themis.Kernel.Contracts.Commands;
using Themis.Kernel.Contracts.Persistence;
using Themis.Kernel.Contracts.Queries;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Contracts.Organisation;

/// <summary>
/// The company as every later step reads it and as COMPANY.CREATE and COMPANY.UPDATE return it (brief, step 3 read
/// contracts; milestone 2 step 3): identity, its own party (D-64), legal data, base currency, time zone, fiscal year
/// start, chart, rounding, status and the version the ETag carries (R-API-02). payment_tolerance waits for Phase 2.
/// </summary>
public sealed record CompanyInfo(
    Guid Id,
    Code Code,
    Guid PartyId,
    string LegalName,
    string OrganisationNumber,
    string? VatNumber,
    CountryCode CountryCode,
    CurrencyCode BaseCurrency,
    string TimeZone,
    int FiscalYearStartMonth,
    Guid ChartOfAccountsId,
    RoundingMode RoundingMode,
    TaxRoundingLevel TaxRoundingLevel,
    CompanyStatus Status,
    long Version) : IVersionedResult;

/// <summary>A site as the reader, SITE.CREATE, SITE.UPDATE and the site queries return it; the ETag is the version (R-API-02).</summary>
public sealed record SiteInfo(Guid Id, Guid CompanyId, Code Code, string Name, string? TimeZone, long Version) : IVersionedResult;

/// <summary>A warehouse with its site and company; the company is the site's company (Conventions, Company scope).</summary>
public sealed record WarehouseInfo(Guid Id, Code Code, string Name, bool IsDefault, MasterDataStatus Status, SiteInfo Site, CompanyInfo Company);

/// <summary>The period a business date falls in: steps 7 and 8 take the entry's period from it and check it is open (R-LED-04).</summary>
public sealed record FiscalPeriodInfo(Guid Id, Guid FiscalYearId, int PeriodNumber, BusinessDate StartDate, BusinessDate EndDate, FiscalPeriodStatus Status);

/// <summary>
/// Reads of the organisation model, implemented in Themis.Kernel.Organisation on the caller's unit of work, so a
/// command or a query reads inside its own transaction and role (R-DB-01).
/// </summary>
public interface IOrganisationReader
{
    Task<CompanyInfo?> GetCompanyAsync(IUnitOfWork unitOfWork, Guid companyId, CancellationToken cancellationToken);

    Task<WarehouseInfo?> GetWarehouseAsync(IUnitOfWork unitOfWork, Guid warehouseId, CancellationToken cancellationToken);

    /// <summary>The calendar date of a UTC instant in the company's time zone (R-TIME-01).</summary>
    BusinessDate BusinessDateFor(CompanyInfo company, DateTimeOffset instant);

    /// <summary>The company's fiscal period covering the date; a date outside every fiscal year is rejected with R-LED-04.</summary>
    Task<Result<FiscalPeriodInfo>> ResolveFiscalPeriodAsync(IUnitOfWork unitOfWork, Guid companyId, BusinessDate businessDate, CancellationToken cancellationToken);
}

/// <summary>
/// GET /v1/kernel/companies/{id}: the company as <see cref="CompanyInfo"/> (permission COMPANY.READ, D-47). Its scope
/// is the company it names, resolved after the record is loaded, so a missing company is a 404 (Build Brief, Authorisation).
/// </summary>
[Query("COMPANY.READ")]
public sealed record GetCompany(Guid Id) : IQuery<CompanyInfo>;

public static class OrganisationRoutes
{
    /// <summary>The most rows a list query returns (step 3 brief, item 8): no paging exists yet, so a list is cut here, ordered by code.</summary>
    public const int ListLimit = 1000;

    public const string Companies = "/v1/kernel/companies";
    public const string Company = "/v1/kernel/companies/{id}";
    public const string CompanyUpdate = "/v1/kernel/companies/{id}/update";
    public const string ChartsOfAccounts = "/v1/kernel/charts-of-accounts";
    public const string ChartOfAccounts = "/v1/kernel/charts-of-accounts/{id}";
    public const string ChartOfAccountsUpdate = "/v1/kernel/charts-of-accounts/{id}/update";
    public const string Sites = "/v1/kernel/sites";
    public const string SiteUpdate = "/v1/kernel/sites/{id}/update";
    public const string Site = "/v1/kernel/companies/{companyId}/sites/{id}";
    public const string CompanySites = "/v1/kernel/companies/{companyId}/sites";
    public const string Warehouses = "/v1/kernel/warehouses";
    public const string WarehouseUpdate = "/v1/kernel/warehouses/{id}/update";
    public const string Warehouse = "/v1/kernel/companies/{companyId}/warehouses/{id}";
    public const string CompanyWarehouses = "/v1/kernel/companies/{companyId}/warehouses";

    /// <summary>The command routes the composition root maps through the kernel pipeline (brief, API conventions; milestone 2 step 3).</summary>
    public static IReadOnlyList<ICommandRoute> Commands { get; } =
    [
        new CommandRoute<CreateChartOfAccounts, ChartOfAccountsView>(ChartsOfAccounts),
        new CommandRoute<UpdateChartOfAccounts, ChartOfAccountsView>(ChartOfAccountsUpdate),
        new CommandRoute<CreateCompany, CompanyInfo>(Companies),
        new CommandRoute<UpdateCompany, CompanyInfo>(CompanyUpdate),
        new CommandRoute<CreateSite, SiteInfo>(Sites),
        new CommandRoute<UpdateSite, SiteInfo>(SiteUpdate),
        new CommandRoute<CreateWarehouse, WarehouseView>(Warehouses),
        new CommandRoute<UpdateWarehouse, WarehouseView>(WarehouseUpdate),
    ];
}

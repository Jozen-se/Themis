using Themis.Kernel.Contracts.Commands;
using Themis.Kernel.Contracts.Persistence;
using Themis.Kernel.Contracts.Queries;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Contracts.Organisation;

/// <summary>The company as every later step reads it: base currency, time zone, rounding, status (brief, step 3 read contracts).</summary>
public sealed record CompanyInfo(
    Guid Id,
    Code Code,
    string LegalName,
    CountryCode CountryCode,
    CurrencyCode BaseCurrency,
    string TimeZone,
    int FiscalYearStartMonth,
    RoundingMode RoundingMode,
    TaxRoundingLevel TaxRoundingLevel,
    CompanyStatus Status,
    long Version) : IVersionedResult;

public sealed record SiteInfo(Guid Id, Guid CompanyId, Code Code, string Name, string? TimeZone);

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
    public const string Company = "/v1/kernel/companies/{id}";
}

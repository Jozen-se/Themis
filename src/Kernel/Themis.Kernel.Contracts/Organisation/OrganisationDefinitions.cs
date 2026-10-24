using Themis.Kernel.Primitives;

namespace Themis.Kernel.Contracts.Organisation;

/// <summary>Installation lifecycle (Phase 0 Specification, Installation). Suspended rejects every command.</summary>
public enum InstallationStatus
{
    Active,
    Suspended,
}

public enum PartyType
{
    Organisation,
    Person,
}

/// <summary>The shared master data lifecycle (State machines, part 1; R-MD-03: never deleted).</summary>
public enum MasterDataStatus
{
    Active,
    Blocked,
    Inactive,
}

/// <summary>Company and Contact use only Active and Inactive (State machines, part 1).</summary>
public enum CompanyStatus
{
    Active,
    Inactive,
}

/// <summary>VAT rounding per line or per document total; the default is pending D-10.</summary>
public enum TaxRoundingLevel
{
    Line,
    Document,
}

public enum FiscalYearStatus
{
    Open,
    Closed,
}

/// <summary>Fiscal periods move one way to Closed, with a single step back from SoftClosed (State machines, part 1; R-LED-04).</summary>
public enum FiscalPeriodStatus
{
    Open,
    SoftClosed,
    Closed,
}

/// <summary>
/// The organisation rows of an installation as data, framework-free, so that a capability can define them and the
/// persistence capability can write them without referencing each other (ARCH-08). Attributes follow the Phase 0
/// Specification's organisation, party and ledger entity tables; the identifiers are supplied by the caller so that
/// a seed is idempotent.
/// </summary>
public sealed record InstallationDefinition(Guid Id, Code Code, string Name, InstallationStatus Status, string DefaultLocale);

/// <summary>
/// A party as the fixture defines it. Its number is not part of the definition: the seeder takes it from the
/// installation-level PARTY series through the same numbering path COMPANY.CREATE uses (milestone 2 step 3, D-64),
/// so no fixed number can collide with a number the series hands out.
/// </summary>
public sealed record PartyDefinition(
    Guid Id,
    PartyType PartyType,
    string Name,
    string? OrganisationNumber,
    string? VatNumber,
    CountryCode CountryCode,
    string? Language,
    MasterDataStatus Status,
    string DuplicateKey);

public sealed record ChartOfAccountsDefinition(Guid Id, Code Code, string Name, string? Template);

/// <summary>A company as the fixture defines it; the normalised organisation number is computed by the fixture with the duplicate key's function (R-MD-05, D-64), like the party's duplicate key.</summary>
public sealed record CompanyDefinition(
    Guid Id,
    Code Code,
    Guid PartyId,
    string LegalName,
    string OrganisationNumber,
    string OrganisationNumberNormalised,
    string? VatNumber,
    CountryCode CountryCode,
    CurrencyCode BaseCurrency,
    string TimeZone,
    int FiscalYearStartMonth,
    Guid ChartOfAccountsId,
    RoundingMode RoundingMode,
    TaxRoundingLevel TaxRoundingLevel,
    CompanyStatus Status);

public sealed record SiteDefinition(Guid Id, Guid CompanyId, Code Code, string Name, string? TimeZone);

/// <summary>Company scope convention: the warehouse stores the company it derives from its site, enforced by a composite foreign key.</summary>
public sealed record WarehouseDefinition(Guid Id, Guid SiteId, Guid CompanyId, Code Code, string Name, bool IsDefault, MasterDataStatus Status);

public sealed record FiscalPeriodDefinition(Guid Id, int PeriodNumber, BusinessDate StartDate, BusinessDate EndDate, FiscalPeriodStatus Status);

public sealed record FiscalYearDefinition(Guid Id, Guid CompanyId, Code Code, BusinessDate StartDate, BusinessDate EndDate, FiscalYearStatus Status, IReadOnlyList<FiscalPeriodDefinition> Periods);

/// <summary>One installation with one company, its own party, one site, one warehouse and one fiscal year (brief, milestone 1 step 3).</summary>
public sealed record OrganisationFixtureDefinition(
    InstallationDefinition Installation,
    PartyDefinition CompanyParty,
    ChartOfAccountsDefinition ChartOfAccounts,
    CompanyDefinition Company,
    SiteDefinition Site,
    WarehouseDefinition Warehouse,
    FiscalYearDefinition FiscalYear);

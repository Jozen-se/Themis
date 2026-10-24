namespace Themis.Kernel.Persistence.Entities.Organisation;

/// <summary>Phase 0 Specification, Company. The attributes R-MD-06 fixes (D-65) are listed in the Specification's Fixed attributes table; the commands enforce them.</summary>
internal sealed class Company : MasterDataRow
{
    public required string Code { get; set; }

    public required Guid PartyId { get; set; }

    public required string LegalName { get; set; }

    public required string OrganisationNumber { get; set; }

    /// <summary>
    /// The organisation number normalised in C# by the same function as the R-MD-01 duplicate key (uppercase letters
    /// and digits only), so the unique key per country (R-MD-05, D-64) and the duplicate key never disagree. Written
    /// by COMPANY.CREATE and COMPANY.UPDATE; never read as a business value.
    /// </summary>
    public required string OrganisationNumberNormalised { get; set; }

    public string? VatNumber { get; set; }

    public required string CountryCode { get; set; }

    public required string BaseCurrency { get; set; }

    public required string TimeZone { get; set; }

    public required int FiscalYearStartMonth { get; set; }

    public required Guid ChartOfAccountsId { get; set; }

    public required string RoundingMode { get; set; }

    public required string TaxRoundingLevel { get; set; }

    /// <summary>Maximum payment difference written off automatically, in the base currency (R-O2C-07).</summary>
    public decimal? PaymentTolerance { get; set; }

    public required string Status { get; set; }
}

namespace Themis.Kernel.Persistence.Entities.Organisation;

/// <summary>Phase 0 Specification, Company. Base currency and time zone are immutable after the first posting.</summary>
internal sealed class Company : MasterDataRow
{
    public required string Code { get; set; }

    public required Guid PartyId { get; set; }

    public required string LegalName { get; set; }

    public required string OrganisationNumber { get; set; }

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

namespace Themis.Kernel.Persistence.Entities.Organisation;

/// <summary>Phase 0 Specification, FiscalYear: contiguous years of 12 to 18 months; code unique per company (R-MD-05).</summary>
internal sealed class FiscalYear : MasterDataRow
{
    public required Guid CompanyId { get; set; }

    public required string Code { get; set; }

    public required DateOnly StartDate { get; set; }

    public required DateOnly EndDate { get; set; }

    public required string Status { get; set; }
}

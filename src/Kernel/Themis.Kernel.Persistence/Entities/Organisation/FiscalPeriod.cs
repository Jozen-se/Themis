namespace Themis.Kernel.Persistence.Entities.Organisation;

/// <summary>
/// Phase 0 Specification, FiscalPeriod: periods cover the year exactly; posting checks the period is open
/// (R-LED-04). The company is derived from the fiscal year and stored, enforced by the composite foreign key
/// (fiscal_year_id, company_id) (Conventions, Company scope).
/// </summary>
internal sealed class FiscalPeriod : MasterDataRow
{
    public required Guid FiscalYearId { get; set; }

    public required Guid CompanyId { get; set; }

    public required int PeriodNumber { get; set; }

    public required DateOnly StartDate { get; set; }

    public required DateOnly EndDate { get; set; }

    public required string Status { get; set; }
}

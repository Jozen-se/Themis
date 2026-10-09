namespace Themis.Kernel.Persistence.Entities.Organisation;

/// <summary>
/// Phase 0 Specification, Warehouse. The company is derived from the site and stored, enforced by the composite
/// foreign key (site_id, company_id) (Conventions, Company scope). Code unique per company (R-MD-05).
/// </summary>
internal sealed class Warehouse : MasterDataRow
{
    public required Guid SiteId { get; set; }

    public required Guid CompanyId { get; set; }

    public required string Code { get; set; }

    public required string Name { get; set; }

    public required bool IsDefault { get; set; }

    public required string Status { get; set; }
}

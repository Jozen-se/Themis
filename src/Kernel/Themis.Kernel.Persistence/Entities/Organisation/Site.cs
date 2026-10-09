namespace Themis.Kernel.Persistence.Entities.Organisation;

/// <summary>Phase 0 Specification, Site: code unique per company (R-MD-05).</summary>
internal sealed class Site : MasterDataRow
{
    public required Guid CompanyId { get; set; }

    public required string Code { get; set; }

    public required string Name { get; set; }

    public Guid? AddressId { get; set; }

    public string? TimeZone { get; set; }
}

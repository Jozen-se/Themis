namespace Themis.Kernel.Persistence.Entities.Organisation;

/// <summary>Phase 0 Specification, Installation: one per company group (D-28). Suspended rejects every command.</summary>
internal sealed class Installation : MasterDataRow
{
    public required string Code { get; set; }

    public required string Name { get; set; }

    public required string Status { get; set; }

    public required string DefaultLocale { get; set; }
}

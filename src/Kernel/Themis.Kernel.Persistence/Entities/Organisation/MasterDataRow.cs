namespace Themis.Kernel.Persistence.Entities.Organisation;

/// <summary>
/// The common fields of every master data entity (Phase 0 Specification, Conventions): time-ordered UUID, created
/// and updated stamps, the version a stale update is rejected on (R-API-02) and the customer extension (R-EXT-01).
/// Nothing is ever deleted (R-MD-03): the kernel role holds no DELETE on these tables.
/// </summary>
internal abstract class MasterDataRow
{
    public required Guid Id { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required Guid CreatedBy { get; init; }

    public required DateTimeOffset UpdatedAt { get; set; }

    public required Guid UpdatedBy { get; set; }

    public long Version { get; set; } = 1;

    public string Extension { get; set; } = "{}";
}

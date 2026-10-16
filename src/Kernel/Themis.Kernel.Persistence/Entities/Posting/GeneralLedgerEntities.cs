using Themis.Kernel.Persistence.Entities.Organisation;

namespace Themis.Kernel.Persistence.Entities.Posting;

/// <summary>Phase 0 Specification, Account: a row of a chart; number unique per chart (R-MD-05); control accounts per R-ACC-01.</summary>
internal sealed class Account : MasterDataRow
{
    public required Guid ChartId { get; set; }

    public required string AccountNumber { get; set; }

    public required string Name { get; set; }

    public required string AccountType { get; set; }

    public required bool IsControl { get; set; }

    public required string Status { get; set; }
}

/// <summary>
/// Phase 0 Specification, PostingProfileRule: a company's mapping of an account role to an account. Empty keys mean
/// any value for resolution and count as a value for the no-overlap check (R-MD-02). <see cref="ExpectsControl"/> is a
/// stored generated column derived from the role, so the composite foreign key to the account's control flag enforces
/// R-PP-04 in the database.
/// </summary>
internal sealed class PostingProfileRule : MasterDataRow
{
    public required Guid CompanyId { get; set; }

    public required string AccountRole { get; set; }

    public string? ItemPostingGroup { get; set; }

    public string? PartyPostingGroup { get; set; }

    public string? ShipToScope { get; set; }

    public Guid? TaxCodeId { get; set; }

    public string? ReasonCode { get; set; }

    public required Guid AccountId { get; set; }

    public bool ExpectsControl { get; private set; }

    public required DateOnly ValidFrom { get; set; }

    public DateOnly? ValidTo { get; set; }
}

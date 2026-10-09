namespace Themis.Kernel.Persistence.Entities.Organisation;

/// <summary>Phase 0 Specification, Party: one per real-world entity per installation (R-MD-01); the duplicate key is derived.</summary>
internal sealed class Party : MasterDataRow
{
    public required string PartyNumber { get; set; }

    public required string PartyType { get; set; }

    public required string Name { get; set; }

    public string? OrganisationNumber { get; set; }

    public string? VatNumber { get; set; }

    public required string CountryCode { get; set; }

    public string? Language { get; set; }

    public required string Status { get; set; }

    public required string DuplicateKey { get; set; }

    public DateTimeOffset? AnonymisedAt { get; set; }
}

using Themis.Kernel.Persistence.Entities.Organisation;

namespace Themis.Kernel.Persistence.Entities.Documents;

/// <summary>Phase 0 Specification, DocumentType: the table-driven registry (D-40), system-owned, written by Themis.Migrator from the catalogue in code.</summary>
internal sealed class DocumentType : MasterDataRow
{
    public required string Code { get; set; }

    public required string NumberAssigned { get; set; }

    public required bool Gapless { get; set; }

    public required bool PostsInventory { get; set; }

    public required bool PostsGeneralLedger { get; set; }

    public string? PartyRole { get; set; }
}

/// <summary>One transition of a document type's state machine (R-DOC-01) with the name of the event it writes (D-44); system-owned like the type.</summary>
internal sealed class DocumentTransition : MasterDataRow
{
    public required string DocumentType { get; set; }

    public required string Command { get; set; }

    public required string FromStatus { get; set; }

    public required string ToStatus { get; set; }

    public required string EventName { get; set; }
}

/// <summary>Phase 0 Specification, Document: the shared header of every document type; milestone 1 step 7 holds the columns the brief names.</summary>
internal sealed class Document : MasterDataRow
{
    public required Guid CompanyId { get; set; }

    public required string DocumentType { get; set; }

    public required string Status { get; set; }

    /// <summary>Empty until the type's assignment point; unique per company and type once assigned.</summary>
    public string? DocumentNumber { get; set; }

    /// <summary>The document id as text, stored at CREATE and never changed (R-DOC-04).</summary>
    public required string DraftReference { get; set; }

    public required DateOnly DocumentDate { get; set; }

    public DateOnly? PostingDate { get; set; }

    public required string Currency { get; set; }

    public Guid? WarehouseId { get; set; }

    public string? ExternalReference { get; set; }

    public DateTimeOffset? PostedAt { get; set; }

    public Guid? PostedBy { get; set; }
}

/// <summary>Phase 0 Specification, DocumentLine: the company is stored and tied to the document's by a composite foreign key (Company scope convention).</summary>
internal sealed class DocumentLine : MasterDataRow
{
    public required Guid CompanyId { get; set; }

    public required Guid DocumentId { get; set; }

    public required int LineNumber { get; set; }

    public required string LineType { get; set; }

    public Guid? ItemId { get; set; }

    public required string Description { get; set; }

    public decimal? Quantity { get; set; }

    public string? UnitCode { get; set; }

    public decimal? QuantityBase { get; set; }

    public Guid? WarehouseId { get; set; }

    public string? StockStatus { get; set; }

    public decimal? EnteredUnitCost { get; set; }

    public decimal? UnitCost { get; set; }

    public decimal? CostAmount { get; set; }

    public required string LineStatus { get; set; }
}

/// <summary>Phase 0 Specification, type-specific header extension of INVENTORY_ADJUSTMENT (D-16, D-39): one row per document, keyed by the document id.</summary>
internal sealed class InventoryAdjustment : MasterDataRow
{
    public required Guid CompanyId { get; set; }

    public string? ReasonCode { get; set; }
}

/// <summary>Phase 0 Specification, NumberSeries: the locked row every number is taken from (R-DOC-07).</summary>
internal sealed class NumberSeries : MasterDataRow
{
    public required Guid CompanyId { get; set; }

    public required string DocumentType { get; set; }

    public required string Scope { get; set; }

    public Guid? FiscalYearId { get; set; }

    public string? Prefix { get; set; }

    public required int Padding { get; set; }

    public required long NextNumber { get; set; }

    public required bool Gapless { get; set; }

    /// <summary>The registry's gapless flag for the type, tied to it by a composite foreign key, so the check "gapless cannot be switched off where the type requires it" holds in the database.</summary>
    public required bool TypeRequiresGapless { get; set; }
}

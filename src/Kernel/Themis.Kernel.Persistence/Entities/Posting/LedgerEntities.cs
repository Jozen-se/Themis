using Themis.Kernel.Persistence.Entities.Organisation;

namespace Themis.Kernel.Persistence.Entities.Posting;

/// <summary>Phase 0 Specification, JournalEntry: append-only (R-LED-01, R-LED-06); no updated or version fields; the command that created it (D-51).</summary>
internal sealed class JournalEntry
{
    public required Guid Id { get; init; }

    public required Guid CompanyId { get; init; }

    public required string EntryNumber { get; init; }

    public required Guid FiscalPeriodId { get; init; }

    public required DateOnly PostingDate { get; init; }

    public required DateOnly DocumentDate { get; init; }

    public required Guid SourceDocumentId { get; init; }

    public required Guid CommandId { get; init; }

    public required string Description { get; init; }

    public Guid? ReversalOfEntryId { get; init; }

    public required DateTimeOffset RecordedAt { get; init; }

    public required Guid RecordedBy { get; init; }

    public string Extension { get; init; } = "{}";
}

/// <summary>Phase 0 Specification, JournalLine: append-only; amount signed debit positive, never 0 (D-15, I-02); the company stored for the scope keys (step 8 brief).</summary>
internal sealed class JournalLine
{
    public required Guid Id { get; init; }

    public required Guid JournalEntryId { get; init; }

    public required Guid CompanyId { get; init; }

    public required int LineNumber { get; init; }

    public required Guid AccountId { get; init; }

    public string? AccountRole { get; init; }

    public Guid? PostingRuleId { get; init; }

    public required decimal Amount { get; init; }

    public required string TxnCurrency { get; init; }

    public required decimal TxnAmount { get; init; }

    public Guid? ItemId { get; init; }

    public Guid? WarehouseId { get; init; }

    public Guid? SourceLineId { get; init; }

    public string Extension { get; init; } = "{}";
}

/// <summary>Phase 0 Specification, InventoryEntry: append-only; signed quantity and cost, never a zero quantity; lot_id waits for Lot (D-18); the command that created it (D-51).</summary>
internal sealed class InventoryEntry
{
    public required Guid Id { get; init; }

    public required Guid CompanyId { get; init; }

    public required Guid ItemId { get; init; }

    public required Guid WarehouseId { get; init; }

    public Guid? LotId { get; init; }

    public required string StockStatus { get; init; }

    public required string MovementType { get; init; }

    public required decimal QuantityBase { get; init; }

    public required decimal CostAmount { get; init; }

    public required decimal UnitCost { get; init; }

    public required DateOnly PostingDate { get; init; }

    public required DateOnly DocumentDate { get; init; }

    public required Guid SourceLineId { get; init; }

    public Guid? JournalEntryId { get; init; }

    public Guid? ReversalOfEntryId { get; init; }

    public required Guid CommandId { get; init; }

    public required DateTimeOffset RecordedAt { get; init; }

    public required Guid RecordedBy { get; init; }

    public string Extension { get; init; } = "{}";
}

/// <summary>Phase 0 Specification, Caches: ItemCostState per company and item, rebuildable from the entries (R-INV-01); the lock target of class 2 (R-LED-08).</summary>
internal sealed class ItemCostState : MasterDataRow
{
    public required Guid CompanyId { get; set; }

    public required Guid ItemId { get; set; }

    public required decimal QuantityBase { get; set; }

    public required decimal InventoryValue { get; set; }

    public decimal? AverageCost { get; set; }

    public Guid? LastInventoryEntryId { get; set; }
}

/// <summary>Phase 0 Specification, Caches: StockBalance per company, item, warehouse, lot and stock status (D-18), rebuildable (R-INV-01); the lock target of class 3 (R-LED-08).</summary>
internal sealed class StockBalance : MasterDataRow
{
    public required Guid CompanyId { get; set; }

    public required Guid ItemId { get; set; }

    public required Guid WarehouseId { get; set; }

    public Guid? LotId { get; set; }

    public required string StockStatus { get; set; }

    public required decimal OnHandBase { get; set; }

    public required decimal ReservedBase { get; set; }

    public Guid? LastInventoryEntryId { get; set; }
}

using Themis.Kernel.Contracts.Commands;
using Themis.Kernel.Contracts.Documents;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Contracts.Posting;

/// <summary>Phase 0 Specification, InventoryEntry.movement_type: the movements of Phases 0 to 3 (Receipt arrives with purchasing).</summary>
public enum MovementType
{
    AdjustmentIn,
    AdjustmentOut,
    Issue,
    ReturnIn,
    StatusOut,
    StatusIn,
}

/// <summary>
/// An Open line of an inventory adjustment as the posting service receives it: the stored warehouse and stock
/// status (never re-defaulted from the header, Specification DocumentLine.warehouse_id), the signed base quantity
/// (positive adds stock, negative removes it) and the entered unit cost of an inbound line.
/// </summary>
public sealed record PostingLine(Guid LineId, int LineNumber, Guid ItemId, Code ItemPostingGroup, Guid WarehouseId, StockStatus StockStatus, decimal QuantityBase, decimal? EnteredUnitCost);

/// <summary>
/// What the document framework hands the posting service for INVENTORY_ADJUSTMENT.POST after the guards passed and
/// the document row is locked (lock class 1): the document, its company and the document's currency (compared with
/// the base currency the posting reads under lock class 1b, D-65; the minor units and the rounding mode come from
/// that read too), the posting and document dates, the fiscal period and year of the posting date (R-LED-04), the
/// reason code and the Open lines in line number order.
/// </summary>
public sealed record InventoryAdjustmentPosting(
    Guid DocumentId,
    Guid CompanyId,
    CurrencyCode Currency,
    BusinessDate PostingDate,
    BusinessDate DocumentDate,
    Guid FiscalPeriodId,
    Guid FiscalYearId,
    ReasonCode ReasonCode,
    IReadOnlyList<PostingLine> Lines);

/// <summary>A line after posting: its inventory entry, the signed cost and the unit cost stored on the line as on the entry (R-DOC-05).</summary>
public sealed record PostedLine(Guid LineId, Guid InventoryEntryId, MovementType MovementType, decimal CostAmount, decimal UnitCost);

/// <summary>The outcome of a posting: the document number assigned, the journal entry when one was written (I-08), and the costed lines.</summary>
public sealed record PostingOutcome(string DocumentNumber, Guid? JournalEntryId, string? EntryNumber, IReadOnlyList<PostedLine> Lines);

/// <summary>
/// The kernel posting service (R-LED-05, R-LED-07): the only writer of inventory and journal entries, in the
/// command's single transaction. For an inventory adjustment it locks the cost states and stock balances in the
/// lock order (R-LED-08), costs the lines with the moving average algorithm (R-INV-05, R-INV-07, R-INV-09), checks
/// stock (R-INV-03, R-INV-04), resolves the accounts (R-PP-01, R-PP-02), takes the numbers as the last lock class
/// (R-DOC-07, R-DOC-08) and writes the journal entry, the inventory entries and the cache updates. The document
/// framework updates the document and its lines afterwards and writes the audit and outbox events.
/// </summary>
public interface IPostingService
{
    Task<Result<PostingOutcome>> PostInventoryAdjustmentAsync(InventoryAdjustmentPosting posting, ICommandContext context, CancellationToken cancellationToken);
}

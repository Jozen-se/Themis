using Themis.Kernel.Contracts.Commands;
using Themis.Kernel.Contracts.Posting;
using Themis.Kernel.Contracts.Queries;
using Themis.Kernel.Contracts.Security;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Contracts.Documents;

/// <summary>Phase 0 Specification, DocumentLine.stock_status: the stock a movement line touches.</summary>
public enum StockStatus
{
    Available,
    Blocked,
}

/// <summary>Phase 0 Specification, DocumentLine.line_status. A line left out of an UPDATE becomes Cancelled; nothing is deleted.</summary>
public enum LineStatus
{
    Open,
    Closed,
    Cancelled,
}

/// <summary>Phase 0 Specification, DocumentLine.line_type. Milestone 1 writes Item lines only.</summary>
public enum DocumentLineType
{
    Item,
    Charge,
    Account,
    Allocation,
    Text,
}

/// <summary>
/// One line of an inventory adjustment draft as the client sends it (step 7 brief): the item (set up in the
/// document's company, D-43), a signed quantity in the item's base unit (positive adds stock, negative removes it),
/// the warehouse (the header's when left out), the stock status and, for inbound lines, an optional entered unit
/// cost. Lines are matched by line number, positive and unique in the request.
/// </summary>
public sealed record InventoryAdjustmentLineInput(
    int LineNumber,
    Guid ItemId,
    decimal Quantity,
    Guid? WarehouseId = null,
    StockStatus StockStatus = StockStatus.Available,
    decimal? EnteredUnitCost = null);

/// <summary>
/// INVENTORY_ADJUSTMENT.CREATE (step 7 brief): a Draft for the company in the body, which is the command's scope
/// (R-SEC-01). The currency is the company's base currency, set by the system; the draft reference is the document
/// id. A draft may be incomplete: the POST guards (reason code, at least one Open line, items Stocked and Active,
/// stock coverage, period open) run at posting in step 8.
/// </summary>
[Command("INVENTORY_ADJUSTMENT.CREATE")]
public sealed record CreateInventoryAdjustment(
    Guid CompanyId,
    BusinessDate DocumentDate,
    ReasonCode? ReasonCode = null,
    Guid? WarehouseId = null,
    string? ExternalReference = null,
    BusinessDate? PostingDate = null,
    IReadOnlyList<InventoryAdjustmentLineInput>? Lines = null) : ICommand<InventoryAdjustmentView>, IScopedRequest
{
    public RequestScope Scope => RequestScope.ForCompany(CompanyId);
}

/// <summary>
/// INVENTORY_ADJUSTMENT.UPDATE (step 7 brief): the whole draft on its current version (R-DOC-02, R-API-02). Lines
/// are matched by line number: a new number inserts, an existing one updates, a line left out becomes Cancelled,
/// a cancelled number given again reopens the line with the new values; nothing is deleted. Id from the route; the
/// scope from the record.
/// </summary>
[Command("INVENTORY_ADJUSTMENT.UPDATE")]
public sealed record UpdateInventoryAdjustment(
    Guid Id,
    long ExpectedVersion,
    BusinessDate DocumentDate,
    ReasonCode? ReasonCode = null,
    Guid? WarehouseId = null,
    string? ExternalReference = null,
    BusinessDate? PostingDate = null,
    IReadOnlyList<InventoryAdjustmentLineInput>? Lines = null) : ICommand<InventoryAdjustmentView>, IVersionedCommand;

/// <summary>INVENTORY_ADJUSTMENT.DISCARD (step 7 brief): Draft to Discarded on the current version; the row stays (R-DOC-06).</summary>
[Command("INVENTORY_ADJUSTMENT.DISCARD")]
public sealed record DiscardInventoryAdjustment(Guid Id, long ExpectedVersion) : ICommand<InventoryAdjustmentView>, IVersionedCommand;

/// <summary>A line of an inventory adjustment as the API returns it; quantities and costs travel as strings (D-37).</summary>
public sealed record InventoryAdjustmentLineView(
    Guid Id,
    int LineNumber,
    LineStatus LineStatus,
    Guid ItemId,
    string Description,
    decimal Quantity,
    UnitCode UnitCode,
    decimal QuantityBase,
    Guid WarehouseId,
    StockStatus StockStatus,
    decimal? EnteredUnitCost,
    decimal? UnitCost,
    decimal? CostAmount,
    long Version);

/// <summary>
/// An inventory adjustment with its header, its reason code and its non-Cancelled lines, as every command and the
/// read return it. The document number is empty until posting assigns it (number on post); the version is the
/// ETag (R-API-02).
/// </summary>
public sealed record InventoryAdjustmentView(
    Guid Id,
    Guid CompanyId,
    string DocumentType,
    DocumentStatus Status,
    string? DocumentNumber,
    string DraftReference,
    BusinessDate DocumentDate,
    BusinessDate? PostingDate,
    CurrencyCode Currency,
    Guid? WarehouseId,
    string? ExternalReference,
    ReasonCode? ReasonCode,
    DateTimeOffset? PostedAt,
    Guid? PostedBy,
    long Version,
    IReadOnlyList<InventoryAdjustmentLineView> Lines) : IVersionedResult;

/// <summary>GET /v1/kernel/companies/{companyId}/inventory-adjustments/{id} (INVENTORY_ADJUSTMENT.READ, D-47): the scope is the company in the route, checked before anything is loaded.</summary>
[Query("INVENTORY_ADJUSTMENT.READ")]
public sealed record GetInventoryAdjustment(Guid CompanyId, Guid Id) : IQuery<InventoryAdjustmentView>, IScopedRequest
{
    public RequestScope Scope => RequestScope.ForCompany(CompanyId);
}

public static class DocumentRoutes
{
    public const string InventoryAdjustments = "/v1/kernel/inventory-adjustments";
    public const string InventoryAdjustmentUpdate = "/v1/kernel/inventory-adjustments/{id}/update";
    public const string InventoryAdjustmentDiscard = "/v1/kernel/inventory-adjustments/{id}/discard";
    public const string InventoryAdjustment = "/v1/kernel/companies/{companyId}/inventory-adjustments/{id}";

    /// <summary>The command routes the composition root maps through the kernel pipeline (brief, API conventions).</summary>
    public static IReadOnlyList<ICommandRoute> Commands { get; } =
    [
        new CommandRoute<CreateInventoryAdjustment, InventoryAdjustmentView>(InventoryAdjustments),
        new CommandRoute<UpdateInventoryAdjustment, InventoryAdjustmentView>(InventoryAdjustmentUpdate),
        new CommandRoute<DiscardInventoryAdjustment, InventoryAdjustmentView>(InventoryAdjustmentDiscard),
    ];
}

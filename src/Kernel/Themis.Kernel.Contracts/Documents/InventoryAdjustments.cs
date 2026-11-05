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
[CommandRules("R-DOC-01", "R-INV-02", "D-40")]
[CommandAudits("Document", "DocumentLine", "InventoryAdjustment")]
[CommandEvents("INVENTORY_ADJUSTMENT_CREATED")]
[CommandErrors("company_required", "external_reference_too_long", "line_number_not_positive", "line_number_duplicate", "line_item_required", "line_quantity_zero", "line_entered_unit_cost_negative", "line_entered_unit_cost_precision", "value_out_of_range", "line_limit_exceeded", "company_unknown", "warehouse_unknown", "warehouse_other_company", "line_item_unknown", "line_warehouse_required", "line_warehouse_unknown", "line_warehouse_other_company", "quantity_precision")]
[CommandTransition("INVENTORY_ADJUSTMENT", "Draft", "Draft")]
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
[CommandRules("R-DOC-02", "R-API-02", "R-INV-02")]
[CommandAudits("Document", "DocumentLine", "InventoryAdjustment")]
[CommandEvents("INVENTORY_ADJUSTMENT_UPDATED")]
[CommandErrors("external_reference_too_long", "line_number_not_positive", "line_number_duplicate", "line_item_required", "line_quantity_zero", "line_entered_unit_cost_negative", "line_entered_unit_cost_precision", "value_out_of_range", "line_limit_exceeded", "inventory_adjustment_not_found", "version_stale", "document_not_draft", "warehouse_unknown", "warehouse_other_company", "line_item_unknown", "line_warehouse_required", "line_warehouse_unknown", "line_warehouse_other_company", "quantity_precision")]
[CommandTransition("INVENTORY_ADJUSTMENT", "Draft", "Draft")]
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
[CommandRules("R-DOC-01", "R-DOC-06", "R-API-02")]
[CommandAudits("Document")]
[CommandEvents("INVENTORY_ADJUSTMENT_DISCARDED")]
[CommandErrors("inventory_adjustment_not_found", "version_stale", "transition_not_allowed")]
[CommandTransition("INVENTORY_ADJUSTMENT", "Draft", "Discarded")]
[Command("INVENTORY_ADJUSTMENT.DISCARD")]
public sealed record DiscardInventoryAdjustment(Guid Id, long ExpectedVersion) : ICommand<InventoryAdjustmentView>, IVersionedCommand;

/// <summary>
/// INVENTORY_ADJUSTMENT.POST (step 8 brief; Sensitive permission): Draft to Posted on the current version. In one
/// transaction the posting service writes the inventory entries, the balanced journal entry, the cache updates,
/// the audit events and INVENTORY_ADJUSTED, or nothing (R-LED-05, R-LED-07). The guards of the state machine run
/// first (R-DOC-01, R-LED-04, R-INV-05), the stock and cost checks after their rows are locked (R-INV-03, R-LED-08).
/// </summary>
[CommandRules("R-LED-05", "R-LED-08", "R-INV-03", "R-INV-05", "R-PP-02", "R-LED-04", "R-DOC-07", "D-69")]
[CommandAudits("Document", "DocumentLine")]
[CommandEvents("INVENTORY_ADJUSTED")]
[CommandErrors("inventory_adjustment_not_found", "version_stale", "transition_not_allowed", "no_open_lines", "reason_code_required", "item_not_stocked", "item_not_active", "lot_required", "posting_date_required", "fiscal_period_not_open", "entered_unit_cost_on_outbound", "document_currency_not_base_currency", "stock_insufficient", "posting_rule_missing", "posting_rule_account_not_active", "amount_out_of_range", "number_series_missing")]
[CommandTransition("INVENTORY_ADJUSTMENT", "Draft", "Posted")]
[Command("INVENTORY_ADJUSTMENT.POST")]
public sealed record PostInventoryAdjustment(Guid Id, long ExpectedVersion) : ICommand<InventoryAdjustmentView>, IVersionedCommand;

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
    public const string InventoryAdjustmentPost = "/v1/kernel/inventory-adjustments/{id}/post";
    public const string InventoryAdjustment = "/v1/kernel/companies/{companyId}/inventory-adjustments/{id}";

    /// <summary>The command routes the composition root maps through the kernel pipeline (brief, API conventions).</summary>
    public static IReadOnlyList<ICommandRoute> Commands { get; } =
    [
        new CommandRoute<CreateInventoryAdjustment, InventoryAdjustmentView>(InventoryAdjustments),
        new CommandRoute<UpdateInventoryAdjustment, InventoryAdjustmentView>(InventoryAdjustmentUpdate),
        new CommandRoute<DiscardInventoryAdjustment, InventoryAdjustmentView>(InventoryAdjustmentDiscard),
        new CommandRoute<PostInventoryAdjustment, InventoryAdjustmentView>(InventoryAdjustmentPost),
    ];
}

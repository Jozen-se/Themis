using System.Globalization;
using Themis.Kernel.Contracts.Commands;
using Themis.Kernel.Contracts.Documents;
using Themis.Kernel.Contracts.MasterData;
using Themis.Kernel.Contracts.Messaging;
using Themis.Kernel.Contracts.Organisation;
using Themis.Kernel.Contracts.Persistence;
using Themis.Kernel.Contracts.Posting;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Documents;

/// <summary>
/// INVENTORY_ADJUSTMENT.POST (step 8 brief): the guards of the state machine before any lock, in this order, each
/// with its rule and its own code: Draft and the listed transition (R-DOC-01); a posting date in an Open period
/// (R-LED-04; SoftClosed is refused too, since FISCAL_PERIOD.POST_SOFT_CLOSED does not exist in milestone 1); at
/// least one Open line, a reason code, every item Stocked and Active in the company and none lot-tracked (guards
/// without a rule of their own, rejected under R-DOC-01 with a code naming the guard); no entered cost on an outbound
/// line (R-INV-05). POST uses each line's stored warehouse and never re-defaults it from the header. Then lock class 1,
/// the versioned update of the document row (R-LED-08, R-API-02), the posting service (R-LED-05, R-LED-07), and the
/// document and line updates with one audit event each (R-AUD-01 as amended by D-51) and INVENTORY_ADJUSTED (D-44,
/// D-50). Ledger rows, caches and series counters get no audit event of their own; they carry or are traced through
/// the command id (D-51).
/// </summary>
internal sealed class PostInventoryAdjustmentHandler : ICommandHandler<PostInventoryAdjustment, InventoryAdjustmentView>
{
    public const string PostingDateRequired = "posting_date_required";
    public const string FiscalPeriodNotOpen = "fiscal_period_not_open";
    public const string NoOpenLines = "no_open_lines";
    public const string ReasonCodeRequired = "reason_code_required";
    public const string ItemNotStocked = "item_not_stocked";
    public const string ItemNotActive = "item_not_active";
    public const string LotRequired = "lot_required";
    public const string EnteredCostOnOutbound = "entered_unit_cost_on_outbound";

    private readonly IDocumentStateMachine _stateMachine;
    private readonly IOrganisationReader _organisation;
    private readonly IPostingService _posting;

    public PostInventoryAdjustmentHandler(IDocumentStateMachine stateMachine, IOrganisationReader organisation, IPostingService posting)
    {
        _stateMachine = stateMachine;
        _organisation = organisation;
        _posting = posting;
    }

    public async Task<Result<InventoryAdjustmentView>> HandleAsync(PostInventoryAdjustment command, ICommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);
        IUnitOfWork unitOfWork = context.UnitOfWork;

        DocumentRow? before = await DocumentStore.FindAsync(unitOfWork, command.Id, null, cancellationToken);
        if (before is null || before.DocumentType != DocumentTypes.InventoryAdjustment)
        {
            return InventoryAdjustmentDrafts.NotFoundError(command.Id);
        }

        // R-DOC-01: Draft and the listed POST transition; the engine names the event (INVENTORY_ADJUSTED, D-44).
        Result<DocumentTransition> transition = await _stateMachine.ResolveAsync(unitOfWork, before.DocumentType, before.Status, DocumentTypes.PostVerb, cancellationToken);
        if (transition.IsFailure)
        {
            return Result<InventoryAdjustmentView>.Failure(transition.Errors);
        }

        if (InventoryAdjustmentDrafts.VersionCheck(before, command.ExpectedVersion) is { } stale)
        {
            return stale;
        }

        // R-LED-04: a posting date, in an Open period of the company.
        if (before.PostingDate is not { } postingDate)
        {
            return Error.Rule("R-LED-04", PostingDateRequired, "Posting needs a posting date in an open period.");
        }

        Result<FiscalPeriodInfo> period = await _organisation.ResolveFiscalPeriodAsync(unitOfWork, before.CompanyId, new BusinessDate(postingDate), cancellationToken);
        if (period.IsFailure)
        {
            return Result<InventoryAdjustmentView>.Failure(period.Errors);
        }

        if (period.Value.Status != FiscalPeriodStatus.Open)
        {
            return Error.Rule("R-LED-04", FiscalPeriodNotOpen, $"Fiscal period {period.Value.PeriodNumber} of the posting date {postingDate:yyyy-MM-dd} is {period.Value.Status}; posting needs an Open period.");
        }

        // The guards without a rule of their own, under R-DOC-01 with a code naming the guard (Specification, shared rules).
        IReadOnlyList<DocumentLineRow> lines = await DocumentStore.LinesAsync(unitOfWork, before.Id, cancellationToken);
        List<DocumentLineRow> open = [.. lines.Where(line => line.LineStatus == LineStatus.Open)];
        if (open.Count == 0)
        {
            return Error.Rule("R-DOC-01", NoOpenLines, "Posting needs at least one Open line.");
        }

        if (before.ReasonCode is not { } reasonCode)
        {
            return Error.Rule("R-DOC-01", ReasonCodeRequired, "Posting needs a reason code.");
        }

        List<PostingLine> postingLines = [];
        foreach (DocumentLineRow line in open)
        {
            Dictionary<string, string> details = new(StringComparer.Ordinal) { ["lineNumber"] = line.LineNumber.ToString(CultureInfo.InvariantCulture) };
            ItemPostingData? item = await DocumentStore.ItemPostingDataAsync(unitOfWork, line.ItemId, before.CompanyId, cancellationToken);
            if (item is null || item.ItemType != ItemType.Stocked)
            {
                return Error.Rule("R-DOC-01", ItemNotStocked, $"Line {line.LineNumber}: item {line.ItemId} is not a Stocked item.", details);
            }

            if (item.Status != MasterDataStatus.Active)
            {
                return Error.Rule("R-DOC-01", ItemNotActive, $"Line {line.LineNumber}: item {line.ItemId} is {item.Status} in the company.", details);
            }

            if (item.LotTracking != LotTracking.None)
            {
                // Lots are outside milestone 1: a lot-tracked item cannot be posted yet.
                return Error.Rule("R-DOC-01", LotRequired, $"Line {line.LineNumber}: item {line.ItemId} is lot-tracked and needs a lot, which milestone 1 does not support.", details);
            }

            // R-INV-05: an outbound line's cost comes from the algorithm; an entered cost on it is refused.
            if (line.QuantityBase < 0 && line.EnteredUnitCost is not null)
            {
                return Error.Rule("R-INV-05", EnteredCostOnOutbound, $"Line {line.LineNumber}: an outbound line takes its cost from the moving average, not from an entered unit cost.", details);
            }

            // The line's stored warehouse and stock status, never re-defaulted from the header (Specification, DocumentLine.warehouse_id).
            postingLines.Add(new PostingLine(line.Id, line.LineNumber, line.ItemId, item.ItemPostingGroup, line.WarehouseId, line.StockStatus, line.QuantityBase, line.EnteredUnitCost));
        }

        int? minorUnits = await DocumentStore.CurrencyMinorUnitsAsync(unitOfWork, before.Currency, cancellationToken);
        if (minorUnits is null)
        {
            return Error.Validation("currency_unknown", $"Currency {before.Currency} is not a currency.");
        }

        DateTimeOffset now = context.Now;
        UserId by = context.Principal.UserId;

        // R-LED-08, lock class 1: the document row by its versioned update, which increments the version once (R-API-02).
        if (await DocumentStore.LockForPostingAsync(unitOfWork, before.Id, command.ExpectedVersion, now, by, cancellationToken) != 1)
        {
            return Error.PreconditionFailed(InventoryAdjustmentDrafts.VersionStale, $"Inventory adjustment {command.Id} changed since version {command.ExpectedVersion} was read.");
        }

        // R-LED-05, R-LED-07: the posting service writes the ledgers and caches, or the whole command rolls back.
        InventoryAdjustmentPosting posting = new(before.Id, before.CompanyId, CurrencyCode.From(before.Currency), minorUnits.Value, new BusinessDate(postingDate), new BusinessDate(before.DocumentDate), period.Value.Id, period.Value.FiscalYearId, reasonCode, postingLines);
        Result<PostingOutcome> outcome = await _posting.PostInventoryAdjustmentAsync(posting, context, cancellationToken);
        if (outcome.IsFailure)
        {
            return Result<InventoryAdjustmentView>.Failure(outcome.Errors);
        }

        // The document update: status, document number, posted_at and posted_by; the version stays as the lock left it.
        DocumentRow after = before with { Status = transition.Value.To, DocumentNumber = outcome.Value.DocumentNumber, PostedAt = now, PostedBy = by.Value, Version = before.Version + 1 };
        await DocumentStore.MarkPostedAsync(unitOfWork, after, now, by, cancellationToken);
        IReadOnlyDictionary<string, FieldChange> headerChanges = DocumentAudit.DocumentChanges(before, after);
        context.Audit.Record(DocumentAudit.DocumentEntity, after.Id, headerChanges);

        // The line updates: unit cost and cost amount as on the inventory entries (R-DOC-05), one audit event each.
        Dictionary<Guid, PostedLine> posted = outcome.Value.Lines.ToDictionary(line => line.LineId);
        List<DocumentLineEvent> lineEvents = [];
        List<DocumentLineRow> stored = [];
        foreach (DocumentLineRow line in lines)
        {
            if (!posted.TryGetValue(line.Id, out PostedLine? costed))
            {
                stored.Add(line);
                continue;
            }

            DocumentLineRow updated = line with { UnitCost = DecimalRounding.Trim(costed.UnitCost), CostAmount = DecimalRounding.Trim(costed.CostAmount), Version = line.Version + 1 };
            await DocumentStore.SetLineCostAsync(unitOfWork, updated, now, by, cancellationToken);
            IReadOnlyDictionary<string, FieldChange> changes = DocumentAudit.LineChanges(line, updated);
            context.Audit.Record(DocumentAudit.LineEntity, updated.Id, changes);
            lineEvents.Add(new DocumentLineEvent(updated.Id, updated.LineNumber, LineChange.Posted, changes));
            stored.Add(updated);
        }

        // D-44 and D-50: INVENTORY_ADJUSTED, the business name the registry records for POST, with the document's company in the envelope.
        context.Outbox.Publish(transition.Value.EventName, EventNames.DefaultSchemaVersion, DocumentTypes.InventoryAdjustment, after.Id, after.CompanyId,
            new DocumentEvent(after.Id, after.DocumentType, after.Status, after.Version, headerChanges, lineEvents));
        return InventoryAdjustmentDrafts.View(after, stored);
    }
}

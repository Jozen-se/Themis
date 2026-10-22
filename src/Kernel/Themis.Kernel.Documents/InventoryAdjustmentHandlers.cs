using System.Globalization;
using Themis.Kernel.Contracts.Commands;
using Themis.Kernel.Contracts.Documents;
using Themis.Kernel.Contracts.Messaging;
using Themis.Kernel.Contracts.Organisation;
using Themis.Kernel.Contracts.Persistence;
using Themis.Kernel.Contracts.Queries;
using Themis.Kernel.Contracts.Security;
using Themis.Kernel.Contracts.Settings;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Documents;

/// <summary>
/// What the two draft commands share (step 7 brief): resolving the header's warehouse and every line against the
/// company, the stable rejection codes, and the view the commands and the read return. Unknown references and
/// references to another company are 400s with their own code and no ruleId, found inside the transaction and
/// therefore stored as the key's result; the database constraints are the second line of defence (Build Brief,
/// Errors). A quantity finer than the item's base unit allows is R-INV-02 (422, code quantity_precision).
/// </summary>
internal static class InventoryAdjustmentDrafts
{
    public const string NotFound = "inventory_adjustment_not_found";
    public const string NotDraft = "document_not_draft";
    public const string VersionStale = "version_stale";
    public const string LineLimitExceeded = "line_limit_exceeded";

    /// <summary>
    /// RR-54: more lines than the effective document.max_lines at the installation or the company is a 400, read inside the
    /// transaction so the rejection is stored as the key's result, like an unknown reference; it is not R-SET-01.
    /// </summary>
    public static async Task<Error?> LineLimitErrorAsync(ISettingsReader settings, IUnitOfWork unitOfWork, Guid companyId, int lineCount, CancellationToken cancellationToken)
    {
        int limit = await settings.GetIntegerAsync(unitOfWork, SettingKeys.DocumentMaxLines, SettingTarget.ForCompany(companyId), cancellationToken);
        return lineCount > limit
            ? Error.Validation(LineLimitExceeded, $"A document holds at most {limit} lines here; {lineCount} were given.", details: new Dictionary<string, string>(StringComparer.Ordinal) { ["field"] = "lines", ["limit"] = limit.ToString(System.Globalization.CultureInfo.InvariantCulture) })
            : null;
    }

    public static Error NotFoundError(Guid id) => Error.NotFound(NotFound, $"Inventory adjustment {id} does not exist.");

    /// <summary>R-API-02: the version in If-Match must be the row's; the pipeline never stores this rejection.</summary>
    public static Error? VersionCheck(DocumentRow document, long expectedVersion) =>
        document.Version == expectedVersion ? null : Error.PreconditionFailed(VersionStale, $"Inventory adjustment {document.Id} is at version {document.Version}, not {expectedVersion}.");

    public static async Task<Result<Guid?>> ResolveHeaderWarehouseAsync(IUnitOfWork unitOfWork, Guid companyId, Guid? warehouseId, CancellationToken cancellationToken)
    {
        if (warehouseId is not { } warehouse)
        {
            return (Guid?)null;
        }

        Guid? company = await DocumentStore.WarehouseCompanyAsync(unitOfWork, warehouse, cancellationToken);
        if (company is null)
        {
            return Error.Validation("warehouse_unknown", $"Warehouse {warehouse} does not exist.");
        }

        return company == companyId ? warehouse : Error.Validation("warehouse_other_company", $"Warehouse {warehouse} belongs to another company.");
    }

    /// <summary>The lines as they will be stored: item snapshot, base unit, quantity at the unit's decimals, warehouse from the line or the header.</summary>
    public static async Task<Result<IReadOnlyList<DocumentLineRow>>> ResolveLinesAsync(IUnitOfWork unitOfWork, Guid documentId, Guid companyId, Guid? headerWarehouse, IReadOnlyList<InventoryAdjustmentLineInput>? inputs, CancellationToken cancellationToken)
    {
        List<DocumentLineRow> lines = [];
        foreach (InventoryAdjustmentLineInput input in inputs ?? [])
        {
            Dictionary<string, string> details = new(StringComparer.Ordinal) { ["lineNumber"] = input.LineNumber.ToString(CultureInfo.InvariantCulture) };

            // D-43: the item must be set up in the document's company; the line references its ItemCompany row.
            ItemSnapshot? item = await DocumentStore.ItemInCompanyAsync(unitOfWork, input.ItemId, companyId, cancellationToken);
            if (item is null)
            {
                return Error.Validation("line_item_unknown", $"Line {input.LineNumber}: item {input.ItemId} is not set up in company {companyId}.", details: With(details, "itemId"));
            }

            // R-INV-02: a quantity in the base unit carries at most the unit's decimals; a finer value is a rule violation (422).
            if (!Quantity.FitsUnit(input.Quantity, item.UnitDecimals))
            {
                return Error.Rule("R-INV-02", Quantity.PrecisionCode, $"Line {input.LineNumber}: quantity {input.Quantity} has more decimals than unit {item.BaseUnit} allows ({item.UnitDecimals}).", With(details, "quantity"));
            }

            Guid? warehouseId = input.WarehouseId ?? headerWarehouse;
            if (warehouseId is not { } warehouse)
            {
                return Error.Validation("line_warehouse_required", $"Line {input.LineNumber}: a stock line names a warehouse, on the line or on the header.", details: With(details, "warehouseId"));
            }

            if (input.WarehouseId is not null)
            {
                Guid? warehouseCompany = await DocumentStore.WarehouseCompanyAsync(unitOfWork, warehouse, cancellationToken);
                if (warehouseCompany is null)
                {
                    return Error.Validation("line_warehouse_unknown", $"Line {input.LineNumber}: warehouse {warehouse} does not exist.", details: With(details, "warehouseId"));
                }

                if (warehouseCompany != companyId)
                {
                    return Error.Validation("line_warehouse_other_company", $"Line {input.LineNumber}: warehouse {warehouse} belongs to another company.", details: With(details, "warehouseId"));
                }
            }

            // Milestone 1: the base unit only, so the base quantity is the quantity (factor 1); costs are set at posting (step 8).
            decimal quantity = DecimalRounding.AtScale(input.Quantity, item.UnitDecimals);
            lines.Add(new DocumentLineRow(EntityId.New(), documentId, input.LineNumber, LineStatus.Open, input.ItemId, item.Description, quantity, item.BaseUnit, quantity, warehouse, input.StockStatus,
                input.EnteredUnitCost is { } cost ? DecimalRounding.Trim(cost) : null, UnitCost: null, CostAmount: null, Version: 1, item.UnitDecimals));
        }

        return lines;
    }

    public static InventoryAdjustmentView View(DocumentRow document, IEnumerable<DocumentLineRow> lines) => new(
        document.Id,
        document.CompanyId,
        document.DocumentType,
        document.Status,
        document.DocumentNumber,
        document.DraftReference,
        new BusinessDate(document.DocumentDate),
        document.PostingDate is { } postingDate ? new BusinessDate(postingDate) : null,
        CurrencyCode.From(document.Currency),
        document.WarehouseId,
        document.ExternalReference,
        document.ReasonCode,
        document.PostedAt,
        document.PostedBy,
        document.Version,
        // Specification, shared rules: a Cancelled line stays stored and is left out of the draft the API shows.
        [.. lines.Where(line => line.LineStatus != LineStatus.Cancelled).OrderBy(line => line.LineNumber).Select(LineView)]);

    public static InventoryAdjustmentLineView LineView(DocumentLineRow line) => new(
        line.Id, line.LineNumber, line.LineStatus, line.ItemId, line.Description, line.Quantity, UnitCode.From(line.UnitCode), line.QuantityBase, line.WarehouseId, line.StockStatus, line.EnteredUnitCost, line.UnitCost, line.CostAmount, line.Version);

    private static Dictionary<string, string> With(Dictionary<string, string> details, string field)
    {
        details["field"] = field;
        return details;
    }
}

/// <summary>
/// INVENTORY_ADJUSTMENT.CREATE (step 7 brief): a Draft with its extension row and lines, the currency the company's
/// base currency, the draft reference the document id (R-DOC-04); allowed because the registry lists CREATE into
/// Draft (R-DOC-01). One audit event per row written (R-AUD-01) and one outbox event for the document, named by the
/// registry (D-44), with the document's company in the envelope (D-50).
/// </summary>
internal sealed class CreateInventoryAdjustmentHandler : ICommandHandler<CreateInventoryAdjustment, InventoryAdjustmentView>
{
    private readonly IDocumentStateMachine _stateMachine;
    private readonly IOrganisationReader _organisation;
    private readonly ISettingsReader _settings;

    public CreateInventoryAdjustmentHandler(IDocumentStateMachine stateMachine, IOrganisationReader organisation, ISettingsReader settings)
    {
        _stateMachine = stateMachine;
        _organisation = organisation;
        _settings = settings;
    }

    public async Task<Result<InventoryAdjustmentView>> HandleAsync(CreateInventoryAdjustment command, ICommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);
        IUnitOfWork unitOfWork = context.UnitOfWork;

        // R-DOC-01: CREATE is a transition like any other; the engine reads it from the registry.
        Result<DocumentTransition> transition = await _stateMachine.ResolveAsync(unitOfWork, DocumentTypes.InventoryAdjustment, DocumentStatus.Draft, DocumentTypes.CreateVerb, cancellationToken);
        if (transition.IsFailure)
        {
            return Result<InventoryAdjustmentView>.Failure(transition.Errors);
        }

        CompanyInfo? company = await _organisation.GetCompanyAsync(unitOfWork, command.CompanyId, cancellationToken);
        if (company is null)
        {
            return Error.Validation("company_unknown", $"Company {command.CompanyId} does not exist.");
        }

        if (await InventoryAdjustmentDrafts.LineLimitErrorAsync(_settings, unitOfWork, command.CompanyId, command.Lines?.Count ?? 0, cancellationToken) is { } overLimit)
        {
            return overLimit;
        }

        Result<Guid?> warehouse = await InventoryAdjustmentDrafts.ResolveHeaderWarehouseAsync(unitOfWork, command.CompanyId, command.WarehouseId, cancellationToken);
        if (warehouse.IsFailure)
        {
            return Result<InventoryAdjustmentView>.Failure(warehouse.Errors);
        }

        Guid id = EntityId.New();
        Result<IReadOnlyList<DocumentLineRow>> lines = await InventoryAdjustmentDrafts.ResolveLinesAsync(unitOfWork, id, command.CompanyId, warehouse.Value, command.Lines, cancellationToken);
        if (lines.IsFailure)
        {
            return Result<InventoryAdjustmentView>.Failure(lines.Errors);
        }

        // The currency is the company's base currency, set by the system; the draft reference is the id as text (Specification, Document).
        DocumentRow document = new(id, command.CompanyId, DocumentTypes.InventoryAdjustment, transition.Value.To, DocumentNumber: null, id.ToString(), command.DocumentDate.Value, command.PostingDate?.Value,
            company.BaseCurrency.Value, warehouse.Value, Trimmed(command.ExternalReference), PostedAt: null, PostedBy: null, Version: 1, command.ReasonCode, ExtensionVersion: 1);

        await DocumentStore.InsertDocumentAsync(unitOfWork, document, context.Now, context.Principal.UserId, cancellationToken);
        await DocumentStore.InsertInventoryAdjustmentAsync(unitOfWork, document, context.Now, context.Principal.UserId, cancellationToken);
        List<DocumentLineEvent> lineEvents = [];
        foreach (DocumentLineRow line in lines.Value)
        {
            await DocumentStore.InsertLineAsync(unitOfWork, command.CompanyId, line, context.Now, context.Principal.UserId, cancellationToken);
            IReadOnlyDictionary<string, FieldChange> changes = DocumentAudit.LineChanges(null, line);
            context.Audit.Record(DocumentAudit.LineEntity, line.Id, changes);
            lineEvents.Add(new DocumentLineEvent(line.Id, line.LineNumber, LineChange.Inserted, changes));
        }

        // R-AUD-01 and D-44: one audit event per row written, one outbox event for the document, named by the registry.
        IReadOnlyDictionary<string, FieldChange> headerChanges = DocumentAudit.DocumentChanges(null, document);
        context.Audit.Record(DocumentAudit.DocumentEntity, id, headerChanges);
        context.Audit.Record(DocumentAudit.InventoryAdjustmentEntity, id, DocumentAudit.InventoryAdjustmentChanges(null, document));
        context.Outbox.Publish(transition.Value.EventName, EventNames.DefaultSchemaVersion, DocumentTypes.InventoryAdjustment, id, command.CompanyId,
            new DocumentEvent(id, DocumentTypes.InventoryAdjustment, document.Status, document.Version, Merge(headerChanges, DocumentAudit.InventoryAdjustmentChanges(null, document)), lineEvents));
        return InventoryAdjustmentDrafts.View(document, lines.Value);
    }

    internal static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>The header's and the extension's changed fields in one payload; the extension's fields never collide with the header's except version, which is the header's.</summary>
    internal static IReadOnlyDictionary<string, FieldChange> Merge(IReadOnlyDictionary<string, FieldChange> header, IReadOnlyDictionary<string, FieldChange> extension)
    {
        Dictionary<string, FieldChange> merged = new(header, StringComparer.Ordinal);
        foreach ((string field, FieldChange change) in extension)
        {
            merged.TryAdd(field, change);
        }

        return merged;
    }
}

/// <summary>
/// INVENTORY_ADJUSTMENT.UPDATE (step 7 brief): the whole draft on the current version. Only a Draft is editable
/// (R-DOC-02, checked before the engine), the registry must list UPDATE from Draft (R-DOC-01), and the version in
/// If-Match must be the row's (R-API-02). Lines are matched by line number: a new number inserts, an existing one
/// updates when its content differs, a line left out becomes Cancelled, a cancelled number given again reopens
/// the line with the new values; nothing is deleted. One audit event per changed row (document, each line
/// inserted, changed, cancelled or reopened, the extension when the reason code changes), one outbox event.
/// </summary>
internal sealed class UpdateInventoryAdjustmentHandler : ICommandHandler<UpdateInventoryAdjustment, InventoryAdjustmentView>
{
    private readonly IDocumentStateMachine _stateMachine;
    private readonly ISettingsReader _settings;

    public UpdateInventoryAdjustmentHandler(IDocumentStateMachine stateMachine, ISettingsReader settings)
    {
        _stateMachine = stateMachine;
        _settings = settings;
    }

    public async Task<Result<InventoryAdjustmentView>> HandleAsync(UpdateInventoryAdjustment command, ICommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);
        IUnitOfWork unitOfWork = context.UnitOfWork;

        DocumentRow? before = await DocumentStore.FindAsync(unitOfWork, command.Id, null, cancellationToken);
        if (before is null || before.DocumentType != DocumentTypes.InventoryAdjustment)
        {
            return InventoryAdjustmentDrafts.NotFoundError(command.Id);
        }

        // R-DOC-02: only Draft documents are editable; checked first, so a Discarded draft is refused with this rule, not R-DOC-01.
        if (before.Status != DocumentStatus.Draft)
        {
            return Error.Rule("R-DOC-02", InventoryAdjustmentDrafts.NotDraft, $"Inventory adjustment {command.Id} is {before.Status}; only a Draft is editable.");
        }

        Result<DocumentTransition> transition = await _stateMachine.ResolveAsync(unitOfWork, before.DocumentType, before.Status, DocumentTypes.UpdateVerb, cancellationToken);
        if (transition.IsFailure)
        {
            return Result<InventoryAdjustmentView>.Failure(transition.Errors);
        }

        if (InventoryAdjustmentDrafts.VersionCheck(before, command.ExpectedVersion) is { } stale)
        {
            return stale;
        }

        if (await InventoryAdjustmentDrafts.LineLimitErrorAsync(_settings, unitOfWork, before.CompanyId, command.Lines?.Count ?? 0, cancellationToken) is { } overLimit)
        {
            return overLimit;
        }

        Result<Guid?> warehouse = await InventoryAdjustmentDrafts.ResolveHeaderWarehouseAsync(unitOfWork, before.CompanyId, command.WarehouseId, cancellationToken);
        if (warehouse.IsFailure)
        {
            return Result<InventoryAdjustmentView>.Failure(warehouse.Errors);
        }

        Result<IReadOnlyList<DocumentLineRow>> wanted = await InventoryAdjustmentDrafts.ResolveLinesAsync(unitOfWork, before.Id, before.CompanyId, warehouse.Value, command.Lines, cancellationToken);
        if (wanted.IsFailure)
        {
            return Result<InventoryAdjustmentView>.Failure(wanted.Errors);
        }

        DateTimeOffset now = context.Now;
        UserId by = context.Principal.UserId;

        // The versioned update of the header is the command's own-row lock (R-LED-08, class 1); 0 rows means a concurrent change since the read (R-API-02).
        DocumentRow after = before with
        {
            Status = transition.Value.To,
            DocumentDate = command.DocumentDate.Value,
            PostingDate = command.PostingDate?.Value,
            WarehouseId = warehouse.Value,
            ExternalReference = CreateInventoryAdjustmentHandler.Trimmed(command.ExternalReference),
            Version = before.Version + 1,
            ReasonCode = command.ReasonCode,
            ExtensionVersion = before.ReasonCode == command.ReasonCode ? before.ExtensionVersion : before.ExtensionVersion + 1,
        };
        if (await DocumentStore.UpdateDocumentAsync(unitOfWork, after, command.ExpectedVersion, now, by, cancellationToken) != 1)
        {
            return Error.PreconditionFailed(InventoryAdjustmentDrafts.VersionStale, $"Inventory adjustment {command.Id} changed since version {command.ExpectedVersion} was read.");
        }

        IReadOnlyDictionary<string, FieldChange> headerChanges = DocumentAudit.DocumentChanges(before, after);
        context.Audit.Record(DocumentAudit.DocumentEntity, after.Id, headerChanges);
        IReadOnlyDictionary<string, FieldChange> extensionChanges = DocumentAudit.InventoryAdjustmentChanges(before, after);
        if (extensionChanges.Count > 0)
        {
            await DocumentStore.UpdateInventoryAdjustmentAsync(unitOfWork, after, now, by, cancellationToken);
            context.Audit.Record(DocumentAudit.InventoryAdjustmentEntity, after.Id, extensionChanges);
        }

        // Specification, shared rules: lines matched by line number; nothing deleted.
        IReadOnlyList<DocumentLineRow> existing = await DocumentStore.LinesAsync(unitOfWork, before.Id, cancellationToken);
        Dictionary<int, DocumentLineRow> byNumber = existing.ToDictionary(line => line.LineNumber);
        List<DocumentLineEvent> lineEvents = [];
        List<DocumentLineRow> stored = [];
        foreach (DocumentLineRow line in wanted.Value)
        {
            if (!byNumber.Remove(line.LineNumber, out DocumentLineRow? current))
            {
                await DocumentStore.InsertLineAsync(unitOfWork, before.CompanyId, line, now, by, cancellationToken);
                Record(context, lineEvents, null, line, LineChange.Inserted);
                stored.Add(line);
                continue;
            }

            if (current.LineStatus == LineStatus.Cancelled)
            {
                DocumentLineRow reopened = line with { Id = current.Id, Version = current.Version + 1 };
                await DocumentStore.UpdateLineAsync(unitOfWork, reopened, now, by, cancellationToken);
                Record(context, lineEvents, current, reopened, LineChange.Reopened);
                stored.Add(reopened);
                continue;
            }

            DocumentLineRow kept = line with { Id = current.Id, LineStatus = current.LineStatus, UnitCost = current.UnitCost, CostAmount = current.CostAmount, Version = current.Version };
            if (DocumentAudit.SameContent(current, kept))
            {
                stored.Add(current);
                continue;
            }

            DocumentLineRow changed = kept with { Version = current.Version + 1 };
            await DocumentStore.UpdateLineAsync(unitOfWork, changed, now, by, cancellationToken);
            Record(context, lineEvents, current, changed, LineChange.Changed);
            stored.Add(changed);
        }

        foreach (DocumentLineRow left in byNumber.Values.Where(line => line.LineStatus != LineStatus.Cancelled))
        {
            DocumentLineRow cancelled = left with { LineStatus = LineStatus.Cancelled, Version = left.Version + 1 };
            await DocumentStore.UpdateLineAsync(unitOfWork, cancelled, now, by, cancellationToken);
            Record(context, lineEvents, left, cancelled, LineChange.Cancelled);
        }

        context.Outbox.Publish(transition.Value.EventName, EventNames.DefaultSchemaVersion, DocumentTypes.InventoryAdjustment, after.Id, after.CompanyId,
            new DocumentEvent(after.Id, after.DocumentType, after.Status, after.Version, CreateInventoryAdjustmentHandler.Merge(headerChanges, extensionChanges), lineEvents));
        return InventoryAdjustmentDrafts.View(after, stored);
    }

    private static void Record(ICommandContext context, List<DocumentLineEvent> events, DocumentLineRow? before, DocumentLineRow after, LineChange change)
    {
        IReadOnlyDictionary<string, FieldChange> changes = DocumentAudit.LineChanges(before, after);
        context.Audit.Record(DocumentAudit.LineEntity, after.Id, changes);
        events.Add(new DocumentLineEvent(after.Id, after.LineNumber, change, changes));
    }
}

/// <summary>
/// INVENTORY_ADJUSTMENT.DISCARD (step 7 brief): Draft to Discarded on the current version, allowed only where the
/// registry lists it (R-DOC-01, so a second DISCARD is refused with that rule); the row and its lines stay
/// (R-DOC-06). One audit event for the document, one outbox event named by the registry.
/// </summary>
internal sealed class DiscardInventoryAdjustmentHandler : ICommandHandler<DiscardInventoryAdjustment, InventoryAdjustmentView>
{
    private readonly IDocumentStateMachine _stateMachine;

    public DiscardInventoryAdjustmentHandler(IDocumentStateMachine stateMachine)
    {
        _stateMachine = stateMachine;
    }

    public async Task<Result<InventoryAdjustmentView>> HandleAsync(DiscardInventoryAdjustment command, ICommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);
        IUnitOfWork unitOfWork = context.UnitOfWork;

        DocumentRow? before = await DocumentStore.FindAsync(unitOfWork, command.Id, null, cancellationToken);
        if (before is null || before.DocumentType != DocumentTypes.InventoryAdjustment)
        {
            return InventoryAdjustmentDrafts.NotFoundError(command.Id);
        }

        Result<DocumentTransition> transition = await _stateMachine.ResolveAsync(unitOfWork, before.DocumentType, before.Status, DocumentTypes.DiscardVerb, cancellationToken);
        if (transition.IsFailure)
        {
            return Result<InventoryAdjustmentView>.Failure(transition.Errors);
        }

        if (InventoryAdjustmentDrafts.VersionCheck(before, command.ExpectedVersion) is { } stale)
        {
            return stale;
        }

        DocumentRow after = before with { Status = transition.Value.To, Version = before.Version + 1 };
        if (await DocumentStore.UpdateDocumentAsync(unitOfWork, after, command.ExpectedVersion, context.Now, context.Principal.UserId, cancellationToken) != 1)
        {
            return Error.PreconditionFailed(InventoryAdjustmentDrafts.VersionStale, $"Inventory adjustment {command.Id} changed since version {command.ExpectedVersion} was read.");
        }

        IReadOnlyDictionary<string, FieldChange> changes = DocumentAudit.DocumentChanges(before, after);
        context.Audit.Record(DocumentAudit.DocumentEntity, after.Id, changes);
        context.Outbox.Publish(transition.Value.EventName, EventNames.DefaultSchemaVersion, DocumentTypes.InventoryAdjustment, after.Id, after.CompanyId,
            new DocumentEvent(after.Id, after.DocumentType, after.Status, after.Version, changes, []));
        IReadOnlyList<DocumentLineRow> lines = await DocumentStore.LinesAsync(unitOfWork, after.Id, cancellationToken);
        return InventoryAdjustmentDrafts.View(after, lines);
    }
}

/// <summary>
/// The scope of a command on an existing inventory adjustment: the document's company (Build Brief, Authorisation:
/// scope-checked after the record is loaded, inside the transaction). A missing document is 404.
/// </summary>
internal sealed class InventoryAdjustmentScopeResolver : IScopeResolver<UpdateInventoryAdjustment>, IScopeResolver<DiscardInventoryAdjustment>, IScopeResolver<PostInventoryAdjustment>
{
    public Task<Result<RequestScope>> ResolveAsync(PostInventoryAdjustment request, IUnitOfWork unitOfWork, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ResolveAsync(request.Id, unitOfWork, cancellationToken);
    }

    public Task<Result<RequestScope>> ResolveAsync(UpdateInventoryAdjustment request, IUnitOfWork unitOfWork, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ResolveAsync(request.Id, unitOfWork, cancellationToken);
    }

    public Task<Result<RequestScope>> ResolveAsync(DiscardInventoryAdjustment request, IUnitOfWork unitOfWork, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ResolveAsync(request.Id, unitOfWork, cancellationToken);
    }

    private static async Task<Result<RequestScope>> ResolveAsync(Guid id, IUnitOfWork unitOfWork, CancellationToken cancellationToken)
    {
        DocumentRow? document = await DocumentStore.FindAsync(unitOfWork, id, null, cancellationToken);
        return document is null || document.DocumentType != DocumentTypes.InventoryAdjustment
            ? InventoryAdjustmentDrafts.NotFoundError(id)
            : RequestScope.ForCompany(document.CompanyId);
    }
}

/// <summary>GET inventory adjustment by id in a company (INVENTORY_ADJUSTMENT.READ, D-47): header, reason code and the non-Cancelled lines; the ETag is the version (R-API-02).</summary>
internal sealed class GetInventoryAdjustmentHandler : IQueryHandler<GetInventoryAdjustment, InventoryAdjustmentView>
{
    public async Task<Result<InventoryAdjustmentView>> HandleAsync(GetInventoryAdjustment query, IQueryContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(context);
        DocumentRow? document = await DocumentStore.FindAsync(context.UnitOfWork, query.Id, query.CompanyId, cancellationToken);
        if (document is null || document.DocumentType != DocumentTypes.InventoryAdjustment)
        {
            return Error.NotFound(InventoryAdjustmentDrafts.NotFound, $"Inventory adjustment {query.Id} does not exist in company {query.CompanyId}.");
        }

        IReadOnlyList<DocumentLineRow> lines = await DocumentStore.LinesAsync(context.UnitOfWork, document.Id, cancellationToken);
        return InventoryAdjustmentDrafts.View(document, lines);
    }
}

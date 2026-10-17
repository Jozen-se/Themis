using System.Globalization;
using Themis.Kernel.Contracts.Documents;
using Themis.Kernel.Contracts.Persistence;

namespace Themis.Kernel.Documents;

/// <summary>How a line changed in an UPDATE (Specification, shared rules: matched by line number).</summary>
internal enum LineChange
{
    Inserted,
    Changed,
    Cancelled,
    Reopened,
}

/// <summary>One line in the document's outbox event: its id, number, what happened to it and the changed fields.</summary>
internal sealed record DocumentLineEvent(Guid Id, int LineNumber, LineChange Change, IReadOnlyDictionary<string, FieldChange> Changes);

/// <summary>
/// The payload of a document's outbox event (D-44, Specification Event names: the id and the changed fields): the
/// header's changed fields, the extension's, and the lines the command inserted, changed, cancelled or reopened.
/// </summary>
internal sealed record DocumentEvent(Guid Id, string DocumentType, DocumentStatus Status, long Version, IReadOnlyDictionary<string, FieldChange> Changes, IReadOnlyList<DocumentLineEvent> Lines);

/// <summary>The changed fields of the audit events (R-AUD-01): before and after as text, only the fields that differ.</summary>
internal static class DocumentAudit
{
    public const string DocumentEntity = "Document";
    public const string LineEntity = "DocumentLine";
    public const string InventoryAdjustmentEntity = "InventoryAdjustment";

    public static IReadOnlyDictionary<string, FieldChange> DocumentChanges(DocumentRow? before, DocumentRow after)
    {
        Dictionary<string, FieldChange> changes = new(StringComparer.Ordinal);
        Add(changes, "companyId", before?.CompanyId.ToString(), after.CompanyId.ToString());
        Add(changes, "documentType", before?.DocumentType, after.DocumentType);
        Add(changes, "status", before?.Status.ToString(), after.Status.ToString());
        Add(changes, "documentNumber", before?.DocumentNumber, after.DocumentNumber);
        Add(changes, "draftReference", before?.DraftReference, after.DraftReference);
        Add(changes, "documentDate", Text(before?.DocumentDate), Text(after.DocumentDate));
        Add(changes, "postingDate", Text(before?.PostingDate), Text(after.PostingDate));
        Add(changes, "currency", before?.Currency, after.Currency);
        Add(changes, "warehouseId", before?.WarehouseId?.ToString(), after.WarehouseId?.ToString());
        Add(changes, "externalReference", before?.ExternalReference, after.ExternalReference);
        Add(changes, "version", Text(before?.Version), Text(after.Version));
        return changes;
    }

    public static IReadOnlyDictionary<string, FieldChange> InventoryAdjustmentChanges(DocumentRow? before, DocumentRow after)
    {
        Dictionary<string, FieldChange> changes = new(StringComparer.Ordinal);
        Add(changes, "companyId", before?.CompanyId.ToString(), after.CompanyId.ToString());
        Add(changes, "reasonCode", before?.ReasonCode?.ToString(), after.ReasonCode?.ToString());
        Add(changes, "version", Text(before?.ExtensionVersion), Text(after.ExtensionVersion));
        return changes;
    }

    public static IReadOnlyDictionary<string, FieldChange> LineChanges(DocumentLineRow? before, DocumentLineRow after)
    {
        Dictionary<string, FieldChange> changes = new(StringComparer.Ordinal);
        Add(changes, "documentId", before?.DocumentId.ToString(), after.DocumentId.ToString());
        Add(changes, "lineNumber", Text(before?.LineNumber), Text(after.LineNumber));
        Add(changes, "lineStatus", before?.LineStatus.ToString(), after.LineStatus.ToString());
        Add(changes, "itemId", before?.ItemId.ToString(), after.ItemId.ToString());
        Add(changes, "description", before?.Description, after.Description);
        Add(changes, "quantity", Text(before?.Quantity), Text(after.Quantity));
        Add(changes, "unitCode", before?.UnitCode, after.UnitCode);
        Add(changes, "quantityBase", Text(before?.QuantityBase), Text(after.QuantityBase));
        Add(changes, "warehouseId", before?.WarehouseId.ToString(), after.WarehouseId.ToString());
        Add(changes, "stockStatus", before?.StockStatus.ToString(), after.StockStatus.ToString());
        Add(changes, "enteredUnitCost", Text(before?.EnteredUnitCost), Text(after.EnteredUnitCost));
        Add(changes, "version", Text(before?.Version), Text(after.Version));
        return changes;
    }

    /// <summary>The fields an UPDATE may change on a line; equal values leave the line untouched.</summary>
    public static bool SameContent(DocumentLineRow before, DocumentLineRow after) =>
        before.ItemId == after.ItemId
        && string.Equals(before.Description, after.Description, StringComparison.Ordinal)
        && before.Quantity == after.Quantity
        && string.Equals(before.UnitCode, after.UnitCode, StringComparison.Ordinal)
        && before.WarehouseId == after.WarehouseId
        && before.StockStatus == after.StockStatus
        && before.EnteredUnitCost == after.EnteredUnitCost;

    private static void Add(Dictionary<string, FieldChange> changes, string field, string? before, string? after)
    {
        if (!string.Equals(before, after, StringComparison.Ordinal))
        {
            changes[field] = new FieldChange(before, after);
        }
    }

    private static string? Text(decimal? value) => value?.ToString(CultureInfo.InvariantCulture);

    private static string? Text(long? value) => value?.ToString(CultureInfo.InvariantCulture);

    private static string? Text(DateOnly? value) => value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

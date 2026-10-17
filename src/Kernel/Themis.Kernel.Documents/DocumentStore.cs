using System.Data.Common;
using Themis.Kernel.Contracts.Documents;
using Themis.Kernel.Contracts.Persistence;
using Themis.Kernel.Contracts.Posting;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Documents;

/// <summary>A document header as stored, with its INVENTORY_ADJUSTMENT extension when it has one.</summary>
internal sealed record DocumentRow(
    Guid Id,
    Guid CompanyId,
    string DocumentType,
    DocumentStatus Status,
    string? DocumentNumber,
    string DraftReference,
    DateOnly DocumentDate,
    DateOnly? PostingDate,
    string Currency,
    Guid? WarehouseId,
    string? ExternalReference,
    DateTimeOffset? PostedAt,
    Guid? PostedBy,
    long Version,
    ReasonCode? ReasonCode,
    long ExtensionVersion);

/// <summary>An Item line as stored, with its unit's decimals for presentation (D-37).</summary>
internal sealed record DocumentLineRow(
    Guid Id,
    Guid DocumentId,
    int LineNumber,
    LineStatus LineStatus,
    Guid ItemId,
    string Description,
    decimal Quantity,
    string UnitCode,
    decimal QuantityBase,
    Guid WarehouseId,
    StockStatus StockStatus,
    decimal? EnteredUnitCost,
    decimal? UnitCost,
    decimal? CostAmount,
    long Version,
    int UnitDecimals);

/// <summary>What a line takes from its item in the document's company (D-43): the description snapshot, the base unit and its decimals.</summary>
internal sealed record ItemSnapshot(string Description, string BaseUnit, int UnitDecimals);

/// <summary>
/// Raw SQL on the command's unit of work as themis_kernel (R-DB-01): reads and writes of document headers, lines
/// and the INVENTORY_ADJUSTMENT extension, and the reference lookups the draft commands need. The kernel data
/// context stays in Themis.Kernel.Persistence; this capability reaches the tables through the unit of work only
/// (ARCH-08). Nothing here deletes (R-DOC-06).
/// </summary>
internal static class DocumentStore
{
    private const string DocumentColumns = "d.id, d.company_id, d.document_type, d.status, d.document_number, d.draft_reference, d.document_date, d.posting_date, d.currency, d.warehouse_id, d.external_reference, d.posted_at, d.posted_by, d.version, a.reason_code, a.version";
    private const string LineColumns = "l.id, l.document_id, l.line_number, l.line_status, l.item_id, l.description, l.quantity, l.unit_code, l.quantity_base, l.warehouse_id, l.stock_status, l.entered_unit_cost, l.unit_cost, l.cost_amount, l.version, u.decimals";

    public static async Task<DocumentRow?> FindAsync(IUnitOfWork unitOfWork, Guid id, Guid? companyId, CancellationToken cancellationToken)
    {
        string condition = companyId is null ? "d.id = @id" : "d.id = @id AND d.company_id = @company";
        IReadOnlyList<DocumentRow> rows = await unitOfWork.QueryAsync(DatabaseRole.Kernel,
            $"""
            SELECT {DocumentColumns}
            FROM kernel.document d
            LEFT JOIN kernel.inventory_adjustment a ON a.id = d.id
            WHERE {condition}
            """,
            new Dictionary<string, object?> { ["id"] = id, ["company"] = companyId },
            ReadDocument,
            cancellationToken);
        return rows.Count == 0 ? null : rows[0];
    }

    /// <summary>Every line of the document, Cancelled ones included, in line number order.</summary>
    public static Task<IReadOnlyList<DocumentLineRow>> LinesAsync(IUnitOfWork unitOfWork, Guid documentId, CancellationToken cancellationToken) =>
        unitOfWork.QueryAsync(DatabaseRole.Kernel,
            $"""
            SELECT {LineColumns}
            FROM kernel.document_line l
            JOIN kernel.unit_of_measure u ON u.code = l.unit_code
            WHERE l.document_id = @document
            ORDER BY l.line_number
            """,
            new Dictionary<string, object?> { ["document"] = documentId },
            ReadLine,
            cancellationToken);

    /// <summary>The item's description, base unit and the unit's decimals, if the item is set up in the company (its ItemCompany row, D-43); else null.</summary>
    public static async Task<ItemSnapshot?> ItemInCompanyAsync(IUnitOfWork unitOfWork, Guid itemId, Guid companyId, CancellationToken cancellationToken)
    {
        IReadOnlyList<ItemSnapshot> rows = await unitOfWork.QueryAsync(DatabaseRole.Kernel,
            """
            SELECT i.description, i.base_unit, u.decimals
            FROM kernel.item i
            JOIN kernel.item_company c ON c.item_id = i.id AND c.company_id = @company
            JOIN kernel.unit_of_measure u ON u.code = i.base_unit
            WHERE i.id = @item
            """,
            new Dictionary<string, object?> { ["item"] = itemId, ["company"] = companyId },
            reader => new ItemSnapshot(reader.GetString(0), reader.GetString(1), reader.GetInt32(2)),
            cancellationToken);
        return rows.Count == 0 ? null : rows[0];
    }

    /// <summary>The company a warehouse belongs to, or null when the warehouse does not exist.</summary>
    public static async Task<Guid?> WarehouseCompanyAsync(IUnitOfWork unitOfWork, Guid warehouseId, CancellationToken cancellationToken) =>
        await unitOfWork.ScalarAsync(DatabaseRole.Kernel, "SELECT company_id FROM kernel.warehouse WHERE id = @id", new Dictionary<string, object?> { ["id"] = warehouseId }, cancellationToken) as Guid?;

    public static Task InsertDocumentAsync(IUnitOfWork unitOfWork, DocumentRow document, DateTimeOffset now, UserId by, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteAsync(DatabaseRole.Kernel,
            """
            INSERT INTO kernel.document (id, company_id, document_type, status, document_number, draft_reference, document_date, posting_date, currency, warehouse_id, external_reference, created_at, created_by, updated_at, updated_by, version)
            VALUES (@id, @company, @type, @status, @number, @reference, @date, @postingDate, @currency, @warehouse, @external, @now, @by, @now, @by, @version)
            """,
            new Dictionary<string, object?>
            {
                ["id"] = document.Id,
                ["company"] = document.CompanyId,
                ["type"] = document.DocumentType,
                ["status"] = document.Status.ToString(),
                ["number"] = document.DocumentNumber,
                ["reference"] = document.DraftReference,
                ["date"] = document.DocumentDate,
                ["postingDate"] = document.PostingDate,
                ["currency"] = document.Currency,
                ["warehouse"] = document.WarehouseId,
                ["external"] = document.ExternalReference,
                ["now"] = now,
                ["by"] = by.Value,
                ["version"] = document.Version,
            },
            cancellationToken);

    public static Task InsertInventoryAdjustmentAsync(IUnitOfWork unitOfWork, DocumentRow document, DateTimeOffset now, UserId by, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteAsync(DatabaseRole.Kernel,
            """
            INSERT INTO kernel.inventory_adjustment (id, company_id, reason_code, created_at, created_by, updated_at, updated_by, version)
            VALUES (@id, @company, @reason, @now, @by, @now, @by, @version)
            """,
            new Dictionary<string, object?>
            {
                ["id"] = document.Id,
                ["company"] = document.CompanyId,
                ["reason"] = document.ReasonCode?.ToString(),
                ["now"] = now,
                ["by"] = by.Value,
                ["version"] = document.ExtensionVersion,
            },
            cancellationToken);

    public static Task InsertLineAsync(IUnitOfWork unitOfWork, Guid companyId, DocumentLineRow line, DateTimeOffset now, UserId by, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteAsync(DatabaseRole.Kernel,
            """
            INSERT INTO kernel.document_line (id, company_id, document_id, line_number, line_type, item_id, description, quantity, unit_code, quantity_base, warehouse_id, stock_status, entered_unit_cost, unit_cost, cost_amount, line_status, created_at, created_by, updated_at, updated_by, version)
            VALUES (@id, @company, @document, @number, @lineType, @item, @description, @quantity, @unit, @quantityBase, @warehouse, @stockStatus, @enteredCost, @unitCost, @costAmount, @status, @now, @by, @now, @by, @version)
            """,
            LineParameters(line, now, by, new Dictionary<string, object?> { ["company"] = companyId, ["document"] = line.DocumentId, ["lineType"] = DocumentLineType.Item.ToString() }),
            cancellationToken);

    /// <summary>The versioned update of the header (R-API-02, lock class 1 of R-LED-08): 0 rows means the version moved since it was read.</summary>
    public static Task<int> UpdateDocumentAsync(IUnitOfWork unitOfWork, DocumentRow document, long expectedVersion, DateTimeOffset now, UserId by, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteAsync(DatabaseRole.Kernel,
            """
            UPDATE kernel.document
            SET status = @status, document_date = @date, posting_date = @postingDate, warehouse_id = @warehouse, external_reference = @external, updated_at = @now, updated_by = @by, version = @version
            WHERE id = @id AND version = @expected
            """,
            new Dictionary<string, object?>
            {
                ["id"] = document.Id,
                ["status"] = document.Status.ToString(),
                ["date"] = document.DocumentDate,
                ["postingDate"] = document.PostingDate,
                ["warehouse"] = document.WarehouseId,
                ["external"] = document.ExternalReference,
                ["now"] = now,
                ["by"] = by.Value,
                ["version"] = document.Version,
                ["expected"] = expectedVersion,
            },
            cancellationToken);

    public static Task UpdateInventoryAdjustmentAsync(IUnitOfWork unitOfWork, DocumentRow document, DateTimeOffset now, UserId by, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteAsync(DatabaseRole.Kernel,
            "UPDATE kernel.inventory_adjustment SET reason_code = @reason, updated_at = @now, updated_by = @by, version = @version WHERE id = @id",
            new Dictionary<string, object?> { ["id"] = document.Id, ["reason"] = document.ReasonCode?.ToString(), ["now"] = now, ["by"] = by.Value, ["version"] = document.ExtensionVersion },
            cancellationToken);

    public static Task UpdateLineAsync(IUnitOfWork unitOfWork, DocumentLineRow line, DateTimeOffset now, UserId by, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteAsync(DatabaseRole.Kernel,
            """
            UPDATE kernel.document_line
            SET item_id = @item, description = @description, quantity = @quantity, unit_code = @unit, quantity_base = @quantityBase, warehouse_id = @warehouse, stock_status = @stockStatus, entered_unit_cost = @enteredCost, unit_cost = @unitCost, cost_amount = @costAmount, line_status = @status, updated_at = @now, updated_by = @by, version = @version
            WHERE id = @id
            """,
            LineParameters(line, now, by, []),
            cancellationToken);

    private static Dictionary<string, object?> LineParameters(DocumentLineRow line, DateTimeOffset now, UserId by, Dictionary<string, object?> parameters)
    {
        parameters["id"] = line.Id;
        parameters["number"] = line.LineNumber;
        parameters["item"] = line.ItemId;
        parameters["description"] = line.Description;
        parameters["quantity"] = line.Quantity;
        parameters["unit"] = line.UnitCode;
        parameters["quantityBase"] = line.QuantityBase;
        parameters["warehouse"] = line.WarehouseId;
        parameters["stockStatus"] = line.StockStatus.ToString();
        parameters["enteredCost"] = line.EnteredUnitCost;
        parameters["unitCost"] = line.UnitCost;
        parameters["costAmount"] = line.CostAmount;
        parameters["status"] = line.LineStatus.ToString();
        parameters["now"] = now;
        parameters["by"] = by.Value;
        parameters["version"] = line.Version;
        return parameters;
    }

    private static DocumentRow ReadDocument(DbDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetGuid(1),
        reader.GetString(2),
        Enum.Parse<DocumentStatus>(reader.GetString(3)),
        reader.IsDBNull(4) ? null : reader.GetString(4),
        reader.GetString(5),
        reader.GetFieldValue<DateOnly>(6),
        reader.IsDBNull(7) ? null : reader.GetFieldValue<DateOnly>(7),
        reader.GetString(8),
        reader.IsDBNull(9) ? null : reader.GetGuid(9),
        reader.IsDBNull(10) ? null : reader.GetString(10),
        reader.IsDBNull(11) ? null : reader.GetFieldValue<DateTimeOffset>(11),
        reader.IsDBNull(12) ? null : reader.GetGuid(12),
        reader.GetInt64(13),
        reader.IsDBNull(14) ? null : Enum.Parse<ReasonCode>(reader.GetString(14)),
        reader.IsDBNull(15) ? 0 : reader.GetInt64(15));

    /// <summary>Quantities read back at the unit's decimals, costs without trailing zeros (D-37).</summary>
    private static DocumentLineRow ReadLine(DbDataReader reader)
    {
        int decimals = reader.GetInt32(15);
        return new DocumentLineRow(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetInt32(2),
            Enum.Parse<LineStatus>(reader.GetString(3)),
            reader.GetGuid(4),
            reader.GetString(5),
            DecimalRounding.AtScale(reader.GetDecimal(6), decimals),
            reader.GetString(7),
            DecimalRounding.AtScale(reader.GetDecimal(8), decimals),
            reader.GetGuid(9),
            Enum.Parse<StockStatus>(reader.GetString(10)),
            reader.IsDBNull(11) ? null : DecimalRounding.Trim(reader.GetDecimal(11)),
            reader.IsDBNull(12) ? null : DecimalRounding.Trim(reader.GetDecimal(12)),
            reader.IsDBNull(13) ? null : DecimalRounding.Trim(reader.GetDecimal(13)),
            reader.GetInt64(14),
            decimals);
    }
}

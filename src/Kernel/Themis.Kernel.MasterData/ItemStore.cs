using System.Data.Common;
using Themis.Kernel.Contracts.MasterData;
using Themis.Kernel.Contracts.Organisation;
using Themis.Kernel.Contracts.Persistence;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.MasterData;

/// <summary>
/// Raw SQL on the command's unit of work as themis_kernel (R-DB-01): reads and inserts of items and their company
/// rows, and the existence checks ITEM.CREATE needs for its references. The kernel data context stays in
/// Themis.Kernel.Persistence; this capability reaches the tables through the unit of work only (ARCH-08).
/// </summary>
internal static class ItemStore
{
    private const string ItemColumns = "i.id, i.item_number, i.description, i.item_type, i.base_unit, i.lot_tracking, i.net_weight_kg, i.sales_multiple, i.min_order_qty, i.version";
    private const string CompanyColumns = "c.id, c.company_id, c.status, c.item_posting_group, c.tax_class, c.costing_method, c.sales_allowed, c.purchase_allowed, c.default_warehouse_id, c.version";

    public static async Task<bool> ItemNumberExistsAsync(IUnitOfWork unitOfWork, Code itemNumber, CancellationToken cancellationToken) =>
        await unitOfWork.ScalarAsync(DatabaseRole.Kernel, "SELECT count(*) FROM kernel.item WHERE item_number = @number", new Dictionary<string, object?> { ["number"] = itemNumber.Value }, cancellationToken) is long count && count > 0;

    public static async Task<int?> UnitDecimalsAsync(IUnitOfWork unitOfWork, UnitCode unit, CancellationToken cancellationToken) =>
        await unitOfWork.ScalarAsync(DatabaseRole.Kernel, "SELECT decimals FROM kernel.unit_of_measure WHERE code = @code", new Dictionary<string, object?> { ["code"] = unit.Value }, cancellationToken) as int?;

    public static async Task<bool> ItemPostingGroupExistsAsync(IUnitOfWork unitOfWork, Code code, CancellationToken cancellationToken) =>
        await unitOfWork.ScalarAsync(DatabaseRole.Kernel, "SELECT count(*) FROM kernel.item_posting_group WHERE code = @code", new Dictionary<string, object?> { ["code"] = code.Value }, cancellationToken) is long count && count > 0;

    /// <summary>The company a warehouse belongs to, or null when the warehouse does not exist.</summary>
    public static async Task<Guid?> WarehouseCompanyAsync(IUnitOfWork unitOfWork, Guid warehouseId, CancellationToken cancellationToken) =>
        await unitOfWork.ScalarAsync(DatabaseRole.Kernel, "SELECT company_id FROM kernel.warehouse WHERE id = @id", new Dictionary<string, object?> { ["id"] = warehouseId }, cancellationToken) as Guid?;

    public static Task InsertItemAsync(IUnitOfWork unitOfWork, ItemView item, DateTimeOffset now, UserId by, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteAsync(DatabaseRole.Kernel,
            """
            INSERT INTO kernel.item (id, item_number, description, item_type, base_unit, lot_tracking, net_weight_kg, sales_multiple, min_order_qty, created_at, created_by, updated_at, updated_by, version)
            VALUES (@id, @number, @description, @type, @unit, @lot, @weight, @multiple, @minimum, @now, @by, @now, @by, @version)
            """,
            new Dictionary<string, object?>
            {
                ["id"] = item.Id,
                ["number"] = item.ItemNumber.Value,
                ["description"] = item.Description,
                ["type"] = item.ItemType.ToString(),
                ["unit"] = item.BaseUnit.Value,
                ["lot"] = item.LotTracking.ToString(),
                ["weight"] = item.NetWeightKg,
                ["multiple"] = item.SalesMultiple,
                ["minimum"] = item.MinOrderQty,
                ["now"] = now,
                ["by"] = by.Value,
                ["version"] = item.Version,
            },
            cancellationToken);

    public static Task InsertItemCompanyAsync(IUnitOfWork unitOfWork, Guid itemId, ItemCompanyView row, DateTimeOffset now, UserId by, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteAsync(DatabaseRole.Kernel,
            """
            INSERT INTO kernel.item_company (id, item_id, company_id, status, item_posting_group, tax_class, costing_method, sales_allowed, purchase_allowed, default_warehouse_id, created_at, created_by, updated_at, updated_by, version)
            VALUES (@id, @item, @company, @status, @group, @tax, @costing, @sales, @purchase, @warehouse, @now, @by, @now, @by, @version)
            """,
            new Dictionary<string, object?>
            {
                ["id"] = row.Id,
                ["item"] = itemId,
                ["company"] = row.CompanyId,
                ["status"] = row.Status.ToString(),
                ["group"] = row.ItemPostingGroup.Value,
                ["tax"] = row.TaxClass.ToString(),
                ["costing"] = row.CostingMethod.ToString(),
                ["sales"] = row.SalesAllowed,
                ["purchase"] = row.PurchaseAllowed,
                ["warehouse"] = row.DefaultWarehouseId,
                ["now"] = now,
                ["by"] = by.Value,
                ["version"] = row.Version,
            },
            cancellationToken);

    /// <summary>The item with the company's row, by id or by item number; null when the item has no row in that company.</summary>
    public static async Task<ItemView?> FindInCompanyAsync(IUnitOfWork unitOfWork, Guid companyId, Guid? itemId, Code? itemNumber, CancellationToken cancellationToken)
    {
        string condition = itemId is not null ? "i.id = @item" : "i.item_number = @number";
        IReadOnlyList<ItemView> rows = await unitOfWork.QueryAsync(DatabaseRole.Kernel,
            $"""
            SELECT {ItemColumns}, {CompanyColumns}
            FROM kernel.item i
            JOIN kernel.item_company c ON c.item_id = i.id AND c.company_id = @company
            WHERE {condition}
            """,
            new Dictionary<string, object?> { ["company"] = companyId, ["item"] = itemId, ["number"] = itemNumber?.Value },
            Read,
            cancellationToken);
        return rows.Count == 0 ? null : rows[0];
    }

    private static ItemView Read(DbDataReader reader) => new(
        reader.GetGuid(0),
        Code.From(reader.GetString(1)),
        reader.GetString(2),
        Enum.Parse<ItemType>(reader.GetString(3)),
        UnitCode.From(reader.GetString(4)),
        Enum.Parse<LotTracking>(reader.GetString(5)),
        reader.IsDBNull(6) ? null : reader.GetDecimal(6),
        reader.IsDBNull(7) ? null : reader.GetDecimal(7),
        reader.IsDBNull(8) ? null : reader.GetDecimal(8),
        reader.GetInt64(9),
        new ItemCompanyView(
            reader.GetGuid(10),
            reader.GetGuid(11),
            Enum.Parse<MasterDataStatus>(reader.GetString(12)),
            Code.From(reader.GetString(13)),
            Enum.Parse<TaxClass>(reader.GetString(14)),
            Enum.Parse<CostingMethod>(reader.GetString(15)),
            reader.GetBoolean(16),
            reader.GetBoolean(17),
            reader.IsDBNull(18) ? null : reader.GetGuid(18),
            reader.GetInt64(19)));
}

using System.Globalization;
using Themis.Kernel.Contracts.Commands;
using Themis.Kernel.Contracts.MasterData;
using Themis.Kernel.Contracts.Messaging;
using Themis.Kernel.Contracts.Organisation;
using Themis.Kernel.Contracts.Persistence;
using Themis.Kernel.Contracts.Queries;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.MasterData;

/// <summary>
/// ITEM.CREATE (step 5 brief): the installation-wide Item and its ItemCompany row in one command (D-43). The item
/// number is stored uppercase and a duplicate is rejected with R-MD-05 (422). An unknown base unit, item posting
/// group or warehouse, or a warehouse of another company, is a 400 with a field-specific code and no ruleId, found
/// inside the transaction and therefore stored as the key's result (Build Brief, Errors). One audit event and one
/// outbox event per created row, the type in the event name being the row's type (D-44, R-AUD-01).
/// </summary>
internal sealed class CreateItemHandler : ICommandHandler<CreateItem, ItemView>
{
    public const string ItemType = "ITEM";
    public const string ItemCompanyType = "ITEM_COMPANY";
    public const string ItemEntity = "Item";
    public const string ItemCompanyEntity = "ItemCompany";

    public async Task<Result<ItemView>> HandleAsync(CreateItem command, ICommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);
        IUnitOfWork unitOfWork = context.UnitOfWork;

        // R-MD-05: the item number is unique per installation; Code is already uppercase, so the comparison is too.
        if (await ItemStore.ItemNumberExistsAsync(unitOfWork, command.ItemNumber, cancellationToken))
        {
            return Error.Rule("R-MD-05", "item_number_duplicate", $"Item number {command.ItemNumber} already exists.");
        }

        int? unitDecimals = await ItemStore.UnitDecimalsAsync(unitOfWork, command.BaseUnit, cancellationToken);
        if (unitDecimals is null)
        {
            return Error.Validation("base_unit_unknown", $"Unit {command.BaseUnit} is not a unit of measure.");
        }

        // Quantities in the base unit carry at most the unit's decimals (UnitOfMeasure.decimals).
        if (command.SalesMultiple is { } multiple && DecimalRounding.Scale(multiple) > unitDecimals && DecimalRounding.Round(multiple, unitDecimals.Value) != multiple)
        {
            return Error.Validation("sales_multiple_precision", $"Sales multiple {multiple} has more decimals than unit {command.BaseUnit} allows ({unitDecimals}).");
        }

        if (command.MinOrderQty is { } minimum && DecimalRounding.Scale(minimum) > unitDecimals && DecimalRounding.Round(minimum, unitDecimals.Value) != minimum)
        {
            return Error.Validation("min_order_qty_precision", $"Minimum order quantity {minimum} has more decimals than unit {command.BaseUnit} allows ({unitDecimals}).");
        }

        if (!await ItemStore.ItemPostingGroupExistsAsync(unitOfWork, command.ItemPostingGroup, cancellationToken))
        {
            return Error.Validation("item_posting_group_unknown", $"Item posting group {command.ItemPostingGroup} does not exist.");
        }

        if (command.DefaultWarehouseId is { } warehouseId)
        {
            Guid? warehouseCompany = await ItemStore.WarehouseCompanyAsync(unitOfWork, warehouseId, cancellationToken);
            if (warehouseCompany is null)
            {
                return Error.Validation("default_warehouse_unknown", $"Warehouse {warehouseId} does not exist.");
            }

            if (warehouseCompany != command.CompanyId)
            {
                return Error.Validation("default_warehouse_other_company", $"Warehouse {warehouseId} belongs to another company.");
            }
        }

        ItemCompanyView companyRow = new(
            EntityId.New(),
            command.CompanyId,
            MasterDataStatus.Active,
            command.ItemPostingGroup,
            command.TaxClass,
            command.CostingMethod,
            command.SalesAllowed,
            command.PurchaseAllowed,
            command.DefaultWarehouseId,
            Version: 1);
        // Quantities in the base unit are shown at the unit's decimals and the net weight without trailing zeros, on
        // create and on read alike (D-37, R-INV-02); the owner chose this over the storage scale of numeric(20,6).
        ItemView item = new(
            EntityId.New(),
            command.ItemNumber,
            command.Description.Trim(),
            command.ItemType,
            command.BaseUnit,
            command.LotTracking,
            command.NetWeightKg is { } weight ? DecimalRounding.Trim(weight) : null,
            command.SalesMultiple is { } salesMultiple ? DecimalRounding.AtScale(salesMultiple, unitDecimals.Value) : null,
            command.MinOrderQty is { } minOrderQty ? DecimalRounding.AtScale(minOrderQty, unitDecimals.Value) : null,
            Version: 1,
            companyRow);

        await ItemStore.InsertItemAsync(unitOfWork, item, context.Now, context.Principal.UserId, cancellationToken);
        await ItemStore.InsertItemCompanyAsync(unitOfWork, item.Id, companyRow, context.Now, context.Principal.UserId, cancellationToken);

        // R-AUD-01 and D-44: one audit event and one outbox event per created row; the type in the name is the row's.
        context.Audit.Record(ItemEntity, item.Id, ItemAudit.ItemChanges(item));
        context.Audit.Record(ItemCompanyEntity, companyRow.Id, ItemAudit.ItemCompanyChanges(item.Id, companyRow));
        context.Outbox.Publish(EventNames.Generated(ItemType, "CREATE"), EventNames.DefaultSchemaVersion, item.Id, item);
        context.Outbox.Publish(EventNames.Generated(ItemCompanyType, "CREATE"), EventNames.DefaultSchemaVersion, companyRow.Id, new ItemCompanyCreated(item.Id, companyRow));
        return item;
    }

    /// <summary>The storage scale of a quantity column, numeric(20,6) (Specification, Data types); the validator refuses anything finer.</summary>
    public const int QuantityScale = 6;

    /// <summary>The ITEM_COMPANY_CREATED payload: the id and fields of the row, with the item it belongs to.</summary>
    internal sealed record ItemCompanyCreated(Guid ItemId, ItemCompanyView Row);
}

/// <summary>Validation before the transaction opens: shape and ranges, never a database lookup (Build Brief, Errors).</summary>
internal sealed class CreateItemValidator : ICommandValidator<CreateItem>
{
    public const int DescriptionMaxLength = 200;

    public IEnumerable<Error> Validate(CreateItem command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.CompanyId == Guid.Empty)
        {
            yield return Error.Validation("company_required", "An item is created in a company.");
        }

        if (string.IsNullOrWhiteSpace(command.Description))
        {
            yield return Error.Validation("description_required", "A description is required.");
        }
        else if (command.Description.Trim().Length > DescriptionMaxLength)
        {
            yield return Error.Validation("description_too_long", $"A description holds at most {DescriptionMaxLength} characters.");
        }

        if (command.NetWeightKg is < 0)
        {
            yield return Error.Validation("net_weight_kg_negative", "Net weight is zero or above.");
        }
        else if (command.NetWeightKg is { } weight && DecimalRounding.Scale(weight) > CreateItemHandler.QuantityScale && DecimalRounding.Round(weight, CreateItemHandler.QuantityScale) != weight)
        {
            // A quantity column holds six decimals (Specification, Data types); PostgreSQL would round silently.
            yield return Error.Validation("net_weight_kg_precision", $"Net weight holds at most {CreateItemHandler.QuantityScale} decimals.");
        }

        if (command.SalesMultiple is <= 0)
        {
            yield return Error.Validation("sales_multiple_not_positive", "Sales multiple is above zero.");
        }

        if (command.MinOrderQty is <= 0)
        {
            yield return Error.Validation("min_order_qty_not_positive", "Minimum order quantity is above zero.");
        }
    }
}

/// <summary>GET item by id in a company (ITEM.READ): the row's absence in that company is a 404 (step 5 brief).</summary>
internal sealed class GetItemHandler : IQueryHandler<GetItem, ItemView>
{
    public async Task<Result<ItemView>> HandleAsync(GetItem query, IQueryContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(context);
        ItemView? item = await ItemStore.FindInCompanyAsync(context.UnitOfWork, query.CompanyId, query.Id, null, cancellationToken);
        return item is null ? Error.NotFound("item_not_found", $"Item {query.Id} has no row in company {query.CompanyId}.") : item;
    }
}

/// <summary>GET item by item number in a company (ITEM.READ).</summary>
internal sealed class GetItemByNumberHandler : IQueryHandler<GetItemByNumber, ItemView>
{
    public async Task<Result<ItemView>> HandleAsync(GetItemByNumber query, IQueryContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(context);
        ItemView? item = await ItemStore.FindInCompanyAsync(context.UnitOfWork, query.CompanyId, null, query.ItemNumber, cancellationToken);
        return item is null ? Error.NotFound("item_not_found", $"Item {query.ItemNumber} has no row in company {query.CompanyId}.") : item;
    }
}

/// <summary>The changed fields of the audit events (R-AUD-01): every field from nothing to its created value.</summary>
internal static class ItemAudit
{
    public static IReadOnlyDictionary<string, FieldChange> ItemChanges(ItemView item) => new Dictionary<string, FieldChange>(StringComparer.Ordinal)
    {
        ["itemNumber"] = new(null, item.ItemNumber.Value),
        ["description"] = new(null, item.Description),
        ["itemType"] = new(null, item.ItemType.ToString()),
        ["baseUnit"] = new(null, item.BaseUnit.Value),
        ["lotTracking"] = new(null, item.LotTracking.ToString()),
        ["netWeightKg"] = new(null, Text(item.NetWeightKg)),
        ["salesMultiple"] = new(null, Text(item.SalesMultiple)),
        ["minOrderQty"] = new(null, Text(item.MinOrderQty)),
    };

    public static IReadOnlyDictionary<string, FieldChange> ItemCompanyChanges(Guid itemId, ItemCompanyView row) => new Dictionary<string, FieldChange>(StringComparer.Ordinal)
    {
        ["itemId"] = new(null, itemId.ToString()),
        ["companyId"] = new(null, row.CompanyId.ToString()),
        ["status"] = new(null, row.Status.ToString()),
        ["itemPostingGroup"] = new(null, row.ItemPostingGroup.Value),
        ["taxClass"] = new(null, row.TaxClass.ToString()),
        ["costingMethod"] = new(null, row.CostingMethod.ToString()),
        ["salesAllowed"] = new(null, row.SalesAllowed ? "True" : "False"),
        ["purchaseAllowed"] = new(null, row.PurchaseAllowed ? "True" : "False"),
        ["defaultWarehouseId"] = new(null, row.DefaultWarehouseId?.ToString()),
    };

    private static string? Text(decimal? value) => value?.ToString(CultureInfo.InvariantCulture);
}

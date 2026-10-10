using Themis.Kernel.Persistence.Entities.Organisation;

namespace Themis.Kernel.Persistence.Entities.MasterData;

/// <summary>Phase 0 Specification, UnitOfMeasure: the global reference list, seeded by migration (Reference data).</summary>
internal sealed class UnitOfMeasure : MasterDataRow
{
    public required string Code { get; set; }

    public required string Name { get; set; }

    public required string Dimension { get; set; }

    public required int Decimals { get; set; }
}

/// <summary>Phase 0 Specification, Currency: reference data seeded by migration; minor units drive rounding (R-MON-02).</summary>
internal sealed class Currency : MasterDataRow
{
    public required string Code { get; set; }

    public required string Name { get; set; }

    public required int MinorUnits { get; set; }
}

/// <summary>Phase 0 Specification, Posting groups: the item posting group list (code, name) the posting profile keys on (R-PP-01).</summary>
internal sealed class ItemPostingGroup : MasterDataRow
{
    public required string Code { get; set; }

    public required string Name { get; set; }
}

/// <summary>Phase 0 Specification, Item: installation-wide identity (D-43); item number unique per installation (R-MD-05).</summary>
internal sealed class Item : MasterDataRow
{
    public required string ItemNumber { get; set; }

    public required string Description { get; set; }

    public required string ItemType { get; set; }

    public required string BaseUnit { get; set; }

    public required string LotTracking { get; set; }

    public Guid? AttributeTemplateId { get; set; }

    public decimal? NetWeightKg { get; set; }

    public decimal? SalesMultiple { get; set; }

    public decimal? MinOrderQty { get; set; }
}

/// <summary>Phase 0 Specification, ItemCompany: one row per item and company (D-43), unique pair (R-MD-05).</summary>
internal sealed class ItemCompany : MasterDataRow
{
    public required Guid ItemId { get; set; }

    public required Guid CompanyId { get; set; }

    public required string Status { get; set; }

    public required string ItemPostingGroup { get; set; }

    public required string TaxClass { get; set; }

    public required string CostingMethod { get; set; }

    public required bool SalesAllowed { get; set; }

    public required bool PurchaseAllowed { get; set; }

    public Guid? DefaultWarehouseId { get; set; }

    public string? DefaultSalesUnit { get; set; }

    public string? DefaultPurchaseUnit { get; set; }
}

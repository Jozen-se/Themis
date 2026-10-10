using Themis.Kernel.Primitives;

namespace Themis.Kernel.Contracts.MasterData;

/// <summary>Phase 0 Specification, Item: only Stocked items create inventory entries.</summary>
public enum ItemType
{
    Stocked,
    NonStocked,
    Service,
}

/// <summary>Phase 0 Specification, Item: immutable after the first inventory entry.</summary>
public enum LotTracking
{
    None,
    Required,
}

/// <summary>Phase 0 Specification, ItemCompany: input to the tax rules (R-TAX-01).</summary>
public enum TaxClass
{
    Standard,
    Reduced,
    Zero,
    Exempt,
}

/// <summary>Phase 0 Specification, ItemCompany: the only value in v1 (R-INV-05); master data, never the cost state cache.</summary>
public enum CostingMethod
{
    MovingAverage,
}

/// <summary>Phase 0 Specification, UnitOfMeasure.</summary>
public enum UnitDimension
{
    Count,
    Mass,
    Length,
    Volume,
    Time,
}

/// <summary>A unit of the global reference list: UN/ECE Recommendation 20 code, dimension and the decimals entered quantities may carry.</summary>
public sealed record UnitOfMeasureDefinition(UnitCode Code, string Name, UnitDimension Dimension, int Decimals);

/// <summary>A currency of the reference list with its minor units, which drive rounding (R-MON-02).</summary>
public sealed record CurrencyDefinition(CurrencyCode Code, string Name, int MinorUnits);

/// <summary>An item posting group (code, name): company configuration keyed by the posting profile (R-PP-01), fixture-seeded in milestone 1.</summary>
public sealed record ItemPostingGroupDefinition(Guid Id, Code Code, string Name);

/// <summary>The master data rows of a fixture as data, framework-free, written by the persistence capability (ARCH-08).</summary>
public sealed record MasterDataFixtureDefinition(IReadOnlyList<ItemPostingGroupDefinition> ItemPostingGroups);

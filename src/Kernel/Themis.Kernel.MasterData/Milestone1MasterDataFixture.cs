using Themis.Kernel.Contracts.MasterData;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.MasterData;

/// <summary>
/// The master data fixture of milestone 1 step 5 (brief): the item posting group TRADE_GOODS, which is company
/// configuration and therefore fixture-seeded, never reference data. Units and currencies are reference data
/// seeded by the migration itself (Specification, Reference data). Items are created through ITEM.CREATE.
/// </summary>
internal static class Milestone1MasterDataFixture
{
    public static Guid TradeGoodsId { get; } = Guid.Parse("00000000-0000-7000-8000-000000000501");

    public const string TradeGoodsCode = "TRADE_GOODS";

    public static MasterDataFixtureDefinition Definition { get; } = new(
    [
        new ItemPostingGroupDefinition(TradeGoodsId, Code.From(TradeGoodsCode), "Trade goods"),
    ]);
}

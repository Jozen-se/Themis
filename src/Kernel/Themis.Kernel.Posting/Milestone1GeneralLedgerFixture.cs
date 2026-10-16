using Themis.Kernel.Contracts.Organisation;
using Themis.Kernel.Contracts.Posting;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Posting;

/// <summary>
/// The general ledger fixture of milestone 1 step 6 (brief): in the company's chart, BAS 1460 Lager av handelsvaror
/// as an Asset control account and the placeholder TEST-ADJ (letters on purpose, so it can never be mistaken for a
/// BAS account while D-20 is open) as an Expense non-control account; and two posting profile rules valid from
/// 2026-01-01, open-ended: INVENTORY for TRADE_GOODS to 1460 and INVENTORY_ADJUSTMENT for TRADE_GOODS with reason
/// CountDifference to TEST-ADJ. Scrap, Other and OpeningBalance have no rule, so a Scrap adjustment is rejected with
/// R-PP-02. Every value is test data; the identifiers are fixed so that seeding is idempotent and tests can name the rows.
/// </summary>
internal static class Milestone1GeneralLedgerFixture
{
    public static Guid InventoryAccountId { get; } = Guid.Parse("00000000-0000-7000-8000-000000000601");
    public static Guid AdjustmentAccountId { get; } = Guid.Parse("00000000-0000-7000-8000-000000000602");
    public static Guid InventoryRuleId { get; } = Guid.Parse("00000000-0000-7000-8000-000000000701");
    public static Guid InventoryAdjustmentRuleId { get; } = Guid.Parse("00000000-0000-7000-8000-000000000702");

    public const string InventoryAccountNumber = "1460";
    public const string InventoryAccountName = "Lager av handelsvaror";
    public const string AdjustmentAccountNumber = "TEST-ADJ";
    public const string AdjustmentAccountName = "Test placeholder, inventory adjustment (D-20 open)";
    public const string TradeGoods = "TRADE_GOODS";

    public static BusinessDate ValidFrom { get; } = BusinessDate.Parse("2026-01-01");

    /// <summary>Builds the fixture for the company and chart the organisation fixture seeds (step 3).</summary>
    public static GeneralLedgerFixtureDefinition For(Guid companyId, Guid chartId) => new(
        [
            new AccountDefinition(InventoryAccountId, chartId, Code.From(InventoryAccountNumber), InventoryAccountName, AccountType.Asset, IsControl: true, MasterDataStatus.Active),
            new AccountDefinition(AdjustmentAccountId, chartId, Code.From(AdjustmentAccountNumber), AdjustmentAccountName, AccountType.Expense, IsControl: false, MasterDataStatus.Active),
        ],
        [
            new PostingProfileRuleDefinition(InventoryRuleId, companyId, AccountRoles.Inventory, InventoryAccountId, ValidFrom, ItemPostingGroup: Code.From(TradeGoods)),
            new PostingProfileRuleDefinition(InventoryAdjustmentRuleId, companyId, AccountRoles.InventoryAdjustment, AdjustmentAccountId, ValidFrom, ItemPostingGroup: Code.From(TradeGoods), ReasonCode: ReasonCode.CountDifference),
        ]);
}

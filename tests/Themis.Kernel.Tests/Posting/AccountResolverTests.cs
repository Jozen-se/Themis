using Themis.Kernel.Contracts.Organisation;
using Themis.Kernel.Contracts.Posting;
using Themis.Kernel.Posting;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Tests.Posting;

/// <summary>
/// The resolution algorithm on in-memory rules (Phase 0 Specification, Posting profile: the resolution algorithm):
/// candidates, the fixed key order (R-PP-01, D-19), validity, R-PP-02 for no candidate, and D-49 for a winner whose
/// account is not Active, never falling back.
/// </summary>
[Trait("Category", "Unit")]
public sealed class AccountResolverTests
{
    private static readonly BusinessDate June = BusinessDate.Parse("2026-06-15");
    private static readonly Code TradeGoods = Code.From("TRADE_GOODS");
    private static readonly Code Freight = Code.From("FREIGHT");
    private static readonly Guid Active = Guid.Parse("00000000-0000-7000-8000-00000000a001");
    private static readonly Guid Blocked = Guid.Parse("00000000-0000-7000-8000-00000000a002");

    private static PostingRuleCandidate Rule(string role, PostingKeys keys, Guid account, MasterDataStatus status = MasterDataStatus.Active, string from = "2026-01-01", string? to = null) =>
        new(Guid.NewGuid(), role, keys, account, status, BusinessDate.Parse(from), to is null ? null : BusinessDate.Parse(to));

    [Fact]
    public void D_19_a_rule_that_sets_the_item_posting_group_beats_one_that_sets_only_the_reason_code()
    {
        PostingRuleCandidate groupOnly = Rule(AccountRoles.InventoryAdjustment, new PostingKeys(ItemPostingGroup: TradeGoods), Active);
        PostingRuleCandidate reasonOnly = Rule(AccountRoles.InventoryAdjustment, new PostingKeys(ReasonCode: ReasonCode.CountDifference), Blocked);

        Result<AccountResolution> result = AccountResolver.Resolve(AccountRoles.InventoryAdjustment, June, new PostingKeys(TradeGoods, ReasonCode: ReasonCode.CountDifference), [reasonOnly, groupOnly]);

        Assert.True(result.IsSuccess);
        Assert.Equal(groupOnly.Id, result.Value.PostingRuleId);
        Assert.Equal(Active, result.Value.AccountId);
        Assert.Equal(AccountRoles.InventoryAdjustment, result.Value.AccountRole);
    }

    [Fact]
    public void R_PP_01_the_most_specific_rule_wins_and_a_rule_with_another_key_value_is_not_a_candidate()
    {
        PostingRuleCandidate groupAny = Rule(AccountRoles.InventoryAdjustment, new PostingKeys(ItemPostingGroup: TradeGoods), Blocked);
        PostingRuleCandidate groupAndReason = Rule(AccountRoles.InventoryAdjustment, new PostingKeys(TradeGoods, ReasonCode: ReasonCode.CountDifference), Active);
        PostingRuleCandidate otherGroup = Rule(AccountRoles.InventoryAdjustment, new PostingKeys(Freight, ReasonCode: ReasonCode.CountDifference), Blocked);
        PostingRuleCandidate otherRole = Rule(AccountRoles.Inventory, new PostingKeys(TradeGoods), Blocked);

        Result<AccountResolution> result = AccountResolver.Resolve(AccountRoles.InventoryAdjustment, June, new PostingKeys(TradeGoods, ReasonCode: ReasonCode.CountDifference), [groupAny, otherGroup, otherRole, groupAndReason]);

        Assert.True(result.IsSuccess);
        Assert.Equal(groupAndReason.Id, result.Value.PostingRuleId);
    }

    [Fact]
    public void R_MD_02_a_rule_outside_its_validity_is_not_a_candidate_valid_from_inclusive_valid_to_exclusive()
    {
        PostingRuleCandidate old = Rule(AccountRoles.Inventory, new PostingKeys(TradeGoods), Blocked, to: "2026-06-15");
        PostingRuleCandidate current = Rule(AccountRoles.Inventory, new PostingKeys(TradeGoods), Active, from: "2026-06-15");

        Result<AccountResolution> onTheDay = AccountResolver.Resolve(AccountRoles.Inventory, June, new PostingKeys(TradeGoods), [old, current]);
        Result<AccountResolution> dayBefore = AccountResolver.Resolve(AccountRoles.Inventory, BusinessDate.Parse("2026-06-14"), new PostingKeys(TradeGoods), [old, current]);

        Assert.Equal(current.Id, onTheDay.Value.PostingRuleId);
        Assert.Equal(old.Id, dayBefore.Value.PostingRuleId);
    }

    [Fact]
    public void R_PP_02_no_candidate_rejects_naming_the_role_the_keys_and_the_date()
    {
        PostingRuleCandidate countDifference = Rule(AccountRoles.InventoryAdjustment, new PostingKeys(TradeGoods, ReasonCode: ReasonCode.CountDifference), Active);

        Result<AccountResolution> result = AccountResolver.Resolve(AccountRoles.InventoryAdjustment, June, new PostingKeys(TradeGoods, ReasonCode: ReasonCode.Scrap), [countDifference]);

        Assert.True(result.IsFailure);
        Assert.Equal("R-PP-02", result.Primary!.RuleId);
        Assert.Equal(AccountResolver.RuleMissing, result.Primary.Code);
        Assert.Equal("no account for INVENTORY_ADJUSTMENT, item group TRADE_GOODS, reason code Scrap, posting date 2026-06-15", result.Primary.Message);
    }

    [Fact]
    public void D_49_a_winner_whose_account_is_Blocked_is_rejected_with_R_PP_02_even_when_a_less_specific_rule_has_an_Active_account()
    {
        PostingRuleCandidate specificBlocked = Rule(AccountRoles.InventoryAdjustment, new PostingKeys(TradeGoods, ReasonCode: ReasonCode.CountDifference), Blocked, MasterDataStatus.Blocked);
        PostingRuleCandidate generalActive = Rule(AccountRoles.InventoryAdjustment, new PostingKeys(ItemPostingGroup: TradeGoods), Active);

        Result<AccountResolution> result = AccountResolver.Resolve(AccountRoles.InventoryAdjustment, June, new PostingKeys(TradeGoods, ReasonCode: ReasonCode.CountDifference), [generalActive, specificBlocked]);

        Assert.True(result.IsFailure);
        Assert.Equal("R-PP-02", result.Primary!.RuleId);
        Assert.Equal(AccountResolver.AccountNotActive, result.Primary.Code);
        Assert.Contains("INVENTORY_ADJUSTMENT, item group TRADE_GOODS, reason code CountDifference, posting date 2026-06-15", result.Primary.Message, StringComparison.Ordinal);
        Assert.Contains(Blocked.ToString(), result.Primary.Message, StringComparison.Ordinal);
        Assert.Contains("Blocked", result.Primary.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void R_PP_06_only_the_roles_keys_are_compared_so_a_reason_code_on_the_line_does_not_disturb_an_INVENTORY_rule()
    {
        PostingRuleCandidate inventory = Rule(AccountRoles.Inventory, new PostingKeys(TradeGoods), Active);

        Result<AccountResolution> result = AccountResolver.Resolve(AccountRoles.Inventory, June, new PostingKeys(TradeGoods, ReasonCode: ReasonCode.Scrap), [inventory]);

        Assert.True(result.IsSuccess);
        Assert.Equal(inventory.Id, result.Value.PostingRuleId);
    }

    [Fact]
    public void An_unknown_role_is_a_programming_error_not_a_rejection()
    {
        Assert.Throws<ArgumentException>(() => AccountResolver.Resolve("NOT_A_ROLE", June, PostingKeys.None, []));
    }
}

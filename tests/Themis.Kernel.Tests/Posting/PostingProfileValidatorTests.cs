using Themis.Kernel.Contracts.Organisation;
using Themis.Kernel.Contracts.Posting;
using Themis.Kernel.Organisation;
using Themis.Kernel.Posting;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Tests.Posting;

/// <summary>
/// The rule check the seeder runs and the Phase 0 commands will run (step 6 brief): the account in the company's
/// chart, Active, R-PP-04 in both directions, R-PP-06; and the account role catalogue and the milestone 1 fixture.
/// </summary>
[Trait("Category", "Unit")]
public sealed class PostingProfileValidatorTests
{
    private static readonly Guid Company = Milestone1OrganisationFixture.CompanyId;
    private static readonly Guid Chart = Milestone1OrganisationFixture.ChartOfAccountsId;
    private static readonly Guid OtherChart = Guid.Parse("00000000-0000-7000-8000-00000000c002");
    private static readonly BusinessDate From = BusinessDate.Parse("2026-01-01");

    private static AccountDefinition Account(bool isControl, MasterDataStatus status = MasterDataStatus.Active, Guid? chart = null) =>
        new(Guid.NewGuid(), chart ?? Chart, Code.From(isControl ? "1460" : "TEST-ADJ"), "Account", isControl ? AccountType.Asset : AccountType.Expense, isControl, status);

    private static PostingProfileRuleDefinition Rule(string role, AccountDefinition account, Code? group = null, ReasonCode? reason = null, PartyPostingGroup? party = null, Guid? taxCode = null) =>
        new(Guid.NewGuid(), Company, role, account.Id, From, ItemPostingGroup: group, ReasonCode: reason, PartyPostingGroup: party, TaxCodeId: taxCode);

    [Fact]
    public void A_well_formed_rule_passes()
    {
        AccountDefinition account = Account(isControl: false);
        Assert.True(PostingProfileValidator.Validate(Rule(AccountRoles.InventoryAdjustment, account, Code.From("TRADE_GOODS"), ReasonCode.CountDifference), account, Chart).IsSuccess);
    }

    [Fact]
    public void An_account_from_another_chart_and_a_Blocked_or_Inactive_account_are_rejected()
    {
        AccountDefinition otherChart = Account(isControl: true, chart: OtherChart);
        AccountDefinition blocked = Account(isControl: true, MasterDataStatus.Blocked);
        AccountDefinition inactive = Account(isControl: true, MasterDataStatus.Inactive);

        Assert.Equal("account_other_chart", PostingProfileValidator.Validate(Rule(AccountRoles.Inventory, otherChart, Code.From("TRADE_GOODS")), otherChart, Chart).Primary!.Code);
        Assert.Equal("account_not_active", PostingProfileValidator.Validate(Rule(AccountRoles.Inventory, blocked, Code.From("TRADE_GOODS")), blocked, Chart).Primary!.Code);
        Assert.Equal("account_not_active", PostingProfileValidator.Validate(Rule(AccountRoles.Inventory, inactive, Code.From("TRADE_GOODS")), inactive, Chart).Primary!.Code);
    }

    [Fact]
    public void R_PP_04_a_control_role_on_a_non_control_account_and_a_non_control_role_on_a_control_account_are_rejected()
    {
        AccountDefinition control = Account(isControl: true);
        AccountDefinition nonControl = Account(isControl: false);

        Result controlRoleWrong = PostingProfileValidator.Validate(Rule(AccountRoles.Inventory, nonControl, Code.From("TRADE_GOODS")), nonControl, Chart);
        Result otherRoleWrong = PostingProfileValidator.Validate(Rule(AccountRoles.InventoryAdjustment, control, Code.From("TRADE_GOODS")), control, Chart);

        Assert.Equal("R-PP-04", controlRoleWrong.Primary!.RuleId);
        Assert.Equal("account_control_mismatch", controlRoleWrong.Primary.Code);
        Assert.Equal("R-PP-04", otherRoleWrong.Primary!.RuleId);
    }

    [Fact]
    public void R_PP_06_a_key_outside_the_roles_mapping_keys_is_rejected()
    {
        AccountDefinition control = Account(isControl: true);

        Result reasonOnInventory = PostingProfileValidator.Validate(Rule(AccountRoles.Inventory, control, Code.From("TRADE_GOODS"), ReasonCode.Scrap), control, Chart);
        Result partyOnInventory = PostingProfileValidator.Validate(Rule(AccountRoles.Inventory, control, party: PartyPostingGroup.EU), control, Chart);

        Assert.Equal("R-PP-06", reasonOnInventory.Primary!.RuleId);
        Assert.Equal("key_not_in_role", reasonOnInventory.Primary.Code);
        Assert.Equal("R-PP-06", partyOnInventory.Primary!.RuleId);
    }

    [Fact]
    public void The_fixture_check_names_an_unknown_account_or_company_and_passes_the_milestone_1_fixture()
    {
        GeneralLedgerFixtureDefinition fixture = Milestone1GeneralLedgerFixture.For(Company, Chart);
        Dictionary<Guid, Guid> charts = new() { [Company] = Chart };

        Assert.True(PostingProfileValidator.CheckFixture(fixture, charts).IsSuccess);
        Assert.Equal("account_unknown", PostingProfileValidator.CheckFixture(fixture with { Accounts = [] }, charts).Primary!.Code);
        Assert.Equal("company_unknown", PostingProfileValidator.CheckFixture(fixture, new Dictionary<Guid, Guid>()).Primary!.Code);
    }

    [Fact]
    public void The_milestone_1_fixture_holds_1460_as_control_TEST_ADJ_as_non_control_and_two_rules_from_2026_01_01()
    {
        GeneralLedgerFixtureDefinition fixture = Milestone1GeneralLedgerFixture.For(Company, Chart);

        Assert.Equal(["1460", "TEST-ADJ"], fixture.Accounts.Select(a => a.AccountNumber.Value));
        Assert.Equal([true, false], fixture.Accounts.Select(a => a.IsControl));
        Assert.Equal([AccountType.Asset, AccountType.Expense], fixture.Accounts.Select(a => a.AccountType));
        Assert.All(fixture.Accounts, a => Assert.Equal(MasterDataStatus.Active, a.Status));
        Assert.Equal("Test placeholder, inventory adjustment (D-20 open)", fixture.Accounts[1].Name);
        Assert.Equal([AccountRoles.Inventory, AccountRoles.InventoryAdjustment], fixture.Rules.Select(r => r.AccountRole));
        Assert.All(fixture.Rules, r => Assert.Equal("TRADE_GOODS", r.ItemPostingGroup!.Value.Value));
        Assert.All(fixture.Rules, r => Assert.Equal(BusinessDate.Parse("2026-01-01"), r.ValidFrom));
        Assert.All(fixture.Rules, r => Assert.Null(r.ValidTo));
        Assert.Null(fixture.Rules[0].ReasonCode);
        Assert.Equal(ReasonCode.CountDifference, fixture.Rules[1].ReasonCode);
    }

    [Fact]
    public void The_account_role_catalogue_is_the_specifications_table()
    {
        Assert.Equal(12, AccountRoles.All.Count);
        Assert.Equal(["INVENTORY", "RECEIVABLES", "OUTPUT_VAT"], AccountRoles.All.Where(r => r.ExpectsControlAccount).Select(r => r.Code));
        Assert.Equal(MappingKeys.ItemPostingGroup | MappingKeys.ReasonCode, AccountRoles.Get("INVENTORY_ADJUSTMENT").Keys);
        Assert.Equal(MappingKeys.ItemPostingGroup | MappingKeys.ShipToScope | MappingKeys.TaxCode, AccountRoles.Get("REVENUE").Keys);
        Assert.Equal(MappingKeys.None, AccountRoles.Get("BANK").Keys);
        // Each role uses at most one of party posting group and ship-to scope (resolution step 2).
        Assert.All(AccountRoles.All, r => Assert.False(r.Uses(MappingKeys.PartyPostingGroup) && r.Uses(MappingKeys.ShipToScope)));
        Assert.False(AccountRoles.Contains("inventory"));
        Assert.Equal("item group TRADE_GOODS, reason code Scrap", new PostingKeys(Code.From("TRADE_GOODS"), ReasonCode: ReasonCode.Scrap).Describe());
        Assert.Equal("no keys", PostingKeys.None.Describe());
    }
}

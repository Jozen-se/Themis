using Microsoft.Extensions.DependencyInjection;
using Themis.Integration.Tests.Pipeline;
using Themis.Kernel.Contracts.Persistence;
using Themis.Kernel.Contracts.Posting;
using Themis.Kernel.Organisation;
using Themis.Kernel.Posting;
using Themis.Kernel.Primitives;

namespace Themis.Integration.Tests.Posting;

/// <summary>
/// The resolver on the seeded fixture through the unit of work as themis_kernel (step 6 brief, Tests): INVENTORY for
/// TRADE_GOODS gives 1460, INVENTORY_ADJUSTMENT for TRADE_GOODS and CountDifference gives TEST-ADJ, and a Scrap
/// adjustment dated 2026-06-15 is rejected with R-PP-02 naming the role, the keys and the date. Step 8 proves the end
/// to end part with a posted document.
/// </summary>
[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Integration")]
public sealed class AccountResolutionTests : IAsyncLifetime
{
    private static readonly Guid Company = Milestone1OrganisationFixture.CompanyId;
    private static readonly Code TradeGoods = Code.From(Milestone1GeneralLedgerFixture.TradeGoods);
    private static readonly BusinessDate June = BusinessDate.Parse("2026-06-15");

    private readonly PostgresFixture _db;
    private PipelineTestHost _host = null!;

    public AccountResolutionTests(PostgresFixture db)
    {
        _db = db;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _host = await PipelineTestHost.StartAsync(_db);

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task R_PP_01_the_fixture_resolves_INVENTORY_to_1460_and_INVENTORY_ADJUSTMENT_CountDifference_to_TEST_ADJ()
    {
        IAccountResolver resolver = _host.Services.GetRequiredService<IAccountResolver>();
        await using IKernelUnitOfWork unitOfWork = await _host.Services.GetRequiredService<IUnitOfWorkFactory>().BeginAsync(Ct);

        Result<AccountResolution> inventory = await resolver.ResolveAsync(unitOfWork, Company, AccountRoles.Inventory, June, new PostingKeys(TradeGoods), Ct);
        Result<AccountResolution> adjustment = await resolver.ResolveAsync(unitOfWork, Company, AccountRoles.InventoryAdjustment, June, new PostingKeys(TradeGoods, ReasonCode: ReasonCode.CountDifference), Ct);

        Assert.True(inventory.IsSuccess);
        Assert.Equal(Milestone1GeneralLedgerFixture.InventoryAccountId, inventory.Value.AccountId);
        Assert.Equal(AccountRoles.Inventory, inventory.Value.AccountRole);
        Assert.Equal(Milestone1GeneralLedgerFixture.InventoryRuleId, inventory.Value.PostingRuleId);
        Assert.True(adjustment.IsSuccess);
        Assert.Equal(Milestone1GeneralLedgerFixture.AdjustmentAccountId, adjustment.Value.AccountId);
        Assert.Equal(Milestone1GeneralLedgerFixture.InventoryAdjustmentRuleId, adjustment.Value.PostingRuleId);
    }

    [Fact]
    public async Task R_PP_02_a_Scrap_adjustment_on_2026_06_15_has_no_rule_and_the_rejection_names_the_role_the_keys_and_the_date()
    {
        IAccountResolver resolver = _host.Services.GetRequiredService<IAccountResolver>();
        await using IKernelUnitOfWork unitOfWork = await _host.Services.GetRequiredService<IUnitOfWorkFactory>().BeginAsync(Ct);

        Result<AccountResolution> scrap = await resolver.ResolveAsync(unitOfWork, Company, AccountRoles.InventoryAdjustment, June, new PostingKeys(TradeGoods, ReasonCode: ReasonCode.Scrap), Ct);
        Result<AccountResolution> beforeValidity = await resolver.ResolveAsync(unitOfWork, Company, AccountRoles.Inventory, BusinessDate.Parse("2025-12-31"), new PostingKeys(TradeGoods), Ct);
        Result<AccountResolution> otherCompany = await resolver.ResolveAsync(unitOfWork, Guid.NewGuid(), AccountRoles.Inventory, June, new PostingKeys(TradeGoods), Ct);

        Assert.True(scrap.IsFailure);
        Assert.Equal("R-PP-02", scrap.Primary!.RuleId);
        Assert.Equal(AccountResolver.RuleMissing, scrap.Primary.Code);
        Assert.Equal("no account for INVENTORY_ADJUSTMENT, item group TRADE_GOODS, reason code Scrap, posting date 2026-06-15", scrap.Primary.Message);
        Assert.Equal("R-PP-02", beforeValidity.Primary!.RuleId);
        Assert.Equal("R-PP-02", otherCompany.Primary!.RuleId);
    }
}

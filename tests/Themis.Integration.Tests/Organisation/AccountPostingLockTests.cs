using Microsoft.Extensions.DependencyInjection;
using Themis.Integration.Tests.Pipeline;
using Themis.Kernel.Contracts.Organisation;
using Themis.Kernel.Organisation;
using Themis.Kernel.Posting;
using static Themis.Integration.Tests.Organisation.AccountTestData;

namespace Themis.Integration.Tests.Organisation;

/// <summary>
/// The account lock class of a posting (milestone 2 step 4 brief, item 4; D-69, R-LED-08): after resolution and just
/// before the number series the posting reads the accounts it resolved FOR SHARE on the account table alone, and the
/// status from that locked result decides (R-PP-02). Through the commands the R-PP-05 guard already stops BLOCK on an
/// account a posting can resolve, so these tests change the status through the account status seam, which skips the
/// guard (docs/development/test-seams.md). Also the step 3 follow-ups RR-130 and RR-133.
/// </summary>
[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Integration")]
public sealed class AccountPostingLockTests : IAsyncLifetime
{
    private const string Adjustments = "/v1/kernel/inventory-adjustments";
    private const string June = "2026-06-15";

    private static readonly Guid Inventory1460 = Milestone1GeneralLedgerFixture.InventoryAccountId;
    private static readonly Guid TestAdj = Milestone1GeneralLedgerFixture.AdjustmentAccountId;
    private static readonly Guid Warehouse = Milestone1OrganisationFixture.WarehouseId;

    private readonly PostgresFixture _db;
    private PipelineTestHost _host = null!;

    public AccountPostingLockTests(PostgresFixture db)
    {
        _db = db;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private PostingFailureSwitch Failures => _host.Services.GetRequiredService<PostingFailureSwitch>();

    public async ValueTask InitializeAsync()
    {
        await _db.TruncateAsync(Ct);
        _host = await PipelineTestHost.StartAsync(_db);
    }

    public async ValueTask DisposeAsync()
    {
        Failures.AfterLedgerWrites = null;
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task R_PP_02_a_posting_that_resolves_a_rule_whose_account_is_not_Active_set_through_the_seam_is_rejected_and_writes_nothing()
    {
        Guid draft = await DraftAsync("pp02");
        await SetStatusAsync(_db, TestAdj, MasterDataStatus.Blocked, Ct);

        CommandReply posted = await PostAsync(draft, "pp02-post");

        Assert.Equal(422, posted.Status);
        Assert.Equal(("R-PP-02", AccountResolver.AccountNotActive), (posted.RuleId, posted.Code));
        await AssertNothingPostedAsync(draft);
    }

    [Fact]
    public async Task R_LED_08_D_69_order_A_the_posting_holds_its_account_lock_and_the_seams_update_waits_until_the_posting_commits()
    {
        Guid draft = await DraftAsync("order-a");
        TaskCompletionSource paused = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Failures.AfterLedgerWrites = async (_, _, cancellationToken) =>
        {
            paused.TrySetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
        };

        Task<CommandReply> posting = PostAsync(draft, "order-a-post");
        await paused.Task.WaitAsync(TimeSpan.FromSeconds(15), Ct);
        // The posting holds 1460 and TEST-ADJ FOR SHARE: the seam's UPDATE of the row waits on it (the wait, not only the outcome).
        Task update = SetStatusAsync(_db, Inventory1460, MasterDataStatus.Blocked, Ct);
        await WaitForLockWaiterAsync(_host, Ct);
        Assert.False(update.IsCompleted);
        Assert.Equal(1L, await _host.ScalarAsync("SELECT count(*) FROM pg_locks l JOIN pg_stat_activity a ON a.pid = l.pid WHERE NOT l.granted AND l.locktype IN ('transactionid', 'tuple') AND a.datname = current_database()", Ct));

        release.SetResult();
        CommandReply postedReply = await posting;
        await update.WaitAsync(TimeSpan.FromSeconds(15), Ct);

        Assert.True(postedReply.Status == 200, postedReply.Body);
        Assert.Equal("Blocked", await _host.ScalarAsync("SELECT status FROM kernel.account WHERE id = @id", Ct, ("id", Inventory1460)));
        Assert.Equal(1L, await _host.CountAsync("kernel.journal_entry", Ct));
        Assert.Equal(0L, _host.Retries);
    }

    [Fact]
    public async Task R_LED_08_D_69_order_B_the_seams_update_holds_the_account_row_the_posting_waits_and_is_then_rejected_under_R_PP_02_and_writes_nothing()
    {
        Guid draft = await DraftAsync("order-b");
        await using HeldStatus held = await HoldStatusAsync(_db, TestAdj, MasterDataStatus.Blocked, Ct);

        // The posting resolves from the committed row (Active), then waits on its FOR SHARE behind the held update.
        Task<CommandReply> posting = PostAsync(draft, "order-b-post");
        await WaitForLockWaiterAsync(_host, Ct);
        Assert.False(posting.IsCompleted);
        await held.CommitAsync(Ct);
        CommandReply postedReply = await posting.WaitAsync(TimeSpan.FromSeconds(15), Ct);

        Assert.Equal(422, postedReply.Status);
        Assert.Equal(("R-PP-02", AccountResolver.AccountNotActive), (postedReply.RuleId, postedReply.Code));
        Assert.Contains("which is Blocked (D-69)", postedReply.Body, StringComparison.Ordinal);
        await AssertNothingPostedAsync(draft);
        Assert.Equal(0L, _host.Retries);
    }

    [Fact]
    public async Task R_MD_06_RR_130_after_the_first_posting_a_reformatted_organisation_number_is_accepted_and_a_different_one_is_rejected()
    {
        CommandReply posted = await PostAsync(await DraftAsync("rr130"), "rr130-post");
        Assert.True(posted.Status == 200, posted.Body);

        string reformatted = Milestone1OrganisationFixture.OrganisationNumber.Replace("-", " ", StringComparison.Ordinal);
        CommandReply same = await _host.PostAsync(OrganisationBodies.CompanyUpdate(Se01), OrganisationBodies.Se01Update(organisationNumber: reformatted), "rr130-same", ifMatch: "\"1\"", cancellationToken: Ct);
        CommandReply other = await _host.PostAsync(OrganisationBodies.CompanyUpdate(Se01), OrganisationBodies.Se01Update(organisationNumber: "555555-5556"), "rr130-other", ifMatch: "\"2\"", cancellationToken: Ct);

        Assert.True(same.Status == 200, same.Body);
        Assert.Equal(reformatted, same.Json!["organisationNumber"]!.GetValue<string>());
        Assert.Equal(422, other.Status);
        Assert.Equal(("R-MD-06", "company_organisation_number_fixed"), (other.RuleId, other.Code));
    }

    [Fact]
    public async Task RR_133_the_shared_cleanup_restores_SE01s_posting_profile_rules_and_the_fixture_charts_accounts_as_seeded_and_deletes_accounts_tests_create()
    {
        string seededAccounts = await AccountsAsync();
        string seededRules = await RulesAsync();
        (Guid created, _) = await CreateAccountAsync(_host, "6931", cancellationToken: Ct);
        await SeedRulesAsync(_db, [AdjustmentRule(Se01, created, Kernel.Contracts.Posting.ReasonCode.Scrap, "2026-01-01")], new Dictionary<Guid, Guid> { [Se01] = Bas }, checkCharts: true, Ct);
        CommandReply renamed = await _host.PostAsync(Update(Inventory1460), new { name = "Renamed by a test", accountType = "Asset", isControl = true }, "rr133-rename", ifMatch: "\"1\"", cancellationToken: Ct);
        Assert.True(renamed.Status == 200, renamed.Body);
        await SetStatusAsync(_db, TestAdj, MasterDataStatus.Inactive, Ct);
        Assert.NotEqual(seededAccounts, await AccountsAsync());
        Assert.NotEqual(seededRules, await RulesAsync());

        await _db.TruncateAsync(Ct);

        Assert.Equal(seededAccounts, await AccountsAsync());
        Assert.Equal(seededRules, await RulesAsync());
        Assert.Equal(0L, await _host.ScalarAsync("SELECT count(*) FROM kernel.account WHERE id = @id", Ct, ("id", created)));
    }

    [Fact]
    public async Task RR_133_both_WAREHOUSE_CREATE_400s_carry_details_field()
    {
        CommandReply unknown = await _host.PostAsync(OrganisationBodies.Warehouses, new { companyId = Se01, siteId = Guid.NewGuid(), code = "W9", name = "Nowhere" }, "rr133-unknown", cancellationToken: Ct);
        Guid se02 = await CreateCompanyAsync(_host, "SE02", "556000-0002", Bas, Ct);
        CommandReply other = await _host.PostAsync(OrganisationBodies.Warehouses, new { companyId = se02, siteId = Milestone1OrganisationFixture.SiteId, code = "W9", name = "Other company" }, "rr133-other", cancellationToken: Ct);

        Assert.Equal((400, "site_unknown"), (unknown.Status, unknown.Code));
        Assert.Equal("siteId", unknown.Json!["errors"]![0]!["details"]!["field"]!.GetValue<string>());
        Assert.Equal((400, "site_other_company"), (other.Status, other.Code));
        Assert.Equal("siteId", other.Json!["errors"]![0]!["details"]!["field"]!.GetValue<string>());
    }

    private async Task<string> AccountsAsync() =>
        (string)(await _host.ScalarAsync("SELECT string_agg(concat_ws('|', id, chart_id, account_number, name, account_type, is_control, status, version), ',' ORDER BY id) FROM kernel.account", Ct))!;

    private async Task<string> RulesAsync() =>
        (string)(await _host.ScalarAsync("SELECT string_agg(concat_ws('|', id, company_id, account_role, item_posting_group, reason_code, account_id, valid_from, valid_to, version), ',' ORDER BY id) FROM kernel.posting_profile_rule", Ct))!;

    private async Task<Guid> DraftAsync(string key)
    {
        CommandReply item = await _host.PostAsync("/v1/kernel/items", new { companyId = Se01, itemNumber = "LOCK-" + key.ToUpperInvariant(), description = "Lock test item", itemType = "Stocked", baseUnit = "H87", lotTracking = "None", itemPostingGroup = "TRADE_GOODS", taxClass = "Standard", costingMethod = "MovingAverage" }, "item-" + key, subject: TestUsers.ClerkSubject, cancellationToken: Ct);
        Assert.True(item.Status == 200, item.Body);
        Guid itemId = item.Json!["id"]!.GetValue<Guid>();
        CommandReply draft = await _host.PostAsync(Adjustments, new { companyId = Se01, documentDate = June, reasonCode = "CountDifference", warehouseId = Warehouse, postingDate = June, lines = new[] { new { lineNumber = 1, itemId, quantity = "10", warehouseId = Warehouse, stockStatus = "Available", enteredUnitCost = "1.00" } } }, "draft-" + key, subject: TestUsers.ClerkSubject, cancellationToken: Ct);
        Assert.True(draft.Status == 200, draft.Body);
        return draft.Json!["id"]!.GetValue<Guid>();
    }

    private Task<CommandReply> PostAsync(Guid id, string key) =>
        _host.PostAsync($"{Adjustments}/{id}/post", null, key, ifMatch: "\"1\"", subject: TestUsers.ClerkSubject, cancellationToken: Ct);

    private async Task AssertNothingPostedAsync(Guid draft)
    {
        Assert.Equal("Draft", await _host.ScalarAsync("SELECT status FROM kernel.document WHERE id = @id", Ct, ("id", draft)));
        Assert.Equal(0L, await _host.CountAsync("kernel.journal_entry", Ct));
        Assert.Equal(0L, await _host.CountAsync("kernel.inventory_entry", Ct));
        Assert.Equal(0L, await _host.ScalarAsync("SELECT count(*) FROM kernel.outbox_event WHERE event_type = 'INVENTORY_ADJUSTED'", Ct));
    }
}

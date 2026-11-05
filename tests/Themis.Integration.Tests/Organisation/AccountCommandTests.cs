using Npgsql;
using Themis.Integration.Tests.Pipeline;
using Themis.Kernel.Contracts.Commands;
using Themis.Kernel.Contracts.Organisation;
using Themis.Kernel.Contracts.Posting;
using Themis.Kernel.Contracts.Security;
using Themis.Kernel.Identity;
using Themis.Kernel.Organisation;
using Themis.Kernel.Pipeline.Dispatch;
using Themis.Kernel.Posting;
using Themis.Kernel.Primitives;
using static Themis.Integration.Tests.Organisation.AccountTestData;

namespace Themis.Integration.Tests.Organisation;

/// <summary>
/// The account commands end to end (milestone 2 step 4 brief, items 1 to 3 and 5; D-69): CREATE, UPDATE and the
/// lifecycle with their R-PP-05, R-DOC-01 and R-MD-06 guards, READ by id and per chart, the installation scope, the
/// pipeline conventions (R-API-01, R-API-02, R-API-04) and one audit event and one outbox event per changed account with
/// the D-50 envelope. Rules for the guards come from the test and development seeder (AccountTestData).
/// </summary>
[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Integration")]
public sealed class AccountCommandTests : IAsyncLifetime
{
    private static readonly Guid Inventory1460 = Milestone1GeneralLedgerFixture.InventoryAccountId;
    private static readonly Guid TestAdj = Milestone1GeneralLedgerFixture.AdjustmentAccountId;

    private readonly PostgresFixture _db;
    private PipelineTestHost _host = null!;
    private OrganisationTestUsers _users = null!;

    public AccountCommandTests(PostgresFixture db)
    {
        _db = db;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await _db.TruncateAsync(Ct);
        _host = await PipelineTestHost.StartAsync(_db);
        _users = new OrganisationTestUsers(_db);
    }

    public async ValueTask DisposeAsync()
    {
        await _users.DisposeAsync();
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task ACCOUNT_CREATE_D_69_R_AUD_01_D_44_writes_an_Active_account_with_one_audit_event_and_one_event_without_a_company_D_50_and_R_API_01_a_replay_writes_nothing()
    {
        CommandReply reply = await _host.PostAsync(Accounts, Create("3010", "Revenue", name: "Försäljning varor"), "acc-1", correlationId: "corr-acc-1", cancellationToken: Ct);
        CommandReply replay = await _host.PostAsync(Accounts, Create("3010", "Revenue", name: "Försäljning varor"), "acc-1", cancellationToken: Ct);

        Assert.True(reply.Status == 200, reply.Body);
        Assert.Equal("\"1\"", reply.ETag);
        Guid id = reply.Json!["id"]!.GetValue<Guid>();
        Assert.Equal(("3010", "Försäljning varor", "Revenue", false, "Active", 1L), (reply.Json["accountNumber"]!.GetValue<string>(), reply.Json["name"]!.GetValue<string>(), reply.Json["accountType"]!.GetValue<string>(), reply.Json["isControl"]!.GetValue<bool>(), reply.Json["status"]!.GetValue<string>(), reply.Json["version"]!.GetValue<long>()));
        Assert.Equal(Bas, reply.Json["chartId"]!.GetValue<Guid>());
        Assert.Equal(TestUsers.OrganisationAdministrator, await _host.ScalarAsync("SELECT created_by FROM kernel.account WHERE id = @id", Ct, ("id", id)));

        const string audit = "FROM kernel.audit_event WHERE command_name = 'ACCOUNT.CREATE'";
        Assert.Equal(1L, await _host.ScalarAsync($"SELECT count(*) {audit}", Ct));
        Assert.Equal(id, await _host.ScalarAsync($"SELECT entity_id {audit} AND entity_type = 'Account'", Ct));
        Assert.Equal("corr-acc-1", await _host.ScalarAsync($"SELECT correlation_id {audit}", Ct));
        Assert.Equal("3010", await _host.ScalarAsync($"SELECT changes->'accountNumber'->>'after' {audit}", Ct));
        Assert.Equal(("ACCOUNT_CREATED", "ACCOUNT", id, null, "ACCOUNT.CREATE"), await EnvelopeAsync("ACCOUNT_CREATED"));
        Assert.Equal(1L, await _host.CountAsync("kernel.outbox_event", Ct));

        Assert.True(replay.Replayed);
        Assert.Equal(reply.Body, replay.Body);
        Assert.Equal(3L, await _host.CountAsync("kernel.account", Ct));
        Assert.Equal(1L, await _host.CountAsync("kernel.outbox_event", Ct));
    }

    [Fact]
    public async Task R_MD_05_a_duplicate_account_number_in_the_same_chart_is_422_and_stored_and_the_same_number_in_another_chart_is_accepted()
    {
        CommandReply chart = await _host.PostAsync(OrganisationBodies.Charts, new { code = "K3", name = "K3-kontoplan" }, "acc-chart", cancellationToken: Ct);
        Guid k3 = chart.Json!["id"]!.GetValue<Guid>();

        CommandReply duplicate = await _host.PostAsync(Accounts, Create("1460", "Asset", isControl: true), "acc-dup", cancellationToken: Ct);
        CommandReply replay = await _host.PostAsync(Accounts, Create("1460", "Asset", isControl: true), "acc-dup", cancellationToken: Ct);
        CommandReply other = await _host.PostAsync(Accounts, Create("1460", "Asset", isControl: true, chartId: k3), "acc-other", cancellationToken: Ct);
        CommandReply unknownChart = await _host.PostAsync(Accounts, Create("1460", chartId: Guid.NewGuid()), "acc-nochart", cancellationToken: Ct);

        Assert.Equal(422, duplicate.Status);
        Assert.Equal(("R-MD-05", "account_number_duplicate"), (duplicate.RuleId, duplicate.Code));
        Assert.True(replay.Replayed);
        Assert.True(other.Status == 200, other.Body);
        // A missing chart is a 400 found inside the transaction, stored as the key's result, with the field it names.
        Assert.Equal(400, unknownChart.Status);
        Assert.Equal("chart_of_accounts_unknown", unknownChart.Code);
        Assert.Equal("chartId", unknownChart.Json!["errors"]![0]!["details"]!["field"]!.GetValue<string>());
        Assert.Equal(1L, await _host.ScalarAsync("SELECT count(*) FROM kernel.account WHERE chart_id = @chart", Ct, ("chart", k3)));
    }

    [Fact]
    public async Task R_MD_06_D_69_the_number_and_chart_are_fixed_the_type_and_control_flag_change_until_the_first_rule_in_any_company_and_then_are_422_with_the_attribute_code()
    {
        (Guid account, _) = await CreateAccountAsync(_host, "6991", cancellationToken: Ct);
        CommandReply number = await _host.PostAsync(Update(account), new { name = "Test account", accountType = "Expense", isControl = false, accountNumber = "6992" }, "upd-number", ifMatch: "\"1\"", cancellationToken: Ct);
        CommandReply chart = await _host.PostAsync(Update(account), new { name = "Test account", accountType = "Expense", isControl = false, chartId = Guid.NewGuid() }, "upd-chart", ifMatch: "\"1\"", cancellationToken: Ct);
        CommandReply sameNumber = await _host.PostAsync(Update(account), new { name = "Renamed", accountType = "Asset", isControl = false, accountNumber = "6991", chartId = Bas }, "upd-before", ifMatch: "\"1\"", cancellationToken: Ct);

        Assert.Equal(("R-MD-06", "account_number_fixed"), (number.RuleId, number.Code));
        Assert.Equal(("R-MD-06", "account_chart_fixed"), (chart.RuleId, chart.Code));
        // Before the trigger the type changes; an equal number and chart are ignored.
        Assert.True(sameNumber.Status == 200, sameNumber.Body);
        Assert.Equal(("Asset", 2L), (sameNumber.Json!["accountType"]!.GetValue<string>(), sameNumber.Json["version"]!.GetValue<long>()));

        // The trigger in another company: a rule of a second company on the same chart references the account.
        Guid se02 = await CreateCompanyAsync(_host, "SE02", "556000-0002", Bas, Ct);
        await SeedRulesAsync(_db, [AdjustmentRule(se02, account, ReasonCode.Scrap, "2026-01-01")], new Dictionary<Guid, Guid> { [se02] = Bas }, checkCharts: true, Ct);

        CommandReply type = await _host.PostAsync(Update(account), new { name = "Renamed", accountType = "Expense", isControl = false }, "upd-type", ifMatch: "\"2\"", cancellationToken: Ct);
        CommandReply renamed = await _host.PostAsync(Update(account), new { name = "Renamed again", accountType = "Asset", isControl = false }, "upd-name", ifMatch: "\"2\"", cancellationToken: Ct);

        Assert.Equal(422, type.Status);
        Assert.Equal(("R-MD-06", "account_type_fixed"), (type.RuleId, type.Code));
        Assert.True(renamed.Status == 200, renamed.Body);
        Assert.Equal(("Asset", "Renamed again", 3L), (renamed.Json!["accountType"]!.GetValue<string>(), renamed.Json["name"]!.GetValue<string>(), renamed.Json["version"]!.GetValue<long>()));
        Assert.Equal("Asset", await _host.ScalarAsync("SELECT account_type FROM kernel.account WHERE id = @id", Ct, ("id", account)));
    }

    [Fact]
    public async Task R_PP_04_once_a_rule_references_the_account_an_is_control_change_is_422_under_R_MD_06_never_the_raw_foreign_key_error()
    {
        CommandReply control = await _host.PostAsync(Update(Inventory1460), new { name = Milestone1GeneralLedgerFixture.InventoryAccountName, accountType = "Asset", isControl = false }, "upd-control", ifMatch: "\"1\"", cancellationToken: Ct);

        Assert.Equal(422, control.Status);
        Assert.Equal(("R-MD-06", "account_control_fixed"), (control.RuleId, control.Code));
        Assert.DoesNotContain("fk_posting_profile_rule_account_control", control.Body, StringComparison.Ordinal);
        Assert.Equal(true, await _host.ScalarAsync("SELECT is_control FROM kernel.account WHERE id = @id", Ct, ("id", Inventory1460)));
        Assert.Equal(1L, await _host.ScalarAsync("SELECT version FROM kernel.account WHERE id = @id", Ct, ("id", Inventory1460)));
    }

    [Fact]
    public async Task R_PP_05_D_56_BLOCK_and_DEACTIVATE_of_an_account_an_active_rule_references_are_422_account_used_by_posting_rule_naming_the_rule()
    {
        CommandReply block = await _host.PostAsync(Lifecycle(TestAdj, "block"), new { reason = "Retired" }, "blk-fixture", ifMatch: "\"1\"", cancellationToken: Ct);
        CommandReply deactivate = await _host.PostAsync(Lifecycle(TestAdj, "deactivate"), null, "deact-fixture", ifMatch: "\"1\"", cancellationToken: Ct);

        Assert.Equal(422, block.Status);
        Assert.Equal(("R-PP-05", "account_used_by_posting_rule"), (block.RuleId, block.Code));
        Assert.Contains("company SE01, role INVENTORY_ADJUSTMENT, item group TRADE_GOODS, reason code CountDifference, valid from 2026-01-01 (open-ended)", block.Body, StringComparison.Ordinal);
        Assert.Equal(Milestone1GeneralLedgerFixture.InventoryAdjustmentRuleId.ToString(), block.Json!["errors"]![0]!["details"]!["ruleIds"]!.GetValue<string>());
        Assert.Equal(422, deactivate.Status);
        Assert.Equal(("R-PP-05", "account_used_by_posting_rule"), (deactivate.RuleId, deactivate.Code));
        // The versioned update ran first and was rolled back with the rejection: nothing changed, the rejection is stored.
        Assert.Equal(("Active", 1L), await StatusAsync(TestAdj));
        Assert.Equal(0L, await _host.CountAsync("kernel.outbox_event", Ct));
        Assert.Equal(2L, await _host.ScalarAsync("SELECT count(*) FROM kernel.idempotency_record WHERE key IN ('blk-fixture', 'deact-fixture')", Ct));
    }

    [Fact]
    public async Task R_PP_05_D_69_a_rule_of_a_second_company_counts_a_future_dated_rule_counts_an_open_ended_rule_counts_in_a_company_without_fiscal_years_and_a_rule_inside_Closed_periods_does_not()
    {
        Guid se02 = await CreateCompanyAsync(_host, "SE02", "556000-0002", Bas, Ct);
        Guid se03 = await CreateCompanyAsync(_host, "SE03", "556000-0003", Bas, Ct);
        // SE03 has fiscal year 2025 with January to June Closed; SE02 has no fiscal year at all.
        await InsertFiscalYearAsync(_db, se03, 2025, closedPeriods: 6, Ct);
        Dictionary<Guid, Guid> charts = new() { [se02] = Bas, [se03] = Bas, [Se01] = Bas };

        (Guid secondCompany, _) = await CreateAccountAsync(_host, "6981", cancellationToken: Ct);
        (Guid futureDated, _) = await CreateAccountAsync(_host, "6982", cancellationToken: Ct);
        (Guid openEnded, _) = await CreateAccountAsync(_host, "6983", cancellationToken: Ct);
        (Guid closed, _) = await CreateAccountAsync(_host, "6984", cancellationToken: Ct);
        (Guid unused, _) = await CreateAccountAsync(_host, "6985", cancellationToken: Ct);
        await SeedRulesAsync(_db,
        [
            // A rule of a second company, ended: SE02 has no Closed period, so it is active.
            AdjustmentRule(se02, secondCompany, ReasonCode.Scrap, "2025-01-01", "2025-02-01"),
            // A future-dated rule of SE01.
            AdjustmentRule(Se01, futureDated, ReasonCode.Scrap, "2030-01-01"),
            // An open-ended rule in a company without fiscal years.
            AdjustmentRule(se02, openEnded, ReasonCode.Other, "2026-01-01"),
            // A rule whose whole validity, January to May 2025, lies inside SE03's Closed periods.
            AdjustmentRule(se03, closed, ReasonCode.Scrap, "2025-01-01", "2025-06-01"),
        ], charts, checkCharts: true, Ct);

        foreach ((Guid account, string label) in new[] { (secondCompany, "second company"), (futureDated, "future-dated"), (openEnded, "open-ended") })
        {
            CommandReply block = await _host.PostAsync(Lifecycle(account, "block"), new { reason = "Retired" }, $"blk-{label.Replace(' ', '-')}", ifMatch: "\"1\"", cancellationToken: Ct);
            Assert.True(block.Status == 422 && block.Code == "account_used_by_posting_rule", $"{label}: {block.Body}");
        }

        CommandReply inside = await _host.PostAsync(Lifecycle(closed, "block"), new { reason = "Retired" }, "blk-closed", ifMatch: "\"1\"", cancellationToken: Ct);
        CommandReply free = await _host.PostAsync(Lifecycle(unused, "deactivate"), null, "deact-free", ifMatch: "\"1\"", cancellationToken: Ct);
        Assert.True(inside.Status == 200, inside.Body);
        Assert.Equal("Blocked", inside.Json!["status"]!.GetValue<string>());
        Assert.True(free.Status == 200, free.Body);
    }

    [Fact]
    public async Task R_PP_05_a_rule_that_ended_inside_an_Open_period_still_counts_as_active()
    {
        (Guid account, _) = await CreateAccountAsync(_host, "6986", cancellationToken: Ct);
        // SE01's fiscal year 2026 has period 1 Closed and periods 2 to 12 Open: a rule of February alone lies inside an Open period.
        await SeedRulesAsync(_db, [AdjustmentRule(Se01, account, ReasonCode.Scrap, "2026-02-01", "2026-03-01")], new Dictionary<Guid, Guid> { [Se01] = Bas }, checkCharts: true, Ct);

        CommandReply block = await _host.PostAsync(Lifecycle(account, "block"), new { reason = "Retired" }, "blk-ended", ifMatch: "\"1\"", cancellationToken: Ct);

        Assert.Equal(("R-PP-05", "account_used_by_posting_rule"), (block.RuleId, block.Code));
        Assert.Contains("valid from 2026-02-01 to 2026-03-01 (exclusive)", block.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task R_PP_05_D_69_the_check_finds_a_referencing_rule_by_account_even_in_a_company_whose_chart_differs()
    {
        CommandReply chart = await _host.PostAsync(OrganisationBodies.Charts, new { code = "K3", name = "K3-kontoplan" }, "acc-k3", cancellationToken: Ct);
        Guid k3 = chart.Json!["id"]!.GetValue<Guid>();
        Guid se04 = await CreateCompanyAsync(_host, "SE04", "556000-0004", k3, Ct);
        (Guid account, _) = await CreateAccountAsync(_host, "6987", cancellationToken: Ct);
        // Nothing ties a rule's account to its company's chart before Gate B, so the seeder writes it with its chart check off.
        await SeedRulesAsync(_db, [AdjustmentRule(se04, account, ReasonCode.Scrap, "2026-01-01")], new Dictionary<Guid, Guid> { [se04] = k3 }, checkCharts: false, Ct);

        CommandReply block = await _host.PostAsync(Lifecycle(account, "block"), new { reason = "Retired" }, "blk-k3", ifMatch: "\"1\"", cancellationToken: Ct);

        Assert.Equal(("R-PP-05", "account_used_by_posting_rule"), (block.RuleId, block.Code));
        Assert.Contains("company SE04", block.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task R_MD_06_D_69_BLOCK_needs_a_reason_400_reason_required_stores_nothing_a_reason_over_500_is_400_and_an_accepted_reason_is_in_the_audit_event_and_the_ACCOUNT_BLOCKED_payload_and_UNBLOCK_restores_Active()
    {
        (Guid account, _) = await CreateAccountAsync(_host, "6971", cancellationToken: Ct);
        CommandReply empty = await _host.PostAsync(Lifecycle(account, "block"), new { reason = "  " }, "blk-empty", ifMatch: "\"1\"", cancellationToken: Ct);
        CommandReply missing = await _host.PostAsync(Lifecycle(account, "block"), new { }, "blk-missing", ifMatch: "\"1\"", cancellationToken: Ct);
        CommandReply tooLong = await _host.PostAsync(Lifecycle(account, "block"), new { reason = new string('x', 501) }, "blk-long", ifMatch: "\"1\"", cancellationToken: Ct);

        Assert.Equal((400, "reason_required"), (empty.Status, empty.Code));
        Assert.Equal((400, "reason_required"), (missing.Status, missing.Code));
        Assert.Equal((400, "reason_too_long"), (tooLong.Status, tooLong.Code));
        Assert.Equal(0L, await _host.ScalarAsync("SELECT count(*) FROM kernel.idempotency_record WHERE key LIKE 'blk-%'", Ct));
        Assert.Equal(("Active", 1L), await StatusAsync(account));

        string reason = new('r', 500);
        CommandReply blocked = await _host.PostAsync(Lifecycle(account, "block"), new { reason }, "blk-ok", ifMatch: "\"1\"", cancellationToken: Ct);
        Assert.True(blocked.Status == 200, blocked.Body);
        Assert.Equal(("Blocked", 2L), (blocked.Json!["status"]!.GetValue<string>(), blocked.Json["version"]!.GetValue<long>()));
        Assert.Equal(reason, await _host.ScalarAsync("SELECT changes->'reason'->>'after' FROM kernel.audit_event WHERE command_name = 'ACCOUNT.BLOCK'", Ct));
        Assert.Equal("Blocked", await _host.ScalarAsync("SELECT changes->'status'->>'after' FROM kernel.audit_event WHERE command_name = 'ACCOUNT.BLOCK'", Ct));
        Assert.Equal(reason, await _host.ScalarAsync("SELECT payload->>'reason' FROM kernel.outbox_event WHERE event_type = 'ACCOUNT_BLOCKED'", Ct));
        Assert.Equal(("ACCOUNT_BLOCKED", "ACCOUNT", account, null, "ACCOUNT.BLOCK"), await EnvelopeAsync("ACCOUNT_BLOCKED"));
        // No column holds the reason (D-69).
        Assert.Equal(0L, await _host.ScalarAsync("SELECT count(*) FROM information_schema.columns WHERE table_schema = 'kernel' AND table_name = 'account' AND column_name LIKE '%reason%'", Ct));

        // UPDATE runs in Blocked; UNBLOCK restores Active; a second UNBLOCK is not a transition (R-DOC-01).
        CommandReply renamed = await _host.PostAsync(Update(account), new { name = "Blocked but renamed", accountType = "Expense", isControl = false }, "upd-blocked", ifMatch: "\"2\"", cancellationToken: Ct);
        CommandReply unblocked = await _host.PostAsync(Lifecycle(account, "unblock"), null, "unblk-ok", ifMatch: "\"3\"", cancellationToken: Ct);
        CommandReply again = await _host.PostAsync(Lifecycle(account, "unblock"), null, "unblk-again", ifMatch: "\"4\"", cancellationToken: Ct);
        Assert.True(renamed.Status == 200, renamed.Body);
        Assert.True(unblocked.Status == 200, unblocked.Body);
        Assert.Equal(("Active", 4L), (unblocked.Json!["status"]!.GetValue<string>(), unblocked.Json["version"]!.GetValue<long>()));
        Assert.Equal(("R-DOC-01", "transition_not_allowed"), (again.RuleId, again.Code));
        Assert.Equal(("ACCOUNT_UNBLOCKED", "ACCOUNT", account, null, "ACCOUNT.UNBLOCK"), await EnvelopeAsync("ACCOUNT_UNBLOCKED"));
    }

    [Fact]
    public async Task R_DOC_01_D_69_DEACTIVATE_with_a_non_zero_lifetime_balance_is_422_account_balance_not_zero_and_with_a_zero_balance_and_no_active_rule_it_succeeds_and_REACTIVATE_restores_Active()
    {
        // A posting through the fixture rules gives TEST-ADJ a balance of -10.00 in SE01.
        await PostCountDifferenceAsync("10", "1.00");
        CommandReply busy = await _host.PostAsync(Lifecycle(TestAdj, "deactivate"), null, "deact-busy", ifMatch: "\"1\"", cancellationToken: Ct);

        Assert.Equal(422, busy.Status);
        string[] codes = [.. busy.Json!["errors"]!.AsArray().Select(error => error!["code"]!.GetValue<string>()).Order(StringComparer.Ordinal)];
        Assert.Equal(["account_balance_not_zero", "account_used_by_posting_rule"], codes);
        Assert.Contains("company SE01: -10.0000", busy.Body, StringComparison.Ordinal);
        Assert.Equal("R-DOC-01", busy.Json["errors"]!.AsArray().Single(error => error!["code"]!.GetValue<string>() == "account_balance_not_zero")!["ruleId"]!.GetValue<string>());

        (Guid account, _) = await CreateAccountAsync(_host, "6961", cancellationToken: Ct);
        CommandReply deactivated = await _host.PostAsync(Lifecycle(account, "deactivate"), null, "deact-ok", ifMatch: "\"1\"", cancellationToken: Ct);
        CommandReply block = await _host.PostAsync(Lifecycle(account, "block"), new { reason = "Inactive already" }, "blk-inactive", ifMatch: "\"2\"", cancellationToken: Ct);
        CommandReply update = await _host.PostAsync(Update(account), new { name = "Inactive", accountType = "Expense", isControl = false }, "upd-inactive", ifMatch: "\"2\"", cancellationToken: Ct);
        CommandReply reactivated = await _host.PostAsync(Lifecycle(account, "reactivate"), null, "react-ok", ifMatch: "\"2\"", cancellationToken: Ct);

        Assert.True(deactivated.Status == 200, deactivated.Body);
        Assert.Equal("Inactive", deactivated.Json!["status"]!.GetValue<string>());
        Assert.Equal(("R-DOC-01", "transition_not_allowed"), (block.RuleId, block.Code));
        Assert.Equal(("R-DOC-01", "transition_not_allowed"), (update.RuleId, update.Code));
        Assert.True(reactivated.Status == 200, reactivated.Body);
        Assert.Equal(("Active", 3L), (reactivated.Json!["status"]!.GetValue<string>(), reactivated.Json["version"]!.GetValue<long>()));
        Assert.Equal(("ACCOUNT_DEACTIVATED", "ACCOUNT", account, null, "ACCOUNT.DEACTIVATE"), await EnvelopeAsync("ACCOUNT_DEACTIVATED"));
        Assert.Equal(("ACCOUNT_REACTIVATED", "ACCOUNT", account, null, "ACCOUNT.REACTIVATE"), await EnvelopeAsync("ACCOUNT_REACTIVATED"));
    }

    [Theory]
    [InlineData("block", "Blocked")]
    [InlineData("deactivate", "Inactive")]
    [InlineData("unblock", "Active")]
    [InlineData("reactivate", "Active")]
    [InlineData("reactivate", "Blocked")]
    [InlineData("update", "Inactive")]
    public async Task R_DOC_01_an_ACCOUNT_lifecycle_command_or_UPDATE_in_a_state_the_master_data_lifecycle_does_not_list_is_422_transition_not_allowed_and_changes_nothing(string verb, string state)
    {
        (Guid account, _) = await CreateAccountAsync(_host, "692" + verb.Length + state.Length, cancellationToken: Ct);
        if (state != "Active")
        {
            await SetStatusAsync(_db, account, Enum.Parse<MasterDataStatus>(state), Ct);
        }

        object? body = verb switch
        {
            "update" => new { name = "Renamed", accountType = "Expense", isControl = false },
            "block" => new { reason = "Again" },
            _ => null,
        };
        CommandReply reply = await _host.PostAsync(verb == "update" ? Update(account) : Lifecycle(account, verb), body, $"r-doc-01-{verb}-{state}", ifMatch: "\"1\"", cancellationToken: Ct);

        Assert.Equal(422, reply.Status);
        Assert.Equal(("R-DOC-01", "transition_not_allowed"), (reply.RuleId, reply.Code));
        Assert.Equal((state, 1L), await StatusAsync(account));
        Assert.Equal(0L, await _host.ScalarAsync("SELECT count(*) FROM kernel.outbox_event WHERE aggregate_id = @id AND event_type <> 'ACCOUNT_CREATED'", Ct, ("id", account)));
    }

    [Fact]
    public async Task R_MD_06_ACCOUNT_READ_returns_one_account_by_id_and_the_list_per_chart_is_ordered_by_account_number_and_stops_at_the_list_limit()
    {
        await CreateAccountAsync(_host, "0100", cancellationToken: Ct);
        await CreateAccountAsync(_host, "9999", cancellationToken: Ct);
        CommandReply one = await _host.GetAsync($"{Accounts}/{Inventory1460}", cancellationToken: Ct);
        CommandReply list = await _host.GetAsync(ChartAccounts(Bas), cancellationToken: Ct);
        CommandReply missing = await _host.GetAsync($"{Accounts}/{Guid.NewGuid()}", cancellationToken: Ct);
        CommandReply noChart = await _host.GetAsync(ChartAccounts(Guid.NewGuid()), cancellationToken: Ct);

        Assert.Equal(200, one.Status);
        Assert.Equal("\"1\"", one.ETag);
        Assert.Equal("1460", one.Json!["accountNumber"]!.GetValue<string>());
        Assert.Equal(["0100", "1460", "9999", "TEST-ADJ"], list.Json!.AsArray().Select(account => account!["accountNumber"]!.GetValue<string>()));
        Assert.Equal((404, "account_not_found"), (missing.Status, missing.Code));
        Assert.Equal((404, "chart_of_accounts_not_found"), (noChart.Status, noChart.Code));

        // The list stops at the step 3 list limit.
        CommandReply chart = await _host.PostAsync(OrganisationBodies.Charts, new { code = "BIG", name = "Large chart" }, "acc-big", cancellationToken: Ct);
        Guid big = chart.Json!["id"]!.GetValue<Guid>();
        string system = WellKnownUsers.System.Value.ToString();
        await _host.ScalarAsync(
            $"INSERT INTO kernel.account (id, chart_id, account_number, name, account_type, is_control, status, created_at, created_by, updated_at, updated_by) " +
            $"SELECT gen_random_uuid(), '{big}', lpad(n::text, 5, '0'), 'Bulk ' || n, 'Expense', false, 'Active', now(), '{system}', now(), '{system}' FROM generate_series(1, {OrganisationRoutes.ListLimit + 1}) n",
            Ct);
        CommandReply limited = await _host.GetAsync(ChartAccounts(big), cancellationToken: Ct);
        Assert.Equal(OrganisationRoutes.ListLimit, limited.Json!.AsArray().Count);
        Assert.Equal("00001", limited.Json.AsArray()[0]!["accountNumber"]!.GetValue<string>());
    }

    [Fact]
    public async Task R_SEC_01_RR_52_each_account_command_and_the_query_is_403_without_the_permission_and_with_only_a_site_limited_assignment()
    {
        string siteLimited = await _users.AddAdministratorAsync(Se01, Milestone1OrganisationFixture.SiteId, Ct);
        (string Path, object? Body, string? IfMatch)[] commands =
        [
            (Accounts, Create("6951"), null),
            (Update(TestAdj), new { name = "x", accountType = "Expense", isControl = false }, "\"1\""),
            (Lifecycle(TestAdj, "block"), new { reason = "x" }, "\"1\""),
            (Lifecycle(TestAdj, "unblock"), null, "\"1\""),
            (Lifecycle(TestAdj, "deactivate"), null, "\"1\""),
            (Lifecycle(TestAdj, "reactivate"), null, "\"1\""),
        ];

        int key = 0;
        foreach ((string path, object? body, string? ifMatch) in commands)
        {
            CommandReply clerk = await _host.PostAsync(path, body, $"sec-clerk-{key}", ifMatch: ifMatch, subject: TestUsers.ClerkSubject, cancellationToken: Ct);
            CommandReply limited = await _host.PostAsync(path, body, $"sec-limited-{key++}", ifMatch: ifMatch, subject: siteLimited, cancellationToken: Ct);
            Assert.True(clerk.Status == 403 && clerk.Code == "permission_missing", $"{path}: {clerk.Body}");
            Assert.True(limited.Status == 403 && limited.Code == "out_of_scope", $"{path}: {limited.Body}");
        }

        foreach (string read in new[] { $"{Accounts}/{TestAdj}", ChartAccounts(Bas) })
        {
            CommandReply clerk = await _host.GetAsync(read, subject: TestUsers.ClerkSubject, cancellationToken: Ct);
            CommandReply limited = await _host.GetAsync(read, subject: siteLimited, cancellationToken: Ct);
            Assert.True(clerk.Status == 403 && clerk.Code == "permission_missing", $"{read}: {clerk.Body}");
            Assert.True(limited.Status == 403 && limited.Code == "out_of_scope", $"{read}: {limited.Body}");
        }

        Assert.Equal(0L, await _host.CountAsync("kernel.idempotency_record", Ct));
        Assert.Equal(("Active", 1L), await StatusAsync(TestAdj));
    }

    [Theory]
    [InlineData("update")]
    [InlineData("block")]
    [InlineData("unblock")]
    [InlineData("deactivate")]
    [InlineData("reactivate")]
    public async Task R_API_01_R_API_02_R_API_04_UPDATE_and_every_lifecycle_command_needs_If_Match_428_a_stale_one_is_412_a_replay_returns_the_first_result_and_a_concurrent_duplicate_is_409(string verb)
    {
        (Guid account, _) = await CreateAccountAsync(_host, "694" + verb.Length, cancellationToken: Ct);
        // Bring the account to the state the verb starts from: UNBLOCK from Blocked, REACTIVATE from Inactive.
        long version = 1;
        if (verb == "unblock")
        {
            Assert.Equal(200, (await _host.PostAsync(Lifecycle(account, "block"), new { reason = "Setup" }, $"setup-{verb}", ifMatch: "\"1\"", cancellationToken: Ct)).Status);
            version = 2;
        }
        else if (verb == "reactivate")
        {
            Assert.Equal(200, (await _host.PostAsync(Lifecycle(account, "deactivate"), null, $"setup-{verb}", ifMatch: "\"1\"", cancellationToken: Ct)).Status);
            version = 2;
        }

        string path = verb == "update" ? Update(account) : Lifecycle(account, verb);
        object? body = verb switch
        {
            "update" => new { name = "Renamed", accountType = "Expense", isControl = false },
            "block" => new { reason = "Retired" },
            _ => null,
        };

        CommandReply missing = await _host.PostAsync(path, body, $"{verb}-428", cancellationToken: Ct);
        CommandReply stale = await _host.PostAsync(path, body, $"{verb}-412", ifMatch: $"\"{version + 5}\"", cancellationToken: Ct);

        // R-API-01: while another request holds the key, a duplicate is 409 at once and nothing is stored.
        long lockKey = AdvisoryLockKey.For(new UserId(TestUsers.OrganisationAdministrator), IdempotencyKey.From($"{verb}-ok"));
        CommandReply duplicate;
        await using (NpgsqlConnection holder = await PostgresFixture.OpenAsync(_db.ConnectionStringFor("themis_app"), Ct))
        await using (NpgsqlTransaction running = await holder.BeginTransactionAsync(Ct))
        {
            await using (NpgsqlCommand take = new("SET LOCAL ROLE themis_kernel; SELECT pg_advisory_xact_lock(@key)", holder, running))
            {
                take.Parameters.AddWithValue("key", lockKey);
                await take.ExecuteNonQueryAsync(Ct);
            }

            duplicate = await _host.PostAsync(path, body, $"{verb}-ok", ifMatch: $"\"{version}\"", cancellationToken: Ct);
            await running.RollbackAsync(Ct);
        }

        CommandReply ok = await _host.PostAsync(path, body, $"{verb}-ok", ifMatch: $"\"{version}\"", cancellationToken: Ct);
        CommandReply replay = await _host.PostAsync(path, body, $"{verb}-ok", ifMatch: $"\"{version}\"", cancellationToken: Ct);

        Assert.Equal((428, "if_match_required"), (missing.Status, missing.Code));
        Assert.Equal((412, "R-API-02", "version_stale"), (stale.Status, stale.RuleId, stale.Code));
        Assert.Equal((409, "R-API-01", "idempotency_key_in_progress"), (duplicate.Status, duplicate.RuleId, duplicate.Code));
        Assert.True(ok.Status == 200, ok.Body);
        Assert.Equal($"\"{version + 1}\"", ok.ETag);
        Assert.True(replay.Replayed);
        Assert.Equal(ok.Body, replay.Body);
        Assert.Equal(version + 1, await _host.ScalarAsync("SELECT version FROM kernel.account WHERE id = @id", Ct, ("id", account)));
        // R-AUD-01, D-44: one audit event and one event for the one changed account.
        string command = "ACCOUNT." + verb.ToUpperInvariant();
        Assert.Equal(1L, await _host.ScalarAsync("SELECT count(*) FROM kernel.audit_event WHERE command_name = @command AND entity_id = @id", Ct, ("command", command), ("id", account)));
        Assert.Equal(1L, await _host.ScalarAsync("SELECT count(*) FROM kernel.outbox_event WHERE command_name = @command AND aggregate_id = @id AND company_id IS NULL", Ct, ("command", command), ("id", account)));
        Assert.Equal(0L, await _host.ScalarAsync("SELECT count(*) FROM kernel.idempotency_record WHERE key IN (@a, @b)", Ct, ("a", $"{verb}-428"), ("b", $"{verb}-412")));
    }

    [Fact]
    public async Task R_SEC_02_D_70_the_conflict_pairs_in_the_database_equal_the_catalogue_in_code_after_the_migration()
    {
        string pairs = (string)(await _host.ScalarAsync("SELECT string_agg(permission_a || ' ' || permission_b, ',' ORDER BY permission_a, permission_b) FROM kernel.sod_conflict", Ct))!;

        Assert.Equal(string.Join(",", PermissionCatalog.Definition.Conflicts.Select(pair => $"{pair.PermissionA} {pair.PermissionB}").Order(StringComparer.Ordinal)), pairs);
        Assert.DoesNotContain("ACCOUNT.MAINTAIN", pairs, StringComparison.Ordinal);
    }

    private async Task PostCountDifferenceAsync(string quantity, string cost)
    {
        CommandReply item = await _host.PostAsync("/v1/kernel/items", new { companyId = Se01, itemNumber = "ACC-ITEM", description = "Account test item", itemType = "Stocked", baseUnit = "H87", lotTracking = "None", itemPostingGroup = "TRADE_GOODS", taxClass = "Standard", costingMethod = "MovingAverage" }, "acc-item", subject: TestUsers.ClerkSubject, cancellationToken: Ct);
        Assert.True(item.Status == 200, item.Body);
        Guid itemId = item.Json!["id"]!.GetValue<Guid>();
        CommandReply draft = await _host.PostAsync("/v1/kernel/inventory-adjustments", new { companyId = Se01, documentDate = "2026-06-15", reasonCode = "CountDifference", warehouseId = Milestone1OrganisationFixture.WarehouseId, postingDate = "2026-06-15", lines = new[] { new { lineNumber = 1, itemId, quantity, warehouseId = Milestone1OrganisationFixture.WarehouseId, stockStatus = "Available", enteredUnitCost = cost } } }, "acc-draft", subject: TestUsers.ClerkSubject, cancellationToken: Ct);
        Assert.True(draft.Status == 200, draft.Body);
        CommandReply posted = await _host.PostAsync($"/v1/kernel/inventory-adjustments/{draft.Json!["id"]!.GetValue<Guid>()}/post", null, "acc-post", ifMatch: "\"1\"", subject: TestUsers.ClerkSubject, cancellationToken: Ct);
        Assert.True(posted.Status == 200, posted.Body);
    }

    private async Task<(string Status, long Version)> StatusAsync(Guid account)
    {
        string json = (string)(await _host.ScalarAsync("SELECT status || ' ' || version FROM kernel.account WHERE id = @id", Ct, ("id", account)))!;
        string[] parts = json.Split(' ');
        return (parts[0], long.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture));
    }

    private Task<(string Type, string Aggregate, Guid Id, Guid? Company, string Command)> EnvelopeAsync(string eventType) => OrganisationBodies.EnvelopeAsync(_host, eventType, Ct);
}

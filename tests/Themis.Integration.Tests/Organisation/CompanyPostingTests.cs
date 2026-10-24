using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Themis.Integration.Tests.Pipeline;
using Themis.Kernel.Organisation;

namespace Themis.Integration.Tests.Organisation;

/// <summary>
/// COMPANY.UPDATE against the posting service (milestone 2 step 3 brief, items 4, 9 and 10; D-65, RR-74): the first
/// posting fixes base currency, time zone, country, organisation number and rounding mode and leaves the VAT rounding
/// level alone (R-MD-06); a posting reads the company's base currency, minor units and rounding mode under FOR SHARE
/// (lock class 1b), so in either order the two commands serialise without deadlock: a posting first makes the update
/// wait and then rejects it, an update first makes the posting wait and then meet the new currency (R-DOC-01) or the
/// new rounding mode (RR-74). Costing rounds half-way amounts in the company's mode.
/// </summary>
[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Integration")]
public sealed class CompanyPostingTests : IAsyncLifetime
{
    private const string Items = "/v1/kernel/items";
    private const string Adjustments = "/v1/kernel/inventory-adjustments";
    private const string June = "2026-06-15";

    private static readonly Guid Se01 = Milestone1OrganisationFixture.CompanyId;
    private static readonly Guid Warehouse = Milestone1OrganisationFixture.WarehouseId;

    private readonly PostgresFixture _db;
    private PipelineTestHost _host = null!;

    public CompanyPostingTests(PostgresFixture db)
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
        _host.CompanyUpdateSeam.AfterVersionedUpdate = null;
        await _host.DisposeAsync();
    }

    [Theory]
    [InlineData("baseCurrency", "EUR", 422, "company_base_currency_fixed")]
    [InlineData("timeZone", "Europe/Helsinki", 422, "company_time_zone_fixed")]
    [InlineData("countryCode", "FI", 422, "company_country_code_fixed")]
    [InlineData("organisationNumber", "555555-5556", 422, "company_organisation_number_fixed")]
    [InlineData("roundingMode", "HalfEven", 422, "company_rounding_mode_fixed")]
    [InlineData("taxRoundingLevel", "Document", 200, null)]
    public async Task R_MD_06_D_65_after_the_companys_first_posting_the_base_currency_time_zone_country_organisation_number_and_rounding_mode_are_fixed_and_the_VAT_rounding_level_still_changes(string attribute, string value, int status, string? code)
    {
        Guid item = await CreateItemAsync("CP-FIXED");
        (_, CommandReply posted) = await PostNewAsync("cp-fixed", [Line(1, item, "10", enteredUnitCost: "1.00")]);
        Assert.True(posted.Status == 200, posted.Body);

        CommandReply reply = await _host.PostAsync(OrganisationBodies.CompanyUpdate(Se01), Se01With(attribute, value), $"cp-fixed-{attribute}", ifMatch: "\"1\"", cancellationToken: Ct);
        CommandReply replay = await _host.PostAsync(OrganisationBodies.CompanyUpdate(Se01), Se01With(attribute, value), $"cp-fixed-{attribute}", ifMatch: "\"1\"", cancellationToken: Ct);

        Assert.True(reply.Status == status, reply.Body);
        Assert.Equal(status, replay.Status);
        Assert.True(replay.Replayed);
        if (code is null)
        {
            Assert.Equal("Document", reply.Json!["taxRoundingLevel"]!.GetValue<string>());
            Assert.Equal(2L, await _host.ScalarAsync("SELECT version FROM kernel.company WHERE id = @id", Ct, ("id", Se01)));
        }
        else
        {
            Assert.Equal(("R-MD-06", code), (reply.RuleId, reply.Code));
            Assert.Equal(1L, await _host.ScalarAsync("SELECT version FROM kernel.company WHERE id = @id", Ct, ("id", Se01)));
            Assert.Equal(1L, await _host.ScalarAsync("SELECT version FROM kernel.party WHERE id = @id", Ct, ("id", Milestone1OrganisationFixture.CompanyPartyId)));
        }
    }

    [Fact]
    public async Task R_MD_06_D_65_the_first_posting_alone_fixes_the_chart_of_accounts_when_the_company_has_no_posting_profile_rule_left()
    {
        CommandReply chart = await _host.PostAsync(OrganisationBodies.Charts, new { code = "K3", name = "K3-kontoplan" }, "chart-k3", cancellationToken: Ct);
        Guid k3 = chart.Json!["id"]!.GetValue<Guid>();
        Guid item = await CreateItemAsync("CP-CHART");
        // A zero-cost adjustment posts its inventory entries and no journal entry (I-08), so no journal line references a rule.
        (_, CommandReply posted) = await PostNewAsync("cp-chart", [Line(1, item, "10", enteredUnitCost: "0")]);
        Assert.True(posted.Status == 200, posted.Body);
        Assert.Equal(1L, await _host.CountAsync("kernel.inventory_entry", Ct));
        Assert.Equal(0L, await _host.CountAsync("kernel.journal_entry", Ct));

        // Test data: the fixture's posting profile rules are the other trigger of the chart (proven in CompanyCommandTests); they are
        // taken away after the posting and put back as they were, so only the posting can decide here.
        string rules = (string)(await _host.ScalarAsync("SELECT jsonb_agg(to_jsonb(r))::text FROM kernel.posting_profile_rule r WHERE company_id = @company", Ct, ("company", Se01)))!;
        // The stored columns only: a generated column (expects_control) cannot be written back.
        string columns = (string)(await _host.ScalarAsync("SELECT string_agg(column_name, ', ' ORDER BY ordinal_position) FROM information_schema.columns WHERE table_schema = 'kernel' AND table_name = 'posting_profile_rule' AND is_generated = 'NEVER'", Ct))!;
        await _host.ScalarAsync("DELETE FROM kernel.posting_profile_rule WHERE company_id = @company", Ct, ("company", Se01));
        try
        {
            CommandReply blocked = await _host.PostAsync(OrganisationBodies.CompanyUpdate(Se01), OrganisationBodies.Se01Update(chartOfAccountsId: k3), "cp-chart-update", ifMatch: "\"1\"", cancellationToken: Ct);
            Assert.Equal(422, blocked.Status);
            Assert.Equal(("R-MD-06", "company_chart_of_accounts_fixed"), (blocked.RuleId, blocked.Code));
            Assert.Equal(0L, await _host.ScalarAsync("SELECT count(*) FROM kernel.posting_profile_rule WHERE company_id = @company", Ct, ("company", Se01)));
            Assert.Equal(Milestone1OrganisationFixture.ChartOfAccountsId, await _host.ScalarAsync("SELECT chart_of_accounts_id FROM kernel.company WHERE id = @id", Ct, ("id", Se01)));
        }
        finally
        {
            await _host.ScalarAsync($"INSERT INTO kernel.posting_profile_rule ({columns}) SELECT {columns} FROM jsonb_populate_recordset(NULL::kernel.posting_profile_rule, @rows::jsonb)", CancellationToken.None, ("rows", rules));
        }

        Assert.Equal(2L, await _host.ScalarAsync("SELECT count(*) FROM kernel.posting_profile_rule WHERE company_id = @company", Ct, ("company", Se01)));
    }

    [Fact]
    public async Task R_MD_06_D_65_one_error_per_fixed_attribute_when_several_change_at_once_and_the_legal_name_still_changes_after_a_posting()
    {
        Guid item = await CreateItemAsync("CP-MANY");
        (_, CommandReply posted) = await PostNewAsync("cp-many", [Line(1, item, "10", enteredUnitCost: "1.00")]);
        Assert.True(posted.Status == 200, posted.Body);

        CommandReply blocked = await _host.PostAsync(OrganisationBodies.CompanyUpdate(Se01), OrganisationBodies.Se01Update(baseCurrency: "EUR", roundingMode: "HalfEven", timeZone: "Europe/Helsinki"), "cp-many-update", ifMatch: "\"1\"", cancellationToken: Ct);
        CommandReply renamed = await _host.PostAsync(OrganisationBodies.CompanyUpdate(Se01), OrganisationBodies.Se01Update(legalName: "Themis Fictitious Fasteners Holding AB"), "cp-many-rename", ifMatch: "\"1\"", cancellationToken: Ct);

        Assert.Equal(422, blocked.Status);
        Assert.Equal("R-MD-06", blocked.RuleId);
        Assert.Equal(["company_base_currency_fixed", "company_rounding_mode_fixed", "company_time_zone_fixed"], blocked.Json!["errors"]!.AsArray().Select(error => error!["code"]!.GetValue<string>()).Order());
        Assert.True(renamed.Status == 200, renamed.Body);
        Assert.Equal(2, renamed.Json!["version"]!.GetValue<long>());
    }

    [Theory]
    [InlineData("baseCurrency", "EUR", "company_base_currency_fixed")]
    [InlineData("roundingMode", "HalfEven", "company_rounding_mode_fixed")]
    public async Task D_65_a_posting_holding_the_company_row_FOR_SHARE_makes_a_concurrent_COMPANY_UPDATE_wait_and_the_update_is_422_R_MD_06_once_the_posting_committed_without_deadlock_or_retry(string attribute, string value, string code)
    {
        Guid item = await CreateItemAsync("CP-A");
        Guid draft = await DraftAsync("cp-a-create", [Line(1, item, "10", enteredUnitCost: "1.00")]);
        TaskCompletionSource paused = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Failures.AfterLedgerWrites = async (_, _, cancellationToken) =>
        {
            paused.TrySetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
        };

        Task<CommandReply> posting = PostAsync(draft, "cp-a-post");
        await paused.Task.WaitAsync(TimeSpan.FromSeconds(15), Ct);
        // Class 1b is held: the versioned UPDATE of class 1 waits on the row instead of failing or deadlocking (R-LED-08).
        Task<CommandReply> update = _host.PostAsync(OrganisationBodies.CompanyUpdate(Se01), Se01With(attribute, value), "cp-a-update", ifMatch: "\"1\"", cancellationToken: Ct);
        await WaitForLockWaiterAsync();
        Assert.False(update.IsCompleted);
        release.SetResult();
        CommandReply postedReply = await posting;
        CommandReply updated = await update;

        Assert.True(postedReply.Status == 200, postedReply.Body);
        Assert.Equal(422, updated.Status);
        Assert.Equal(("R-MD-06", code), (updated.RuleId, updated.Code));
        Assert.Equal(1L, await _host.ScalarAsync("SELECT version FROM kernel.company WHERE id = @id", Ct, ("id", Se01)));
        Assert.Equal("SEK", await _host.ScalarAsync("SELECT base_currency FROM kernel.company WHERE id = @id", Ct, ("id", Se01)));
        Assert.Equal("HalfAwayFromZero", await _host.ScalarAsync("SELECT rounding_mode FROM kernel.company WHERE id = @id", Ct, ("id", Se01)));
        Assert.Equal(0L, _host.Retries);
        Assert.Empty(_host.RetriesBySqlState);
        Assert.Equal("Posted", await _host.ScalarAsync("SELECT status FROM kernel.document WHERE id = @id", Ct, ("id", draft)));
    }

    [Theory]
    [InlineData("baseCurrency", "EUR")]
    [InlineData("roundingMode", "HalfEven")]
    public async Task D_65_a_COMPANY_UPDATE_holding_the_company_row_makes_a_concurrent_posting_wait_and_the_posting_then_meets_the_new_currency_R_DOC_01_or_posts_in_the_new_rounding_mode_RR_74(string attribute, string value)
    {
        Guid item = await CreateItemAsync("CP-B");
        // 1 x 2.125: 2.13 half away from zero, 2.12 half even (R-MON-02, RR-74).
        Guid draft = await DraftAsync("cp-b-create", [Line(1, item, "1", enteredUnitCost: "2.125")]);
        TaskCompletionSource paused = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _host.CompanyUpdateSeam.AfterVersionedUpdate = async (_, cancellationToken) =>
        {
            paused.TrySetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
        };

        Task<CommandReply> update = _host.PostAsync(OrganisationBodies.CompanyUpdate(Se01), Se01With(attribute, value), "cp-b-update", ifMatch: "\"1\"", cancellationToken: Ct);
        await paused.Task.WaitAsync(TimeSpan.FromSeconds(15), Ct);
        // The posting's FOR SHARE of class 1b waits behind the update's row lock and reads the committed row afterwards.
        Task<CommandReply> posting = PostAsync(draft, "cp-b-post");
        await WaitForLockWaiterAsync();
        Assert.False(posting.IsCompleted);
        release.SetResult();
        CommandReply updated = await update;
        CommandReply postedReply = await posting;

        Assert.True(updated.Status == 200, updated.Body);
        Assert.Equal(2, updated.Json!["version"]!.GetValue<long>());
        Assert.Equal(0L, _host.Retries);
        if (attribute == "baseCurrency")
        {
            // R-DOC-01: the draft's currency is no longer the company's base currency; nothing is posted and the rejection is stored.
            Assert.True(postedReply.Status == 422, postedReply.Body);
            Assert.Equal(("R-DOC-01", "document_currency_not_base_currency"), (postedReply.RuleId, postedReply.Code));
            Assert.Equal("Draft", await _host.ScalarAsync("SELECT status FROM kernel.document WHERE id = @id", Ct, ("id", draft)));
            Assert.Equal(0L, await _host.CountAsync("kernel.inventory_entry", Ct));
            Assert.Equal(0L, await _host.CountAsync("kernel.journal_entry", Ct));
            CommandReply replay = await PostAsync(draft, "cp-b-post");
            Assert.Equal(422, replay.Status);
            Assert.True(replay.Replayed);
        }
        else
        {
            // RR-74: the posting that waited costs in the mode the update committed.
            Assert.True(postedReply.Status == 200, postedReply.Body);
            Assert.Equal(2.12m, await _host.ScalarAsync("SELECT cost_amount FROM kernel.document_line WHERE document_id = @id", Ct, ("id", draft)));
            Assert.Equal(2.12m, await _host.ScalarAsync("SELECT inventory_value FROM kernel.item_cost_state WHERE item_id = @item", Ct, ("item", item)));
            Assert.Equal(2.12m, await _host.ScalarAsync("SELECT max(abs(amount)) FROM kernel.journal_line", Ct));
        }
    }

    [Fact]
    public async Task R_DOC_01_a_draft_in_the_former_base_currency_is_rejected_on_POST_with_document_currency_not_base_currency_and_a_fresh_draft_posts_in_the_new_one()
    {
        Guid item = await CreateItemAsync("CP-CUR");
        Guid stale = await DraftAsync("cp-cur-stale-create", [Line(1, item, "10", enteredUnitCost: "1.00")]);
        CommandReply updated = await _host.PostAsync(OrganisationBodies.CompanyUpdate(Se01), OrganisationBodies.Se01Update(baseCurrency: "EUR"), "cp-cur-update", ifMatch: "\"1\"", cancellationToken: Ct);
        Assert.True(updated.Status == 200, updated.Body);

        CommandReply rejected = await PostAsync(stale, "cp-cur-post");
        Assert.Equal(422, rejected.Status);
        Assert.Equal(("R-DOC-01", "document_currency_not_base_currency"), (rejected.RuleId, rejected.Code));
        Assert.Equal("SEK", await _host.ScalarAsync("SELECT currency FROM kernel.document WHERE id = @id", Ct, ("id", stale)));
        Assert.Equal("Draft", await _host.ScalarAsync("SELECT status FROM kernel.document WHERE id = @id", Ct, ("id", stale)));

        // A draft created after the change carries the new base currency and posts; the currency is then fixed (R-MD-06).
        (Guid fresh, CommandReply posted) = await PostNewAsync("cp-cur-fresh", [Line(1, item, "10", enteredUnitCost: "1.00")]);
        Assert.True(posted.Status == 200, posted.Body);
        Assert.Equal("EUR", await _host.ScalarAsync("SELECT currency FROM kernel.document WHERE id = @id", Ct, ("id", fresh)));
        Assert.Equal("EUR", await _host.ScalarAsync("SELECT DISTINCT txn_currency FROM kernel.journal_line", Ct));
        CommandReply back = await _host.PostAsync(OrganisationBodies.CompanyUpdate(Se01), OrganisationBodies.Se01Update(), "cp-cur-back", ifMatch: "\"2\"", cancellationToken: Ct);
        Assert.Equal(422, back.Status);
        Assert.Equal("company_base_currency_fixed", back.Code);
    }

    [Fact]
    public async Task RR_74_a_company_in_HalfEven_costs_a_half_way_amount_to_the_even_cent_and_the_same_lines_in_HalfAwayFromZero_round_away()
    {
        CommandReply halfEven = await _host.PostAsync(OrganisationBodies.CompanyUpdate(Se01), OrganisationBodies.Se01Update(roundingMode: "HalfEven"), "cp-mode-even", ifMatch: "\"1\"", cancellationToken: Ct);
        Assert.True(halfEven.Status == 200, halfEven.Body);
        Guid item = await CreateItemAsync("CP-EVEN");
        (Guid even, CommandReply posted) = await PostNewAsync("cp-even", [Line(1, item, "1", enteredUnitCost: "2.125"), Line(2, item, "1", enteredUnitCost: "2.135")]);
        Assert.True(posted.Status == 200, posted.Body);

        // 2.125 -> 2.12 and 2.135 -> 2.14 (to even); half away from zero would give 2.13 and 2.14.
        Assert.Equal([2.12m, 2.14m], await LineCostsAsync(even));
        Assert.Equal(4.26m, await _host.ScalarAsync("SELECT inventory_value FROM kernel.item_cost_state WHERE item_id = @item", Ct, ("item", item)));
        // The mode is fixed from this posting on (D-65).
        CommandReply back = await _host.PostAsync(OrganisationBodies.CompanyUpdate(Se01), OrganisationBodies.Se01Update(), "cp-mode-back", ifMatch: "\"2\"", cancellationToken: Ct);
        Assert.Equal(("R-MD-06", "company_rounding_mode_fixed"), (back.RuleId, back.Code));
    }

    // ---- helpers --------------------------------------------------------------------------------------------------

    private static object Se01With(string attribute, string value) => attribute switch
    {
        "baseCurrency" => OrganisationBodies.Se01Update(baseCurrency: value),
        "timeZone" => OrganisationBodies.Se01Update(timeZone: value),
        "countryCode" => OrganisationBodies.Se01Update(countryCode: value),
        "organisationNumber" => OrganisationBodies.Se01Update(organisationNumber: value),
        "roundingMode" => OrganisationBodies.Se01Update(roundingMode: value),
        "taxRoundingLevel" => OrganisationBodies.Se01Update(taxRoundingLevel: value),
        _ => throw new ArgumentOutOfRangeException(nameof(attribute), attribute, null),
    };

    /// <summary>Waits until a backend of the test database waits on a row lock, so the second command is proven to queue behind the first.</summary>
    private async Task WaitForLockWaiterAsync()
    {
        for (int attempt = 0; attempt < 600; attempt++)
        {
            if ((long)(await _host.ScalarAsync("SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock'", Ct))! > 0)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25), Ct);
        }

        Assert.Fail("No backend waited on a lock within 15 seconds.");
    }

    private async Task<Guid> CreateItemAsync(string number)
    {
        CommandReply reply = await _host.PostAsync(Items, new { companyId = Se01, itemNumber = number, description = $"Item {number}", itemType = "Stocked", baseUnit = "H87", lotTracking = "None", itemPostingGroup = "TRADE_GOODS", taxClass = "Standard", costingMethod = "MovingAverage" }, "item-" + number, cancellationToken: Ct);
        Assert.True(reply.Status == 200, reply.Body);
        return reply.Json!["id"]!.GetValue<Guid>();
    }

    private static object Line(int number, Guid item, string quantity, string? enteredUnitCost = null) =>
        new { lineNumber = number, itemId = item, quantity, warehouseId = Warehouse, stockStatus = "Available", enteredUnitCost };

    private async Task<Guid> DraftAsync(string key, object[] lines)
    {
        CommandReply reply = await _host.PostAsync(Adjustments, new { companyId = Se01, documentDate = June, reasonCode = "CountDifference", warehouseId = Warehouse, postingDate = June, lines }, key, cancellationToken: Ct);
        Assert.True(reply.Status == 200, reply.Body);
        return reply.Json!["id"]!.GetValue<Guid>();
    }

    private Task<CommandReply> PostAsync(Guid id, string key) =>
        _host.PostAsync($"{Adjustments}/{id}/post", null, key, ifMatch: "\"1\"", cancellationToken: Ct);

    private async Task<(Guid Id, CommandReply Reply)> PostNewAsync(string key, object[] lines)
    {
        Guid id = await DraftAsync($"{key}-create", lines);
        return (id, await PostAsync(id, key));
    }

    private async Task<IReadOnlyList<decimal>> LineCostsAsync(Guid document)
    {
        string costs = (string)(await _host.ScalarAsync("SELECT string_agg(cost_amount::text, ',' ORDER BY line_number) FROM kernel.document_line WHERE document_id = @id", Ct, ("id", document)))!;
        return [.. costs.Split(',').Select(cost => decimal.Parse(cost, CultureInfo.InvariantCulture))];
    }
}

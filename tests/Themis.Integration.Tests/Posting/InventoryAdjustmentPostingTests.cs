using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Themis.FixtureModule.Contracts;
using Themis.Integration.Tests.Identity;
using Themis.Integration.Tests.MasterData;
using Themis.Integration.Tests.Pipeline;
using Themis.Kernel.Contracts.Commands;
using Themis.Kernel.Documents;
using Themis.Kernel.Organisation;
using Themis.Kernel.Persistence;
using Themis.Kernel.Pipeline.Dispatch;
using Themis.Kernel.Posting;
using Themis.Kernel.Primitives;

namespace Themis.Integration.Tests.Posting;

/// <summary>
/// INVENTORY_ADJUSTMENT.POST end to end through the pipeline (step 8 brief): the 13 reference calculations of the
/// Build Brief with one balanced journal entry per step (E-01, E-02, R-INV-05, R-INV-07, R-MON-02), the guards with
/// their rules and codes (R-DOC-01, R-LED-04, R-INV-05, R-DOC-08, R-PP-02, R-INV-03), the transaction boundary
/// (R-LED-05, I-09, D-41), idempotency and versions (R-API-01, R-API-02, R-API-04), the lock order under concurrency
/// (R-LED-08), the audit and outbox convention of a posting (R-AUD-01, D-51, D-50) and the invariants I-01, I-02,
/// I-03, I-04, I-06 and I-08 after every posting. The fixture module's posting hook injects the failures.
/// </summary>
[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Integration")]
public sealed class InventoryAdjustmentPostingTests : IAsyncLifetime
{
    private const string Adjustments = "/v1/kernel/inventory-adjustments";
    private const string June = "2026-06-15";
    private const string InsufficientPrivilege = "42501";
    private const string CheckViolation = "23514";

    private static readonly Guid Company = Milestone1OrganisationFixture.CompanyId;
    private static readonly Guid Warehouse = Milestone1OrganisationFixture.WarehouseId;
    private static readonly Guid Inventory = Milestone1GeneralLedgerFixture.InventoryAccountId;
    private static readonly Guid Adjustment = Milestone1GeneralLedgerFixture.AdjustmentAccountId;
    private static readonly Guid FiscalYear2027 = Guid.Parse("00000000-0000-7000-8000-00000000f027");

    private readonly PostgresFixture _db;
    private PipelineTestHost _host = null!;
    private IdentityTestData? _users;
    private MasterDataTestData? _data;
    private int _itemSequence;

    public InventoryAdjustmentPostingTests(PostgresFixture db)
    {
        _db = db;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private PostingFailureSwitch Failures => _host.Services.GetRequiredService<PostingFailureSwitch>();

    public async ValueTask InitializeAsync()
    {
        await _db.TruncateAsync(Ct);
        _host = await PipelineTestHost.StartAsync(_db);
        try
        {
            _users = await IdentityTestData.CreateAsync(_db, Ct);
            _data = await MasterDataTestData.CreateAsync(_db, Ct);
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        Failures.Failure = FixtureFailure.None;
        await _db.TruncateAsync(CancellationToken.None);
        await _db.ExecuteAsMigratorAsync($"DELETE FROM kernel.fiscal_period WHERE fiscal_year_id = '{FiscalYear2027}'; DELETE FROM kernel.fiscal_year WHERE id = '{FiscalYear2027}'", CancellationToken.None);
        if (_data is not null)
        {
            await _data.DisposeAsync();
        }

        if (_users is not null)
        {
            await _users.DisposeAsync();
        }

        await _host.DisposeAsync();
    }

    // ---- the reference calculations -------------------------------------------------------------------------------

    private sealed record Step(int Number, string Item, string Quantity, string? EnteredCost, decimal? LineCost, decimal QuantityAfter, decimal ValueAfter, decimal? AverageAfter);

    private static readonly Step[] ReferenceSteps =
    [
        new(1, "TEST-001", "100", "2.00", 200.00m, 100m, 200.00m, 2.00m),
        new(2, "TEST-001", "100", "3.00", 300.00m, 200m, 500.00m, 2.50m),
        new(3, "TEST-001", "-50", null, -125.00m, 150m, 375.00m, 2.50m),
        new(4, "TEST-001", "-150", null, -375.00m, 0m, 0m, 2.50m),
        new(5, "TEST-001", "-1", null, null, 0m, 0m, 2.50m),
        new(6, "TEST-002", "3", "1.00", 3.00m, 3m, 3.00m, 1.00m),
        new(7, "TEST-002", "3", "1.01", 3.03m, 6m, 6.03m, 1.005m),
        new(8, "TEST-002", "-1", null, -1.01m, 5m, 5.02m, 1.004m),
        new(9, "TEST-002", "-5", null, -5.02m, 0m, 0m, 1.004m),
        new(10, "TEST-003", "100000000", "0.01", 1_000_000.00m, 100_000_000m, 1_000_000.00m, 0.01m),
        new(11, "TEST-003", "200000000", "0.02", 4_000_000.00m, 300_000_000m, 5_000_000.00m, 0.0166666667m),
        new(12, "TEST-003", "-150000000", null, -2_500_000.00m, 150_000_000m, 2_500_000.00m, 0.0166666667m),
        new(13, "TEST-003", "150000000", null, 2_500_000.00m, 300_000_000m, 5_000_000.00m, 0.0166666667m),
    ];

    [Fact]
    public async Task R_INV_05_the_13_reference_calculations_match_to_the_last_decimal_and_every_posted_step_writes_one_balanced_journal_entry_per_E_01_and_E_02()
    {
        Dictionary<string, Guid> items = new(StringComparer.Ordinal);
        foreach (string number in new[] { "TEST-001", "TEST-002", "TEST-003" })
        {
            items[number] = await CreateItemAsync(number);
        }

        foreach (Step step in ReferenceSteps)
        {
            Guid item = items[step.Item];
            long entriesBefore = await _host.CountAsync("kernel.journal_entry", Ct);
            (Guid id, CommandReply reply) = await PostNewAsync($"ref-{step.Number}", [Line(1, item, step.Quantity, enteredUnitCost: step.EnteredCost)]);

            if (step.LineCost is not { } lineCost)
            {
                // Step 5: R-INV-03, the balance key would go below 0; nothing is written and the rejection is the key's result.
                Assert.Equal(422, reply.Status);
                Assert.Equal("R-INV-03", reply.RuleId);
                Assert.Equal(InventoryPostingService.StockInsufficient, reply.Code);
                Assert.Equal("1", reply.Json!["errors"]![0]!["details"]!["lineNumber"]!.GetValue<string>());
                await AssertNothingPostedAsync(id, $"ref-{step.Number}", stored: true);
            }
            else
            {
                Assert.True(reply.Status == 200, $"Step {step.Number}: {reply.Body}");
                JsonNode line = reply.Json!["lines"]![0]!;
                Assert.Equal(lineCost, Decimal(line["costAmount"]));
                Assert.Equal(DecimalRounding.Round(lineCost / decimal.Parse(step.Quantity, CultureInfo.InvariantCulture), 10), Decimal(line["unitCost"]));
                // D-37: costs travel as strings, the version as a number.
                Assert.Equal(JsonValueKind.String, line["costAmount"]!.GetValueKind());
                Assert.Equal(JsonValueKind.Number, reply.Json["version"]!.GetValueKind());
                Assert.Equal("Posted", reply.Json["status"]!.GetValue<string>());
                Assert.StartsWith("IA-", reply.Json["documentNumber"]!.GetValue<string>(), StringComparison.Ordinal);

                // E-01 (inbound: debit INVENTORY, credit INVENTORY_ADJUSTMENT) and E-02 (outbound: the reverse), the debit line first.
                Assert.Equal(entriesBefore + 1, await _host.CountAsync("kernel.journal_entry", Ct));
                IReadOnlyList<JournalLine> journal = await JournalLinesAsync(id);
                Assert.Equal(2, journal.Count);
                (Guid debit, Guid credit) = lineCost > 0 ? (Inventory, Adjustment) : (Adjustment, Inventory);
                Assert.Equal((1, debit, Math.Abs(lineCost)), (journal[0].LineNumber, journal[0].AccountId, journal[0].Amount));
                Assert.Equal((2, credit, -Math.Abs(lineCost)), (journal[1].LineNumber, journal[1].AccountId, journal[1].Amount));
                Assert.All(journal, journalLine => Assert.Equal((item, Warehouse, "SEK"), (journalLine.ItemId, journalLine.WarehouseId, journalLine.TxnCurrency)));
            }

            // The cache after the step: quantity, value and the informational average (step 6 of the algorithm).
            (decimal quantity, decimal value, decimal? average) = await CostStateAsync(item);
            Assert.Equal((step.QuantityAfter, step.ValueAfter, step.AverageAfter), (quantity, value, average));
            await AssertInvariantsAsync();
        }
    }

    // ---- the transaction boundary ---------------------------------------------------------------------------------

    [Fact]
    public async Task R_LED_05_a_failure_injected_after_the_inventory_entries_leaves_nothing_behind_and_I_09_the_journal_entry_number_is_taken_by_the_next_posting()
    {
        Guid item = await CreateItemAsync("P-FAIL");
        long next = await NextJournalNumberAsync();
        (Guid first, CommandReply posted) = await PostNewAsync("fail-first", [Line(1, item, "10", enteredUnitCost: "1.00")]);
        Assert.Equal(200, posted.Status);
        Guid second = await DraftAsync("fail-second-create", [Line(1, item, "5", enteredUnitCost: "2.00")]);

        Failures.Failure = FixtureFailure.ThrowAfterWrite;
        CommandReply failed;
        try
        {
            failed = await PostAsync(second, "fail-second");
        }
        finally
        {
            Failures.Failure = FixtureFailure.None;
        }

        Assert.Equal(500, failed.Status);
        Assert.Equal("application/problem+json", failed.MediaType);
        await AssertNothingPostedAsync(second, "fail-second", stored: false);
        Assert.Equal((10m, 10.00m, 1.00m), await CostStateAsync(item));
        Assert.Equal(next + 1, await NextJournalNumberAsync());

        // The client's retry runs the command afresh and takes the number the failed attempt gave back (R-DOC-07, I-09).
        CommandReply retry = await PostAsync(second, "fail-second");
        Assert.Equal(200, retry.Status);
        Assert.False(retry.Replayed);
        Assert.Equal(JournalNumber(next), await EntryNumberAsync(first));
        Assert.Equal(JournalNumber(next + 1), await EntryNumberAsync(second));
        Assert.Equal((15m, 20.00m, 1.3333333333m), await CostStateAsync(item));
        await AssertInvariantsAsync();
    }

    [Fact]
    public async Task D_41_a_serialization_failure_on_the_first_attempt_is_retried_once_and_the_adjustment_posts_exactly_once_with_one_outbox_event()
    {
        Guid item = await CreateItemAsync("P-RETRY");
        long next = await NextJournalNumberAsync();
        long retriesBefore = _host.Retries;
        Guid id = await DraftAsync("retry-create", [Line(1, item, "4", enteredUnitCost: "2.50")]);

        Failures.Failure = FixtureFailure.SerializationFailureOnFirstAttempt;
        CommandReply reply;
        try
        {
            reply = await PostAsync(id, "retry-post");
        }
        finally
        {
            Failures.Failure = FixtureFailure.None;
        }

        Assert.Equal(200, reply.Status);
        Assert.Equal(1L, _host.Retries - retriesBefore);
        Assert.Contains(_host.Logs.Lines, line => line.Level == LogLevel.Warning && line.Message.Contains("40001", StringComparison.Ordinal));
        Assert.Equal(1L, await _host.ScalarAsync("SELECT count(*) FROM kernel.journal_entry WHERE source_document_id = @id", Ct, ("id", id)));
        Assert.Equal(1L, await _host.ScalarAsync("SELECT count(*) FROM kernel.inventory_entry e JOIN kernel.document_line l ON l.id = e.source_line_id WHERE l.document_id = @id", Ct, ("id", id)));
        Assert.Equal(1L, await _host.ScalarAsync("SELECT count(*) FROM kernel.outbox_event WHERE aggregate_id = @id AND event_type = 'INVENTORY_ADJUSTED'", Ct, ("id", id)));
        Assert.Equal(1L, await _host.ScalarAsync("SELECT count(*) FROM kernel.audit_event WHERE entity_id = @id AND command_name = 'INVENTORY_ADJUSTMENT.POST'", Ct, ("id", id)));
        Assert.Equal(JournalNumber(next), await EntryNumberAsync(id));
        Assert.Equal((4m, 10.00m, 2.50m), await CostStateAsync(item));
        await AssertInvariantsAsync();
    }

    // ---- zero cost, the stored line warehouse, line order --------------------------------------------------------

    [Fact]
    public async Task I_08_an_adjustment_whose_lines_all_have_zero_cost_posts_its_inventory_entries_and_no_journal_entry_and_D_52_leaves_the_journal_series_alone()
    {
        Guid item = await CreateItemAsync("P-ZERO");
        long next = await NextJournalNumberAsync();
        // The second line names its own warehouse: POST uses the stored line warehouse, never the header's (Specification, DocumentLine.warehouse_id).
        (Guid id, CommandReply reply) = await PostNewAsync("zero", [Line(1, item, "10", enteredUnitCost: "0"), Line(2, item, "5", warehouse: _data!.SecondWarehouse, enteredUnitCost: "0.00")]);

        Assert.Equal(200, reply.Status);
        Assert.StartsWith("IA-", reply.Json!["documentNumber"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.All(reply.Json["lines"]!.AsArray(), line => Assert.Equal((0m, 0m), (Decimal(line!["costAmount"]), Decimal(line["unitCost"]))));
        Assert.Equal(0L, await _host.ScalarAsync("SELECT count(*) FROM kernel.journal_entry WHERE source_document_id = @id", Ct, ("id", id)));
        Assert.Equal(2L, await _host.ScalarAsync("SELECT count(*) FROM kernel.inventory_entry e JOIN kernel.document_line l ON l.id = e.source_line_id WHERE l.document_id = @id AND e.cost_amount = 0 AND e.journal_entry_id IS NULL AND e.movement_type = 'AdjustmentIn'", Ct, ("id", id)));
        Assert.Equal(10m, await OnHandAsync(item, Warehouse));
        Assert.Equal(5m, await OnHandAsync(item, _data.SecondWarehouse));
        Assert.Equal((15m, 0m, 0m), await CostStateAsync(item));
        Assert.Equal(next, await NextJournalNumberAsync());
        await AssertInvariantsAsync();
    }

    [Fact]
    public async Task R_INV_09_an_inbound_and_an_outbound_line_of_the_same_item_in_one_document_are_costed_in_line_order_and_R_INV_03_is_checked_per_line()
    {
        Guid ordered = await CreateItemAsync("P-ORDER");
        Guid reversed = await CreateItemAsync("P-REVERSED");

        (Guid id, CommandReply reply) = await PostNewAsync("order-ok", [Line(1, ordered, "10", enteredUnitCost: "2.00"), Line(2, ordered, "-4")]);
        (Guid rejectedId, CommandReply rejected) = await PostNewAsync("order-rejected", [Line(1, reversed, "-4"), Line(2, reversed, "10", enteredUnitCost: "2.00")]);

        Assert.Equal(200, reply.Status);
        JsonArray lines = reply.Json!["lines"]!.AsArray();
        Assert.Equal((20.00m, -8.00m), (Decimal(lines[0]!["costAmount"]), Decimal(lines[1]!["costAmount"])));
        Assert.Equal((6m, 12.00m, 2.00m), await CostStateAsync(ordered));
        Assert.Equal(4, (await JournalLinesAsync(id)).Count);
        Assert.Equal(422, rejected.Status);
        Assert.Equal("R-INV-03", rejected.RuleId);
        Assert.Equal("1", rejected.Json!["errors"]![0]!["details"]!["lineNumber"]!.GetValue<string>());
        await AssertNothingPostedAsync(rejectedId, "order-rejected", stored: true);
        await AssertInvariantsAsync();
    }

    // ---- the guards -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task R_PP_02_a_Scrap_adjustment_with_a_non_zero_cost_is_rejected_naming_the_role_the_keys_and_the_date_and_nothing_is_saved()
    {
        Guid item = await CreateItemAsync("P-SCRAP");

        (Guid id, CommandReply reply) = await PostNewAsync("scrap", [Line(1, item, "10", enteredUnitCost: "2.00")], reasonCode: "Scrap");

        Assert.Equal(422, reply.Status);
        Assert.Equal("R-PP-02", reply.RuleId);
        Assert.Equal(AccountResolver.RuleMissing, reply.Code);
        Assert.Equal("no account for INVENTORY_ADJUSTMENT, item group TRADE_GOODS, reason code Scrap, posting date 2026-06-15", reply.Json!["errors"]![0]!["message"]!.GetValue<string>());
        await AssertNothingPostedAsync(id, "scrap", stored: true);
        Assert.Equal(0L, await _host.ScalarAsync("SELECT count(*) FROM kernel.item_cost_state WHERE item_id = @item", Ct, ("item", item)));
    }

    [Fact]
    public async Task R_LED_04_a_posting_date_in_the_Closed_period_1_or_no_posting_date_at_all_is_rejected()
    {
        Guid item = await CreateItemAsync("P-PERIOD");

        (Guid closed, CommandReply closedReply) = await PostNewAsync("closed", [Line(1, item, "1", enteredUnitCost: "1.00")], postingDate: "2026-01-15");
        (Guid missing, CommandReply missingReply) = await PostNewAsync("undated", [Line(1, item, "1", enteredUnitCost: "1.00")], postingDate: null);

        Assert.Equal(422, closedReply.Status);
        Assert.Equal("R-LED-04", closedReply.RuleId);
        Assert.Equal(PostInventoryAdjustmentHandler.FiscalPeriodNotOpen, closedReply.Code);
        Assert.Equal(422, missingReply.Status);
        Assert.Equal("R-LED-04", missingReply.RuleId);
        Assert.Equal(PostInventoryAdjustmentHandler.PostingDateRequired, missingReply.Code);
        await AssertNothingPostedAsync(closed, "closed", stored: true);
        await AssertNothingPostedAsync(missing, "undated", stored: true);
    }

    [Fact]
    public async Task R_DOC_08_a_posting_in_fiscal_year_2027_without_a_JOURNAL_ENTRY_series_is_rejected_naming_the_type_and_the_year_and_D_52_a_zero_cost_posting_there_succeeds()
    {
        await SeedFiscalYear2027Async();
        Guid item = await CreateItemAsync("P-2027");

        (Guid costed, CommandReply rejected) = await PostNewAsync("y2027", [Line(1, item, "10", enteredUnitCost: "2.00")], postingDate: "2027-03-15");
        (Guid zero, CommandReply posted) = await PostNewAsync("y2027-zero", [Line(1, item, "10", enteredUnitCost: "0")], postingDate: "2027-03-15");

        Assert.Equal(422, rejected.Status);
        Assert.Equal("R-DOC-08", rejected.RuleId);
        Assert.Equal(NumberSeriesService.SeriesMissing, rejected.Code);
        string message = rejected.Json!["errors"]![0]!["message"]!.GetValue<string>();
        Assert.Contains("JOURNAL_ENTRY", message, StringComparison.Ordinal);
        Assert.Contains(FiscalYear2027.ToString(), message, StringComparison.OrdinalIgnoreCase);
        await AssertNothingPostedAsync(costed, "y2027", stored: true);
        // D-52: the journal entry series is needed only when a journal entry is written.
        Assert.Equal(200, posted.Status);
        Assert.StartsWith("IA-", posted.Json!["documentNumber"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal(0L, await _host.ScalarAsync("SELECT count(*) FROM kernel.journal_entry WHERE source_document_id = @id", Ct, ("id", zero)));
        Assert.Equal(10m, await OnHandAsync(item, Warehouse));
        await AssertInvariantsAsync();
    }

    [Fact]
    public async Task R_DOC_01_a_lot_tracked_item_no_Open_line_a_missing_reason_code_a_non_Stocked_item_and_a_Blocked_item_are_rejected_with_their_own_codes()
    {
        Guid lotTracked = await CreateItemAsync("P-LOT", lotTracking: "Required");
        Guid service = await CreateItemAsync("P-SERVICE", itemType: "Service");
        Guid blocked = await CreateItemAsync("P-BLOCKED");
        Guid plain = await CreateItemAsync("P-PLAIN");
        await _host.ScalarAsync("UPDATE kernel.item_company SET status = 'Blocked' WHERE item_id = @item", Ct, ("item", blocked));

        (Guid lot, CommandReply lotReply) = await PostNewAsync("guard-lot", [Line(1, lotTracked, "1", enteredUnitCost: "1.00")]);
        (Guid empty, CommandReply emptyReply) = await PostNewAsync("guard-empty", []);
        (Guid noReason, CommandReply noReasonReply) = await PostNewAsync("guard-reason", [Line(1, plain, "1", enteredUnitCost: "1.00")], reasonCode: null);
        (Guid notStocked, CommandReply notStockedReply) = await PostNewAsync("guard-service", [Line(1, service, "1", enteredUnitCost: "1.00")]);
        (Guid notActive, CommandReply notActiveReply) = await PostNewAsync("guard-blocked", [Line(1, blocked, "1", enteredUnitCost: "1.00")]);

        foreach ((Guid id, string key, CommandReply reply, string code) in new[]
                 {
                     (lot, "guard-lot", lotReply, PostInventoryAdjustmentHandler.LotRequired),
                     (empty, "guard-empty", emptyReply, PostInventoryAdjustmentHandler.NoOpenLines),
                     (noReason, "guard-reason", noReasonReply, PostInventoryAdjustmentHandler.ReasonCodeRequired),
                     (notStocked, "guard-service", notStockedReply, PostInventoryAdjustmentHandler.ItemNotStocked),
                     (notActive, "guard-blocked", notActiveReply, PostInventoryAdjustmentHandler.ItemNotActive),
                 })
        {
            Assert.True(reply.Status == 422, $"{key}: {reply.Body}");
            Assert.Equal("R-DOC-01", reply.RuleId);
            Assert.Equal(code, reply.Code);
            await AssertNothingPostedAsync(id, key, stored: true);
        }

        Assert.Equal("1", lotReply.Json!["errors"]![0]!["details"]!["lineNumber"]!.GetValue<string>());
    }

    [Fact]
    public async Task R_INV_05_an_outbound_line_with_an_entered_cost_and_an_inbound_line_without_one_on_an_item_without_stock_or_average_are_rejected()
    {
        Guid item = await CreateItemAsync("P-COST");

        (Guid outbound, CommandReply outboundReply) = await PostNewAsync("cost-out", [Line(1, item, "-1", enteredUnitCost: "1.00")]);
        (Guid inbound, CommandReply inboundReply) = await PostNewAsync("cost-in", [Line(1, item, "1")]);

        Assert.Equal(422, outboundReply.Status);
        Assert.Equal("R-INV-05", outboundReply.RuleId);
        Assert.Equal(PostInventoryAdjustmentHandler.EnteredCostOnOutbound, outboundReply.Code);
        Assert.Equal(422, inboundReply.Status);
        Assert.Equal("R-INV-05", inboundReply.RuleId);
        Assert.Equal(MovingAverageCost.EnteredCostRequired, inboundReply.Code);
        Assert.Equal("1", inboundReply.Json!["errors"]![0]!["details"]!["lineNumber"]!.GetValue<string>());
        await AssertNothingPostedAsync(outbound, "cost-out", stored: true);
        // The inbound check runs after lock class 1; the rollback gives the version back too.
        await AssertNothingPostedAsync(inbound, "cost-in", stored: true);
    }

    // ---- idempotency, versions, permissions ----------------------------------------------------------------------

    [Fact]
    public async Task R_API_01_replaying_the_POST_returns_the_first_result_and_posts_nothing_twice_a_concurrent_duplicate_gets_409_and_R_API_02_a_stale_If_Match_is_412()
    {
        Guid item = await CreateItemAsync("P-REPLAY");
        Guid id = await DraftAsync("replay-create", [Line(1, item, "10", enteredUnitCost: "1.00")]);
        Guid other = await DraftAsync("replay-other-create", [Line(1, item, "1", enteredUnitCost: "1.00")]);

        CommandReply posted = await PostAsync(id, "replay-post");
        CommandReply replay = await PostAsync(id, "replay-post");
        CommandReply differentBody = await PostAsync(other, "replay-post");
        CommandReply again = await PostAsync(id, "replay-again", ifMatch: posted.ETag);
        CommandReply stale = await PostAsync(other, "replay-stale", ifMatch: "\"7\"");
        CommandReply missing = await PostAsync(other, "replay-missing", ifMatch: null);

        Assert.Equal(200, posted.Status);
        Assert.Equal("\"2\"", posted.ETag);
        Assert.Equal(2, posted.Json!["version"]!.GetValue<long>());
        Assert.True(replay.Replayed);
        Assert.Equal(posted.Body, replay.Body);
        // R-API-04: the same key with another request is refused and the first result kept.
        Assert.Equal(422, differentBody.Status);
        Assert.Equal("R-API-04", differentBody.RuleId);
        // R-DOC-01: Posted has no POST transition.
        Assert.Equal(422, again.Status);
        Assert.Equal("R-DOC-01", again.RuleId);
        Assert.Equal(DocumentStateMachine.TransitionNotAllowed, again.Code);
        Assert.Equal(412, stale.Status);
        Assert.Equal("R-API-02", stale.RuleId);
        Assert.Equal(428, missing.Status);
        Assert.Equal(1L, await _host.ScalarAsync("SELECT count(*) FROM kernel.journal_entry WHERE source_document_id = @id", Ct, ("id", id)));
        Assert.Equal(1L, await _host.ScalarAsync("SELECT count(*) FROM kernel.outbox_event WHERE aggregate_id = @id AND event_type = 'INVENTORY_ADJUSTED'", Ct, ("id", id)));
        Assert.Equal(0L, await _host.ScalarAsync("SELECT count(*) FROM kernel.idempotency_record WHERE key IN ('replay-stale', 'replay-missing')", Ct));
        Assert.Equal((10m, 10.00m, 1.00m), await CostStateAsync(item));

        // R-API-01: a concurrent duplicate answers 409 at once while the first request holds the key's advisory lock.
        long lockKey = AdvisoryLockKey.For(new UserId(TestUsers.Clerk), IdempotencyKey.From("replay-concurrent"));
        await using NpgsqlConnection holder = await PostgresFixture.OpenAsync(_db.ConnectionStringFor("themis_app"), Ct);
        await using NpgsqlTransaction running = await holder.BeginTransactionAsync(Ct);
        await using (NpgsqlCommand take = new("SET LOCAL ROLE themis_kernel; SELECT pg_advisory_xact_lock(@key)", holder, running))
        {
            take.Parameters.AddWithValue("key", lockKey);
            await take.ExecuteNonQueryAsync(Ct);
        }

        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        CommandReply duplicate = await _host.PostAsync($"{Adjustments}/{other}/post", null, "replay-concurrent", ifMatch: "\"1\"", cancellationToken: deadline.Token);
        await running.RollbackAsync(Ct);

        Assert.Equal(409, duplicate.Status);
        Assert.Equal("idempotency_key_in_progress", duplicate.Code);
        Assert.Equal("R-API-01", duplicate.RuleId);
        await AssertNothingPostedAsync(other, "replay-concurrent", stored: false);
        await AssertInvariantsAsync();
    }

    [Fact]
    public async Task R_SEC_01_a_user_without_INVENTORY_ADJUSTMENT_POST_is_rejected_with_403_and_the_rule_id_and_a_user_holding_only_POST_posts()
    {
        Guid item = await CreateItemAsync("P-SEC");
        Guid id = await DraftAsync("sec-create", [Line(1, item, "10", enteredUnitCost: "1.00")]);

        CommandReply preparer = await PostAsync(id, "sec-preparer", subject: TestUsers.PreparerSubject);
        CommandReply poster = await PostAsync(id, "sec-poster", subject: _users!.PosterOnlySubject);

        Assert.Equal(403, preparer.Status);
        Assert.Equal("application/problem+json", preparer.MediaType);
        Assert.Equal("permission_missing", preparer.Code);
        Assert.Equal("R-SEC-01", preparer.RuleId);
        Assert.Equal(0L, await _host.ScalarAsync("SELECT count(*) FROM kernel.idempotency_record WHERE key = 'sec-preparer'", Ct));
        Assert.Equal(200, poster.Status);
        Assert.Equal(_users.PosterOnlyUser, poster.Json!["postedBy"]!.GetValue<Guid>());
        Assert.Equal(_users.PosterOnlyUser, await _host.ScalarAsync("SELECT posted_by FROM kernel.document WHERE id = @id", Ct, ("id", id)));
        Assert.Equal(_users.PosterOnlyUser, await _host.ScalarAsync("SELECT DISTINCT recorded_by FROM kernel.inventory_entry e JOIN kernel.document_line l ON l.id = e.source_line_id WHERE l.document_id = @id", Ct, ("id", id)));
    }

    // ---- concurrency and the lock order ---------------------------------------------------------------------------

    [Fact]
    public async Task R_LED_08_two_adjustments_on_the_same_items_with_their_lines_in_opposite_order_post_concurrently_without_deadlock()
    {
        Guid bolt = await CreateItemAsync("P-LOCK-A");
        Guid nut = await CreateItemAsync("P-LOCK-B");
        (_, CommandReply stock) = await PostNewAsync("lock-stock", [Line(1, bolt, "100", enteredUnitCost: "1.00"), Line(2, nut, "100", enteredUnitCost: "1.00")]);
        Assert.Equal(200, stock.Status);
        Guid first = await DraftAsync("lock-first-create", [Line(1, bolt, "-10"), Line(2, nut, "-10")]);
        Guid second = await DraftAsync("lock-second-create", [Line(1, nut, "-5"), Line(2, bolt, "-5")]);

        Task<CommandReply> firstPost = PostAsync(first, "lock-first");
        Task<CommandReply> secondPost = PostAsync(second, "lock-second");
        await Task.WhenAll(firstPost, secondPost);

        Assert.True(firstPost.Result.Status == 200, firstPost.Result.Body);
        Assert.True(secondPost.Result.Status == 200, secondPost.Result.Body);
        Assert.Equal(85m, await OnHandAsync(bolt, Warehouse));
        Assert.Equal(85m, await OnHandAsync(nut, Warehouse));
        Assert.Equal((85m, 85.00m, 1.00m), await CostStateAsync(bolt));
        Assert.Equal((85m, 85.00m, 1.00m), await CostStateAsync(nut));
        await AssertInvariantsAsync();
    }

    [Fact]
    public async Task R_LED_08_two_adjustments_on_new_items_race_the_cache_inserts_and_both_post_with_one_cache_row_per_key()
    {
        Guid washer = await CreateItemAsync("P-RACE-A");
        Guid screw = await CreateItemAsync("P-RACE-B");
        Guid first = await DraftAsync("race-first-create", [Line(1, washer, "10", enteredUnitCost: "1.00"), Line(2, screw, "10", enteredUnitCost: "1.00")]);
        Guid second = await DraftAsync("race-second-create", [Line(1, screw, "5", enteredUnitCost: "2.00"), Line(2, washer, "5", enteredUnitCost: "2.00")]);

        Task<CommandReply> firstPost = PostAsync(first, "race-first");
        Task<CommandReply> secondPost = PostAsync(second, "race-second");
        await Task.WhenAll(firstPost, secondPost);

        Assert.True(firstPost.Result.Status == 200, firstPost.Result.Body);
        Assert.True(secondPost.Result.Status == 200, secondPost.Result.Body);
        foreach (Guid item in new[] { washer, screw })
        {
            Assert.Equal(1L, await _host.ScalarAsync("SELECT count(*) FROM kernel.item_cost_state WHERE item_id = @item", Ct, ("item", item)));
            Assert.Equal(1L, await _host.ScalarAsync("SELECT count(*) FROM kernel.stock_balance WHERE item_id = @item", Ct, ("item", item)));
            Assert.Equal((15m, 20.00m, 1.3333333333m), await CostStateAsync(item));
        }

        await AssertInvariantsAsync();
    }

    // ---- audit, outbox and the database roles ---------------------------------------------------------------------

    [Fact]
    public async Task D_51_every_audit_event_and_ledger_row_of_a_posting_carries_the_same_command_id_the_outbox_envelope_is_in_the_transaction_and_an_audit_row_without_a_command_id_is_refused()
    {
        Guid item = await CreateItemAsync("P-AUDIT");
        (Guid id, CommandReply reply) = await PostNewAsync("audit", [Line(1, item, "10", enteredUnitCost: "2.00"), Line(2, item, "-4")], correlationId: "corr-audit");
        Assert.Equal(200, reply.Status);
        Guid line1 = reply.Json!["lines"]![0]!["id"]!.GetValue<Guid>();
        Guid line2 = reply.Json["lines"]![1]!["id"]!.GetValue<Guid>();

        // R-AUD-01 as amended by D-51: one audit event for the document and one per costed line, all with the command's id; nothing else gets an audit event.
        const string audit = "FROM kernel.audit_event WHERE command_name = 'INVENTORY_ADJUSTMENT.POST'";
        Assert.Equal(3L, await _host.ScalarAsync($"SELECT count(*) {audit}", Ct));
        Assert.Equal(1L, await _host.ScalarAsync($"SELECT count(DISTINCT command_id) {audit}", Ct));
        Guid commandId = (Guid)(await _host.ScalarAsync($"SELECT DISTINCT command_id {audit}", Ct))!;
        Assert.Equal(id, await _host.ScalarAsync($"SELECT entity_id {audit} AND entity_type = 'Document'", Ct));
        Assert.Equal(new HashSet<Guid> { line1, line2 }, new HashSet<Guid> { (Guid)(await _host.ScalarAsync($"SELECT min(entity_id::text)::uuid {audit} AND entity_type = 'DocumentLine'", Ct))!, (Guid)(await _host.ScalarAsync($"SELECT max(entity_id::text)::uuid {audit} AND entity_type = 'DocumentLine'", Ct))! });
        string documentChanges = (string)(await _host.ScalarAsync($"SELECT changes::text {audit} AND entity_type = 'Document'", Ct))!;
        foreach (string field in new[] { "status", "documentNumber", "postedAt", "postedBy", "version" })
        {
            Assert.Contains($"\"{field}\"", documentChanges, StringComparison.Ordinal);
        }

        Assert.Equal("Posted", await _host.ScalarAsync($"SELECT changes->'status'->>'after' {audit} AND entity_type = 'Document'", Ct));
        Assert.Equal("Draft", await _host.ScalarAsync($"SELECT changes->'status'->>'before' {audit} AND entity_type = 'Document'", Ct));
        Assert.Equal("20", await _host.ScalarAsync($"SELECT changes->'costAmount'->>'after' {audit} AND entity_id = @line", Ct, ("line", line1)));
        Assert.Equal("-8", await _host.ScalarAsync($"SELECT changes->'costAmount'->>'after' {audit} AND entity_id = @line", Ct, ("line", line2)));

        // The ledger rows carry the command id, the time and the user instead of audit events of their own (D-51).
        Assert.Equal(commandId, await _host.ScalarAsync("SELECT command_id FROM kernel.journal_entry WHERE source_document_id = @id", Ct, ("id", id)));
        Assert.Equal(1L, await _host.ScalarAsync("SELECT count(DISTINCT command_id) FROM kernel.inventory_entry e JOIN kernel.document_line l ON l.id = e.source_line_id WHERE l.document_id = @id AND e.command_id = @command AND e.recorded_by = @user", Ct, ("id", id), ("command", commandId), ("user", TestUsers.Clerk)));
        Assert.Equal(TestUsers.Clerk, await _host.ScalarAsync("SELECT recorded_by FROM kernel.journal_entry WHERE source_document_id = @id", Ct, ("id", id)));
        Assert.Equal($"Inventory adjustment {reply.Json["documentNumber"]!.GetValue<string>()}", await _host.ScalarAsync("SELECT description FROM kernel.journal_entry WHERE source_document_id = @id", Ct, ("id", id)));
        Assert.Equal(Milestone1OrganisationFixture.PeriodId(6), await _host.ScalarAsync("SELECT fiscal_period_id FROM kernel.journal_entry WHERE source_document_id = @id", Ct, ("id", id)));
        Assert.Equal(0L, await _host.ScalarAsync("SELECT count(*) FROM kernel.audit_event WHERE command_name NOT LIKE 'SYSTEM.%' AND entity_type IN ('JournalEntry', 'JournalLine', 'InventoryEntry', 'ItemCostState', 'StockBalance', 'NumberSeries')", Ct));

        // D-44, D-50: INVENTORY_ADJUSTED in the same transaction, with the envelope and the changed fields of the document and its lines.
        string outbox = "FROM kernel.outbox_event WHERE aggregate_id = @id AND event_type = 'INVENTORY_ADJUSTED'";
        Assert.Equal(1L, await _host.ScalarAsync($"SELECT count(*) {outbox}", Ct, ("id", id)));
        Assert.Equal(("INVENTORY_ADJUSTMENT", Company, "INVENTORY_ADJUSTMENT.POST", "corr-audit"), ((string)(await _host.ScalarAsync($"SELECT aggregate_type {outbox}", Ct, ("id", id)))!, (Guid)(await _host.ScalarAsync($"SELECT company_id {outbox}", Ct, ("id", id)))!, (string)(await _host.ScalarAsync($"SELECT command_name {outbox}", Ct, ("id", id)))!, (string)(await _host.ScalarAsync($"SELECT correlation_id {outbox}", Ct, ("id", id)))!));
        Assert.Equal(id.ToString(), await _host.ScalarAsync($"SELECT payload->>'id' {outbox}", Ct, ("id", id)));
        Assert.Equal("Posted", await _host.ScalarAsync($"SELECT payload->'changes'->'status'->>'after' {outbox}", Ct, ("id", id)));
        Assert.Equal(reply.Json["documentNumber"]!.GetValue<string>(), await _host.ScalarAsync($"SELECT payload->'changes'->'documentNumber'->>'after' {outbox}", Ct, ("id", id)));
        Assert.Equal("2", await _host.ScalarAsync($"SELECT payload->'changes'->'version'->>'after' {outbox}", Ct, ("id", id)));
        Assert.NotNull(await _host.ScalarAsync($"SELECT payload->'changes'->'postedAt'->>'after' {outbox}", Ct, ("id", id)));
        Assert.Equal(2, await _host.ScalarAsync($"SELECT jsonb_array_length(payload->'lines') {outbox}", Ct, ("id", id)));
        Assert.Equal("Posted", await _host.ScalarAsync($"SELECT payload->'lines'->0->>'change' {outbox}", Ct, ("id", id)));
        Assert.Equal("2", await _host.ScalarAsync($"SELECT payload->'lines'->0->'changes'->'unitCost'->>'after' {outbox}", Ct, ("id", id)));
        Assert.Equal("-8", await _host.ScalarAsync($"SELECT payload->'lines'->1->'changes'->'costAmount'->>'after' {outbox}", Ct, ("id", id)));

        // D-51: audit_event.command_id is required of every new row; the check stays NOT VALID for the rows before it.
        await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(_db.ConnectionStringFor("themis_app"), Ct);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(Ct);
        await ExecuteAsync(connection, "SET LOCAL ROLE themis_kernel");
        await AssertFailsAsync(connection, CheckViolation, $"INSERT INTO kernel.audit_event (id, occurred_at, user_id, command_name, entity_type, entity_id, changes) VALUES (gen_random_uuid(), now(), '{TestUsers.Clerk}', 'PROBE.WRITE', 'Probe', gen_random_uuid(), '{{}}')");
        Assert.False((bool)(await ScalarAsync(connection, "SELECT convalidated FROM pg_constraint WHERE conname = 'ck_audit_event_command_id'"))!);

        // I-14: the kernel role cannot update or delete what it just posted.
        foreach (string table in new[] { "journal_entry", "journal_line", "inventory_entry" })
        {
            await AssertFailsAsync(connection, InsufficientPrivilege, $"UPDATE kernel.{table} SET company_id = company_id");
            await AssertFailsAsync(connection, InsufficientPrivilege, $"DELETE FROM kernel.{table}");
        }

        await transaction.RollbackAsync(Ct);
        await AssertInvariantsAsync();
    }

    [Fact]
    public async Task D_51_audit_rows_written_before_the_posting_migration_keep_an_empty_command_id_and_the_check_is_added_NOT_VALID()
    {
        string fresh = await _db.CreateEmptyDatabaseAsync(Ct);
        try
        {
            string migrator = new NpgsqlConnectionStringBuilder(fresh) { Username = "themis_migrator", Password = PostgresFixture.TestRolePassword }.ConnectionString;
            // The database as step 7 left it, with one audit row of the old shape.
            await using (KernelDbContext context = KernelDbContextFactory.ForMigrations(migrator))
            {
                await context.GetService<IMigrator>().MigrateAsync("20261017221115_DocumentFrameworkTables", Ct);
            }

            Guid user = Guid.NewGuid();
            Guid old = Guid.NewGuid();
            await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(migrator, Ct);
            await ExecuteAsync(connection, $"INSERT INTO kernel.user_account (id, external_subject, display_name, is_service_account, status, created_at, created_by, updated_at, updated_by) VALUES ('{user}', 'old-{user:N}', 'Old', false, 'Active', now(), '{user}', now(), '{user}')");
            await ExecuteAsync(connection, $"INSERT INTO kernel.audit_event (id, occurred_at, user_id, command_name, entity_type, entity_id, changes) VALUES ('{old}', now(), '{user}', 'OLD.WRITE', 'Probe', gen_random_uuid(), '{{}}')");

            Assert.Equal(0, await Themis.Migrator.Program.Main(["--connection", migrator]));

            Assert.Null(await ScalarAsync(connection, "SELECT command_id FROM kernel.audit_event WHERE id = @id", ("id", old)));
            Assert.Equal(1L, await ScalarAsync(connection, "SELECT count(*) FROM kernel.audit_event WHERE id = @id", ("id", old)));
            Assert.False((bool)(await ScalarAsync(connection, "SELECT convalidated FROM pg_constraint WHERE conname = 'ck_audit_event_command_id'"))!);
            // The data migrations of steps 4 and 5 wrote their SYSTEM.MIGRATE rows before the column existed; every seeder run after the migration carries the id.
            Assert.Equal(0L, await ScalarAsync(connection, "SELECT count(*) FROM kernel.audit_event WHERE command_id IS NULL AND id <> @id AND command_name <> 'SYSTEM.MIGRATE'", ("id", old)));
            Assert.True((long)(await ScalarAsync(connection, "SELECT count(*) FROM kernel.audit_event WHERE command_id IS NOT NULL"))! > 0);
            await AssertFailsAsync(connection, CheckViolation, $"INSERT INTO kernel.audit_event (id, occurred_at, user_id, command_name, entity_type, entity_id, changes) VALUES (gen_random_uuid(), now(), '{user}', 'NEW.WRITE', 'Probe', gen_random_uuid(), '{{}}')", insideTransaction: false);
        }
        finally
        {
            await _db.DropDatabaseAsync(fresh, CancellationToken.None);
        }
    }

    // ---- helpers --------------------------------------------------------------------------------------------------

    private async Task<Guid> CreateItemAsync(string number, string lotTracking = "None", string itemType = "Stocked")
    {
        string unique = $"{number}-{++_itemSequence}";
        CommandReply reply = await _host.PostAsync("/v1/kernel/items", new { companyId = Company, itemNumber = unique, description = $"Item {unique}", itemType, baseUnit = "H87", lotTracking, itemPostingGroup = "TRADE_GOODS", taxClass = "Standard", costingMethod = "MovingAverage" }, "item-" + unique, cancellationToken: Ct);
        Assert.True(reply.Status == 200, reply.Body);
        return reply.Json!["id"]!.GetValue<Guid>();
    }

    private static object Line(int number, Guid item, string quantity, Guid? warehouse = null, string stockStatus = "Available", string? enteredUnitCost = null) =>
        new { lineNumber = number, itemId = item, quantity, warehouseId = warehouse, stockStatus, enteredUnitCost };

    private async Task<Guid> DraftAsync(string key, object[] lines, string? reasonCode = "CountDifference", string? postingDate = June)
    {
        CommandReply reply = await _host.PostAsync(Adjustments, new { companyId = Company, documentDate = June, reasonCode, warehouseId = Warehouse, postingDate, lines }, key, cancellationToken: Ct);
        Assert.True(reply.Status == 200, reply.Body);
        Assert.Equal("\"1\"", reply.ETag);
        return reply.Json!["id"]!.GetValue<Guid>();
    }

    private Task<CommandReply> PostAsync(Guid id, string key, string? ifMatch = "\"1\"", string? subject = null, string? correlationId = null) =>
        _host.PostAsync($"{Adjustments}/{id}/post", null, key, ifMatch: ifMatch, subject: subject, correlationId: correlationId, cancellationToken: Ct);

    private async Task<(Guid Id, CommandReply Reply)> PostNewAsync(string key, object[] lines, string? reasonCode = "CountDifference", string? postingDate = June, string? correlationId = null)
    {
        Guid id = await DraftAsync($"{key}-create", lines, reasonCode, postingDate);
        return (id, await PostAsync(id, key, correlationId: correlationId));
    }

    private static decimal Decimal(JsonNode? node) => decimal.Parse(node!.GetValue<string>(), CultureInfo.InvariantCulture);

    private static string JournalNumber(long sequence) => $"{Milestone1NumberSeriesFixture.JournalEntryPrefix}{sequence.ToString(CultureInfo.InvariantCulture).PadLeft(Milestone1NumberSeriesFixture.Padding, '0')}";

    private async Task<long> NextJournalNumberAsync() => (long)(await _host.ScalarAsync("SELECT next_number FROM kernel.number_series WHERE id = @id", Ct, ("id", Milestone1NumberSeriesFixture.JournalEntrySeriesId)))!;

    private async Task<string?> EntryNumberAsync(Guid documentId) => (string?)await _host.ScalarAsync("SELECT entry_number FROM kernel.journal_entry WHERE source_document_id = @id", Ct, ("id", documentId));

    private async Task<(decimal Quantity, decimal Value, decimal? Average)> CostStateAsync(Guid item)
    {
        await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(_db.SuperuserConnectionString, Ct);
        await using NpgsqlCommand command = new("SELECT quantity_base, inventory_value, average_cost FROM kernel.item_cost_state WHERE company_id = @company AND item_id = @item", connection);
        command.Parameters.AddWithValue("company", Company);
        command.Parameters.AddWithValue("item", item);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(Ct);
        Assert.True(await reader.ReadAsync(Ct), $"No cost state for item {item}.");
        return (reader.GetDecimal(0), reader.GetDecimal(1), reader.IsDBNull(2) ? null : reader.GetDecimal(2));
    }

    private async Task<decimal> OnHandAsync(Guid item, Guid warehouse, string stockStatus = "Available") =>
        (decimal)(await _host.ScalarAsync("SELECT on_hand_base FROM kernel.stock_balance WHERE company_id = @company AND item_id = @item AND warehouse_id = @warehouse AND stock_status = @status AND lot_id IS NULL", Ct, ("company", Company), ("item", item), ("warehouse", warehouse), ("status", stockStatus)))!;

    private sealed record JournalLine(int LineNumber, Guid AccountId, string AccountRole, decimal Amount, decimal TxnAmount, string TxnCurrency, Guid? ItemId, Guid? WarehouseId, Guid? SourceLineId);

    private async Task<IReadOnlyList<JournalLine>> JournalLinesAsync(Guid documentId)
    {
        await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(_db.SuperuserConnectionString, Ct);
        await using NpgsqlCommand command = new("SELECT l.line_number, l.account_id, l.account_role, l.amount, l.txn_amount, l.txn_currency, l.item_id, l.warehouse_id, l.source_line_id FROM kernel.journal_line l JOIN kernel.journal_entry e ON e.id = l.journal_entry_id WHERE e.source_document_id = @id ORDER BY l.line_number", connection);
        command.Parameters.AddWithValue("id", documentId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(Ct);
        List<JournalLine> lines = [];
        while (await reader.ReadAsync(Ct))
        {
            lines.Add(new JournalLine(reader.GetInt32(0), reader.GetGuid(1), reader.GetString(2), reader.GetDecimal(3), reader.GetDecimal(4), reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetGuid(6), reader.IsDBNull(7) ? null : reader.GetGuid(7), reader.IsDBNull(8) ? null : reader.GetGuid(8)));
        }

        return lines;
    }

    /// <summary>The document is still a Draft at version 1 and nothing of the posting exists; a stored rejection (R-API-03) keeps the key's result, a failure or a 409 keeps nothing.</summary>
    private async Task AssertNothingPostedAsync(Guid id, string key, bool stored)
    {
        Assert.Equal("Draft", await _host.ScalarAsync("SELECT status FROM kernel.document WHERE id = @id", Ct, ("id", id)));
        Assert.Equal(1L, await _host.ScalarAsync("SELECT version FROM kernel.document WHERE id = @id", Ct, ("id", id)));
        Assert.Null(await _host.ScalarAsync("SELECT document_number FROM kernel.document WHERE id = @id", Ct, ("id", id)));
        Assert.Equal(0L, await _host.ScalarAsync("SELECT count(*) FROM kernel.document_line WHERE document_id = @id AND (cost_amount IS NOT NULL OR unit_cost IS NOT NULL)", Ct, ("id", id)));
        Assert.Equal(0L, await _host.ScalarAsync("SELECT count(*) FROM kernel.journal_entry WHERE source_document_id = @id", Ct, ("id", id)));
        Assert.Equal(0L, await _host.ScalarAsync("SELECT count(*) FROM kernel.inventory_entry e JOIN kernel.document_line l ON l.id = e.source_line_id WHERE l.document_id = @id", Ct, ("id", id)));
        Assert.Equal(0L, await _host.ScalarAsync("SELECT count(*) FROM kernel.audit_event WHERE entity_id = @id AND command_name = 'INVENTORY_ADJUSTMENT.POST'", Ct, ("id", id)));
        Assert.Equal(0L, await _host.ScalarAsync("SELECT count(*) FROM kernel.outbox_event WHERE aggregate_id = @id AND event_type = 'INVENTORY_ADJUSTED'", Ct, ("id", id)));
        Assert.Equal(stored ? 1L : 0L, await _host.ScalarAsync("SELECT count(*) FROM kernel.idempotency_record WHERE key = @key", Ct, ("key", key)));
    }

    /// <summary>I-01, I-02, I-03, I-04, I-06, I-08 and I-09 over everything posted so far.</summary>
    private async Task AssertInvariantsAsync()
    {
        // I-01: every journal entry sums to 0.
        Assert.Equal(0L, await _host.ScalarAsync("SELECT count(*) FROM (SELECT journal_entry_id FROM kernel.journal_line GROUP BY journal_entry_id HAVING sum(amount) <> 0) unbalanced", Ct));
        // I-02: no zero journal line, no zero-quantity inventory entry, txn and company amounts share a sign.
        Assert.Equal(0L, await _host.ScalarAsync("SELECT count(*) FROM kernel.journal_line WHERE amount = 0 OR sign(txn_amount) <> sign(amount)", Ct));
        Assert.Equal(0L, await _host.ScalarAsync("SELECT count(*) FROM kernel.inventory_entry WHERE quantity_base = 0", Ct));
        // I-03: stock balance on hand = sum of entry quantities per key, never below 0.
        Assert.Equal(0L, await _host.ScalarAsync("SELECT count(*) FROM kernel.stock_balance b WHERE b.on_hand_base < 0 OR b.on_hand_base <> coalesce((SELECT sum(e.quantity_base) FROM kernel.inventory_entry e WHERE e.company_id = b.company_id AND e.item_id = b.item_id AND e.warehouse_id = b.warehouse_id AND e.stock_status = b.stock_status AND e.lot_id IS NOT DISTINCT FROM b.lot_id), 0)", Ct));
        Assert.Equal(0L, await _host.ScalarAsync("SELECT count(*) FROM (SELECT company_id, item_id, warehouse_id, lot_id, stock_status FROM kernel.inventory_entry EXCEPT SELECT company_id, item_id, warehouse_id, lot_id, stock_status FROM kernel.stock_balance) missing", Ct));
        // I-04: item cost state = sums of entry cost and quantity per company and item; value 0 at quantity 0.
        Assert.Equal(0L, await _host.ScalarAsync("SELECT count(*) FROM kernel.item_cost_state s LEFT JOIN (SELECT company_id, item_id, sum(quantity_base) AS quantity, sum(cost_amount) AS value FROM kernel.inventory_entry GROUP BY company_id, item_id) e ON e.company_id = s.company_id AND e.item_id = s.item_id WHERE (s.quantity_base = 0 AND s.inventory_value <> 0) OR s.quantity_base <> coalesce(e.quantity, 0) OR s.inventory_value <> coalesce(e.value, 0)", Ct));
        Assert.Equal(0L, await _host.ScalarAsync("SELECT count(*) FROM (SELECT company_id, item_id FROM kernel.inventory_entry EXCEPT SELECT company_id, item_id FROM kernel.item_cost_state) missing", Ct));
        // I-06: the balance of the INVENTORY role = the sum of inventory entry costs, per company.
        Assert.Equal(0L, await _host.ScalarAsync("SELECT count(*) FROM (SELECT c.id FROM kernel.company c WHERE (SELECT coalesce(sum(l.amount), 0) FROM kernel.journal_line l WHERE l.company_id = c.id AND l.account_role = 'INVENTORY') <> (SELECT coalesce(sum(e.cost_amount), 0) FROM kernel.inventory_entry e WHERE e.company_id = c.id)) off", Ct));
        // I-08: every entry with a cost links to a journal entry; a posted document has one journal entry exactly when a line has a cost.
        Assert.Equal(0L, await _host.ScalarAsync("SELECT count(*) FROM kernel.inventory_entry WHERE cost_amount <> 0 AND journal_entry_id IS NULL", Ct));
        Assert.Equal(0L, await _host.ScalarAsync("SELECT count(*) FROM kernel.document d WHERE d.status = 'Posted' AND (SELECT count(*) FROM kernel.journal_entry j WHERE j.source_document_id = d.id) <> (CASE WHEN EXISTS (SELECT 1 FROM kernel.inventory_entry e JOIN kernel.document_line l ON l.id = e.source_line_id WHERE l.document_id = d.id AND e.cost_amount <> 0) THEN 1 ELSE 0 END)", Ct));
        // I-09: the journal entry numbers of the fiscal year are the contiguous sequence from 1.
        long entries = await _host.CountAsync("kernel.journal_entry", Ct);
        Assert.Equal(entries, await _host.ScalarAsync("SELECT count(DISTINCT entry_number) FROM kernel.journal_entry", Ct));
        Assert.Equal(entries + 1, await NextJournalNumberAsync());
        if (entries > 0)
        {
            Assert.Equal(JournalNumber(entries), await _host.ScalarAsync("SELECT max(entry_number) FROM kernel.journal_entry", Ct));
        }
    }

    private async Task SeedFiscalYear2027Async()
    {
        string system = Themis.Kernel.Contracts.Security.WellKnownUsers.System.Value.ToString();
        string audit = $"now(), '{system}', now(), '{system}'";
        List<string> statements = [$"INSERT INTO kernel.fiscal_year (id, company_id, code, start_date, end_date, status, created_at, created_by, updated_at, updated_by) VALUES ('{FiscalYear2027}', '{Company}', '2027', '2027-01-01', '2027-12-31', 'Open', {audit})"];
        for (int month = 1; month <= 12; month++)
        {
            DateOnly start = new(2027, month, 1);
            DateOnly end = start.AddMonths(1).AddDays(-1);
            statements.Add($"INSERT INTO kernel.fiscal_period (id, fiscal_year_id, company_id, period_number, start_date, end_date, status, created_at, created_by, updated_at, updated_by) VALUES ('{Guid.Parse($"00000000-0000-7000-8000-0000000f27{month:00}")}', '{FiscalYear2027}', '{Company}', {month}, '{start:yyyy-MM-dd}', '{end:yyyy-MM-dd}', 'Open', {audit})");
        }

        await _db.ExecuteAsMigratorAsync(string.Join("; ", statements), Ct);
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using NpgsqlCommand command = new(sql, connection);
        await command.ExecuteNonQueryAsync(Ct);
    }

    private static async Task<object?> ScalarAsync(NpgsqlConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        await using NpgsqlCommand command = new(sql, connection);
        foreach ((string name, object value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        object? result = await command.ExecuteScalarAsync(Ct);
        return result is DBNull ? null : result;
    }

    private static async Task AssertFailsAsync(NpgsqlConnection connection, string sqlState, string sql, bool insideTransaction = true)
    {
        if (insideTransaction)
        {
            await ExecuteAsync(connection, "SAVEPOINT probe");
        }

        PostgresException exception = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connection, sql));
        Assert.Equal(sqlState, exception.SqlState);
        if (insideTransaction)
        {
            await ExecuteAsync(connection, "ROLLBACK TO SAVEPOINT probe");
        }
    }
}

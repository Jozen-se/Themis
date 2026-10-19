using Microsoft.Extensions.Logging;
using Themis.Integration.Tests.Pipeline;
using Themis.Kernel.Organisation;

namespace Themis.Integration.Tests.Hardening;

/// <summary>
/// The pipeline fixes of the code review (Remediation Register, Code review 2026-10-18), each through the real
/// pipeline over HTTP: retry on a wrapped deadlock (RR-40), expired idempotency records (RR-42), unique-key races
/// (RR-45), value bounds and a numeric overflow at posting (RR-46), the user before the validators (RR-51), the
/// correlation id format (RR-53) and repeated query string keys (RR-56). The milestone 1 acceptance tests stay as
/// they are; nothing here changes a business behaviour they prove.
/// </summary>
[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Integration")]
public sealed class PipelineHardeningTests : IAsyncLifetime
{
    private const string Counters = "/v1/fixture/counters";
    private const string Items = "/v1/kernel/items";
    private const string Adjustments = "/v1/kernel/inventory-adjustments";
    private const string June = "2026-06-15";

    private static readonly Guid Company = Milestone1OrganisationFixture.CompanyId;
    private static readonly Guid Warehouse = Milestone1OrganisationFixture.WarehouseId;

    private readonly PostgresFixture _db;
    private PipelineTestHost _host = null!;
    private int _sequence;

    public PipelineHardeningTests(PostgresFixture db)
    {
        _db = db;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await _db.TruncateAsync(Ct);
        _host = await PipelineTestHost.StartAsync(_db);
    }

    public async ValueTask DisposeAsync()
    {
        _host.Barrier.Arm(0);
        _host.ItemCreateSeam.AfterDuplicateCheck = null;
        await _db.TruncateAsync(CancellationToken.None);
        await _host.DisposeAsync();
    }

    // ---- RR-40 -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task RR_40_a_real_deadlock_raised_through_Entity_Framework_is_retried_once_and_both_commands_succeed()
    {
        Guid first = await CreateCounterAsync("rr40-a");
        Guid second = await CreateCounterAsync("rr40-b");
        long deadlocksBefore = _host.RetriesBySqlState.GetValueOrDefault("40P01");

        // Both commands write their first row, meet at the barrier, then go for the other's row: PostgreSQL ends one with 40P01,
        // wrapped by SaveChanges in a DbUpdateException; the retry (first attempt only at the barrier) runs after the winner commits.
        _host.Barrier.Arm(2);
        Task<CommandReply> left = _host.PostAsync($"{Counters}/touch-pair", new { firstId = first, secondId = second, increment = 1 }, "rr40-pair-left", cancellationToken: Ct);
        Task<CommandReply> right = _host.PostAsync($"{Counters}/touch-pair", new { firstId = second, secondId = first, increment = 10 }, "rr40-pair-right", cancellationToken: Ct);
        await Task.WhenAll(left, right);

        Assert.True(left.Result.Status == 200, left.Result.Body);
        Assert.True(right.Result.Status == 200, right.Result.Body);
        Assert.Equal(1L, _host.RetriesBySqlState.GetValueOrDefault("40P01") - deadlocksBefore);
        Assert.Contains(_host.Logs.Lines, line => line.Level == LogLevel.Warning && line.Message.Contains("40P01", StringComparison.Ordinal) && line.Message.Contains("D-41", StringComparison.Ordinal));
        Assert.Equal(16L, await _host.ScalarAsync("SELECT value FROM fixture.counter WHERE id = @id", Ct, ("id", first)));
        Assert.Equal(16L, await _host.ScalarAsync("SELECT value FROM fixture.counter WHERE id = @id", Ct, ("id", second)));
        Assert.Equal(3L, await _host.ScalarAsync("SELECT version FROM fixture.counter WHERE id = @id", Ct, ("id", first)));
        Assert.Equal(4L, await _host.ScalarAsync("SELECT count(*) FROM kernel.outbox_event WHERE event_type = 'FIXTURE_COUNTER_TOUCHED'", Ct));
        Assert.Equal(2L, await _host.ScalarAsync("SELECT count(*) FROM kernel.idempotency_record WHERE key LIKE 'rr40-pair-%'", Ct));
    }

    // ---- RR-42 -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task RR_42_an_expired_idempotency_record_counts_as_absent_the_record_is_replaced_and_the_key_runs_as_new()
    {
        CommandReply first = await _host.PostAsync(Counters, new { companyId = Company, initialValue = 1, sample = "1" }, "rr42-expired", cancellationToken: Ct);
        Assert.Equal(200, first.Status);
        await _db.ExecuteAsMigratorAsync("UPDATE kernel.idempotency_record SET expires_at = now() - interval '1 day' WHERE key = 'rr42-expired'", Ct);

        // The same key with another body: before RR-42 a R-API-04 rejection; now the expired record is removed and the command runs.
        CommandReply second = await _host.PostAsync(Counters, new { companyId = Company, initialValue = 2, sample = "2" }, "rr42-expired", cancellationToken: Ct);

        Assert.Equal(200, second.Status);
        Assert.False(second.Replayed);
        Assert.NotEqual(first.Json!["id"]!.GetValue<Guid>(), second.Json!["id"]!.GetValue<Guid>());
        Assert.Equal(2L, await _host.CountAsync("fixture.counter", Ct));
        Assert.Equal(1L, await _host.ScalarAsync("SELECT count(*) FROM kernel.idempotency_record WHERE key = 'rr42-expired'", Ct));
        Assert.True((bool)(await _host.ScalarAsync("SELECT expires_at > now() FROM kernel.idempotency_record WHERE key = 'rr42-expired'", Ct))!);
        Assert.Contains(_host.Logs.Lines, line => line.Message.Contains("expired idempotency record", StringComparison.Ordinal) && line.Message.Contains("RR-42", StringComparison.Ordinal));
        // A replay of the new result works as for any key (R-API-01).
        CommandReply replay = await _host.PostAsync(Counters, new { companyId = Company, initialValue = 2, sample = "2" }, "rr42-expired", cancellationToken: Ct);
        Assert.True(replay.Replayed);
        Assert.Equal(second.Body, replay.Body);
    }

    [Fact]
    public async Task RR_42_a_412_after_the_expired_record_was_removed_rolls_the_removal_back_with_the_rest()
    {
        Guid id = await CreateCounterAsync("rr42-stale-create");
        CommandReply touched = await _host.PostAsync($"{Counters}/{id}/touch", new { increment = 1 }, "rr42-stale", ifMatch: "\"1\"", cancellationToken: Ct);
        Assert.Equal(200, touched.Status);
        await _db.ExecuteAsMigratorAsync("UPDATE kernel.idempotency_record SET expires_at = now() - interval '1 day' WHERE key = 'rr42-stale'", Ct);

        CommandReply stale = await _host.PostAsync($"{Counters}/{id}/touch", new { increment = 1 }, "rr42-stale", ifMatch: "\"7\"", cancellationToken: Ct);

        Assert.Equal(412, stale.Status);
        // R-API-02: a stale version is not stored, so the whole transaction rolled back, the delete of the expired record included.
        Assert.Equal(1L, await _host.ScalarAsync("SELECT count(*) FROM kernel.idempotency_record WHERE key = 'rr42-stale' AND expires_at < now()", Ct));
        Assert.Equal(2L, await _host.ScalarAsync("SELECT version FROM fixture.counter WHERE id = @id", Ct, ("id", id)));
    }

    // ---- RR-45 -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task RR_45_two_creates_racing_past_the_duplicate_check_end_with_one_item_and_the_mapped_R_MD_05_rejection_stored_for_the_loser()
    {
        TaskCompletionSource paused = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int holds = 0;
        _host.ItemCreateSeam.AfterDuplicateCheck = async (number, cancellationToken) =>
        {
            if (number.Value == "RACE-001" && Interlocked.Exchange(ref holds, 1) == 0)
            {
                paused.TrySetResult();
                await release.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            }
        };

        Task<CommandReply> loser = _host.PostAsync(Items, ItemBody("RACE-001"), "rr45-loser", cancellationToken: Ct);
        await paused.Task.WaitAsync(TimeSpan.FromSeconds(15), Ct);
        CommandReply winner = await _host.PostAsync(Items, ItemBody("RACE-001"), "rr45-winner", cancellationToken: Ct);
        Assert.True(winner.Status == 200, winner.Body);
        release.SetResult();
        CommandReply lost = await loser;

        Assert.Equal(422, lost.Status);
        Assert.Equal(("R-MD-05", "item_number_duplicate"), (lost.RuleId, lost.Code));
        Assert.Contains(_host.Logs.Lines, line => line.Message.Contains("ux_item_item_number", StringComparison.Ordinal) && line.Message.Contains("23505", StringComparison.Ordinal) && line.Message.Contains("RR-45", StringComparison.Ordinal));
        Assert.Equal(1L, await _host.ScalarAsync("SELECT count(*) FROM kernel.item WHERE item_number = 'RACE-001'", Ct));
        // Stored like any other rejection (R-API-03, R-API-01): the replay returns the same rejection.
        CommandReply replay = await _host.PostAsync(Items, ItemBody("RACE-001"), "rr45-loser", cancellationToken: Ct);
        Assert.Equal(422, replay.Status);
        Assert.True(replay.Replayed);
        Assert.Equal("item_number_duplicate", replay.Code);
    }

    // ---- RR-46 -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task RR_46_a_quantity_cost_or_product_outside_its_column_is_400_value_out_of_range_before_anything_is_stored()
    {
        Guid item = await CreateItemAsync("RR46-BOUNDS");
        (string Field, object Line)[] cases =
        [
            ("quantity", Line(1, item, "100000000000000", enteredUnitCost: "1")),
            ("enteredUnitCost", Line(1, item, "1", enteredUnitCost: "1000000000000000000")),
            ("enteredUnitCost", Line(1, item, "10000000000000", enteredUnitCost: "1000")),
        ];
        foreach ((string field, object line) in cases)
        {
            CommandReply reply = await _host.PostAsync(Adjustments, Draft([line]), $"rr46-{field}-{++_sequence}", cancellationToken: Ct);
            Assert.Equal(400, reply.Status);
            Assert.Equal("value_out_of_range", reply.Code);
            Assert.Null(reply.RuleId);
            Assert.Equal(field, reply.Json!["errors"]![0]!["details"]!["field"]!.GetValue<string>());
        }

        CommandReply itemReply = await _host.PostAsync(Items, new { companyId = Company, itemNumber = "RR46-BIG", description = "Big", itemType = "Stocked", baseUnit = "H87", lotTracking = "None", itemPostingGroup = "TRADE_GOODS", taxClass = "Standard", costingMethod = "MovingAverage", salesMultiple = "100000000000000" }, "rr46-item", cancellationToken: Ct);
        Assert.Equal(400, itemReply.Status);
        Assert.Equal("value_out_of_range", itemReply.Code);

        // A value at the edge is accepted: 99,999,999,999,999 x 100.00 is below 10^16 (numeric(20,4)).
        CommandReply edge = await _host.PostAsync(Adjustments, Draft([Line(1, item, "99999999999999", enteredUnitCost: "100.00")]), "rr46-edge", cancellationToken: Ct);
        Assert.True(edge.Status == 200, edge.Body);
        Assert.Equal(0L, await _host.ScalarAsync("SELECT count(*) FROM kernel.idempotency_record WHERE key LIKE 'rr46-quantity%' OR key LIKE 'rr46-enteredUnitCost%' OR key = 'rr46-item'", Ct));
        Assert.Equal(1L, await _host.ScalarAsync("SELECT count(*) FROM kernel.document", Ct));
    }

    [Fact]
    public async Task RR_46_a_numeric_overflow_found_at_posting_is_422_amount_out_of_range_under_R_DOC_01_with_nothing_posted_and_the_rejection_stored()
    {
        // Two receipts worth 9.9 x 10^15 each pass the draft bounds; their sum overflows the cost state's numeric(20,4).
        Guid item = await CreateItemAsync("RR46-OVERFLOW");
        Guid firstDraft = await DraftAsync("rr46-first-create", [Line(1, item, "99000000000000", enteredUnitCost: "99.99")]);
        CommandReply first = await PostAsync(firstDraft, "rr46-first-post");
        Assert.True(first.Status == 200, first.Body);
        Guid secondDraft = await DraftAsync("rr46-second-create", [Line(1, item, "99000000000000", enteredUnitCost: "99.99")]);

        CommandReply second = await PostAsync(secondDraft, "rr46-second-post");

        Assert.Equal(422, second.Status);
        Assert.Equal(("R-DOC-01", "amount_out_of_range"), (second.RuleId, second.Code));
        Assert.Equal("Draft", await _host.ScalarAsync("SELECT status FROM kernel.document WHERE id = @id", Ct, ("id", secondDraft)));
        Assert.Equal(1L, await _host.ScalarAsync("SELECT count(*) FROM kernel.journal_entry", Ct));
        Assert.Equal(99000000000000m, await _host.ScalarAsync("SELECT quantity_base FROM kernel.item_cost_state WHERE item_id = @item", Ct, ("item", item)));
        Assert.Contains(_host.Logs.Lines, line => line.Message.Contains("22003", StringComparison.Ordinal) && line.Message.Contains("RR-46", StringComparison.Ordinal));
        CommandReply replay = await PostAsync(secondDraft, "rr46-second-post");
        Assert.True(replay.Replayed);
        Assert.Equal("amount_out_of_range", replay.Code);
    }

    // ---- RR-51 -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task RR_51_the_user_is_resolved_before_the_validators_so_an_unknown_subject_gets_403_for_an_invalid_body_and_a_known_one_400()
    {
        CommandReply unknown = await _host.PostAsync(Counters, new { companyId = Company, initialValue = 1, sample = "-1" }, "rr51-unknown", subject: "nobody-" + Guid.NewGuid().ToString("N"), cancellationToken: Ct);
        CommandReply known = await _host.PostAsync(Counters, new { companyId = Company, initialValue = 1, sample = "-1" }, "rr51-known", subject: TestUsers.PreparerSubject, cancellationToken: Ct);

        Assert.Equal(403, unknown.Status);
        Assert.Equal("subject_unknown", unknown.Code);
        Assert.Equal(400, known.Status);
        Assert.Equal("sample_negative", known.Code);
        // Neither outcome is stored (R-API-03).
        Assert.Equal(0L, await _host.ScalarAsync("SELECT count(*) FROM kernel.idempotency_record WHERE key LIKE 'rr51-%'", Ct));
    }

    // ---- RR-53 -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task RR_53_a_correlation_id_outside_the_allowed_form_is_replaced_and_the_replacement_logged_at_warning_while_a_valid_one_is_kept()
    {
        CommandReply spaces = await _host.PostAsync(Counters, new { companyId = Company, initialValue = 1, sample = "1" }, "rr53-spaces", correlationId: "bad id with spaces", cancellationToken: Ct);
        CommandReply tooLong = await _host.PostAsync(Counters, new { companyId = Company, initialValue = 1, sample = "1" }, "rr53-long", correlationId: new string('a', 129), cancellationToken: Ct);
        CommandReply kept = await _host.PostAsync(Counters, new { companyId = Company, initialValue = 1, sample = "1" }, "rr53-kept", correlationId: "erp-sync:2026-10-19_0001.A", cancellationToken: Ct);

        Assert.Equal(200, spaces.Status);
        Assert.NotEqual("bad id with spaces", spaces.CorrelationId);
        Assert.Matches("^[A-Za-z0-9._:-]{1,128}$", spaces.CorrelationId!);
        Assert.NotEqual(new string('a', 129), tooLong.CorrelationId);
        Assert.Equal("erp-sync:2026-10-19_0001.A", kept.CorrelationId);
        Assert.Equal(2, _host.Logs.Lines.Count(line => line.Level == LogLevel.Warning && line.Message.Contains("X-Correlation-Id header was refused", StringComparison.Ordinal) && line.Message.Contains("RR-53", StringComparison.Ordinal)));
        // The audit event carries the id as the command saw it, never the refused value (D-51: the command id is the trusted link).
        Assert.Equal(0L, await _host.ScalarAsync("SELECT count(*) FROM kernel.audit_event WHERE correlation_id = 'bad id with spaces'", Ct));
        Assert.Equal(1L, await _host.ScalarAsync("SELECT count(*) FROM kernel.audit_event WHERE correlation_id = 'erp-sync:2026-10-19_0001.A'", Ct));
    }

    // ---- RR-56 -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task RR_56_a_query_string_key_given_twice_is_400_query_parameter_repeated_and_given_once_it_binds()
    {
        CommandReply repeated = await _host.GetAsync($"/v1/kernel/companies/{Company}?id={Company}&id={Guid.NewGuid()}", cancellationToken: Ct);
        CommandReply once = await _host.GetAsync($"/v1/kernel/companies/{Company}?id={Company}", cancellationToken: Ct);

        Assert.Equal(400, repeated.Status);
        Assert.Equal("query_parameter_repeated", repeated.Code);
        Assert.Equal(200, once.Status);
    }

    // ---- helpers -----------------------------------------------------------------------------------------------------

    private async Task<Guid> CreateCounterAsync(string key)
    {
        CommandReply reply = await _host.PostAsync(Counters, new { companyId = Company, initialValue = 5, sample = "1.5" }, key, cancellationToken: Ct);
        Assert.True(reply.Status == 200, reply.Body);
        return reply.Json!["id"]!.GetValue<Guid>();
    }

    private static object ItemBody(string number) =>
        new { companyId = Company, itemNumber = number, description = $"Item {number}", itemType = "Stocked", baseUnit = "H87", lotTracking = "None", itemPostingGroup = "TRADE_GOODS", taxClass = "Standard", costingMethod = "MovingAverage" };

    private async Task<Guid> CreateItemAsync(string number)
    {
        CommandReply reply = await _host.PostAsync(Items, ItemBody(number), "item-" + number, cancellationToken: Ct);
        Assert.True(reply.Status == 200, reply.Body);
        return reply.Json!["id"]!.GetValue<Guid>();
    }

    private static object Line(int number, Guid item, string quantity, string? enteredUnitCost = null) =>
        new { lineNumber = number, itemId = item, quantity, warehouseId = Warehouse, stockStatus = "Available", enteredUnitCost };

    private static object Draft(object[] lines) => new { companyId = Company, documentDate = June, reasonCode = "CountDifference", warehouseId = Warehouse, postingDate = June, lines };

    private async Task<Guid> DraftAsync(string key, object[] lines)
    {
        CommandReply reply = await _host.PostAsync(Adjustments, Draft(lines), key, cancellationToken: Ct);
        Assert.True(reply.Status == 200, reply.Body);
        return reply.Json!["id"]!.GetValue<Guid>();
    }

    private Task<CommandReply> PostAsync(Guid id, string key) => _host.PostAsync($"{Adjustments}/{id}/post", null, key, ifMatch: "\"1\"", cancellationToken: Ct);
}

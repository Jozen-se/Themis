using Microsoft.Extensions.Logging;
using Themis.Integration.Tests.Pipeline;
using Themis.Kernel.Messaging;
using Themis.Kernel.Organisation;

namespace Themis.Integration.Tests.Messaging;

/// <summary>
/// The outbox relay's pass end to end (step 9 brief; D-53): a posting's INVENTORY_ADJUSTED is published with
/// attempts 1 and one log line carrying the envelope and never the payload; every event type of the milestone goes
/// through the same mechanism oldest first; two passes never hold the same event at the same time (FOR UPDATE SKIP
/// LOCKED); a pass that fails after its claim leaves the events unpublished with attempts unchanged and the age
/// gauge updated; a rolled-back command leaves no event. The tests call the pass directly; the host's loop is off.
/// </summary>
[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Integration")]
public sealed class OutboxRelayTests : IAsyncLifetime
{
    private const string Adjustments = "/v1/kernel/inventory-adjustments";
    private const string Counters = "/v1/fixture/counters";

    private static readonly Guid Company = Milestone1OrganisationFixture.CompanyId;
    private static readonly Guid Warehouse = Milestone1OrganisationFixture.WarehouseId;

    private readonly PostgresFixture _db;
    private PipelineTestHost _host = null!;
    private int _sequence;

    public OutboxRelayTests(PostgresFixture db)
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
        _host.RelaySeam.AfterClaim = null;
        await _db.TruncateAsync(CancellationToken.None);
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task D_53_a_pass_marks_a_posting_INVENTORY_ADJUSTED_published_with_attempts_1_and_logs_one_line_with_the_envelope_and_no_payload()
    {
        Guid item = await CreateItemAsync();
        Guid id = await DraftAsync(item, "10", "1.00");
        CommandReply posted = await _host.PostAsync($"{Adjustments}/{id}/post", null, Key("post"), ifMatch: "\"1\"", correlationId: "corr-relay-post", cancellationToken: Ct);
        Assert.Equal(200, posted.Status);
        string documentNumber = posted.Json!["documentNumber"]!.GetValue<string>();
        Guid eventId = (Guid)(await _host.ScalarAsync("SELECT id FROM kernel.outbox_event WHERE event_type = 'INVENTORY_ADJUSTED' AND aggregate_id = @id", Ct, ("id", id)))!;
        // The item's events (D-44: one per master data row ITEM.CREATE changes), the draft's and the posting's.
        long pending = await PendingAsync();
        Assert.Equal(1L, await _host.ScalarAsync("SELECT count(*) FROM kernel.outbox_event WHERE event_type = 'INVENTORY_ADJUSTED'", Ct));

        int published = await _host.Relay.PublishPendingAsync(Ct);
        int again = await _host.Relay.PublishPendingAsync(Ct);

        Assert.Equal((int)pending, published);
        Assert.Equal(0, again);
        Assert.Equal(0L, await PendingAsync());
        Assert.Equal(pending, _host.Published);
        Assert.NotNull(_host.OldestPendingAge);
        Assert.Equal(1, await _host.ScalarAsync("SELECT attempts FROM kernel.outbox_event WHERE id = @id", Ct, ("id", eventId)));
        Assert.NotNull(await _host.ScalarAsync("SELECT published_at FROM kernel.outbox_event WHERE id = @id", Ct, ("id", eventId)));
        // The rest of the row is untouched: the kernel role may write the two delivery columns only.
        Assert.Equal(id.ToString(), await _host.ScalarAsync("SELECT payload->>'id' FROM kernel.outbox_event WHERE id = @id", Ct, ("id", eventId)));

        LogLine line = Assert.Single(RelayLines(), line => line.Message.Contains(eventId.ToString(), StringComparison.OrdinalIgnoreCase));
        Assert.Equal(LogLevel.Information, line.Level);
        foreach (string field in new[] { "INVENTORY_ADJUSTED", "v1", "INVENTORY_ADJUSTMENT", id.ToString(), Company.ToString(), "INVENTORY_ADJUSTMENT.POST", "corr-relay-post", "attempts 1" })
        {
            Assert.Contains(field, line.Message, StringComparison.OrdinalIgnoreCase);
        }

        // Build Brief, Observability: the event's command name and correlation id as scopes, no user, no payload.
        Assert.Equal("INVENTORY_ADJUSTMENT.POST", line.Scopes["CommandName"]);
        Assert.Equal("corr-relay-post", line.Scopes["CorrelationId"]);
        Assert.False(line.Scopes.ContainsKey("UserId"));
        Assert.DoesNotContain(documentNumber, line.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("changes", line.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(TestUsers.Clerk.ToString(), line.Message, StringComparison.OrdinalIgnoreCase);
        // D-53: the relay writes no audit event and no outbox event of its own.
        Assert.Equal(0L, await _host.ScalarAsync("SELECT count(*) FROM kernel.audit_event WHERE command_name NOT LIKE 'SYSTEM.%' AND command_name NOT IN ('ITEM.CREATE', 'INVENTORY_ADJUSTMENT.CREATE', 'INVENTORY_ADJUSTMENT.POST')", Ct));
        Assert.Equal(pending, await _host.CountAsync("kernel.outbox_event", Ct));
    }

    [Fact]
    public async Task D_44_every_event_type_of_the_milestone_is_published_by_the_same_pass_oldest_first()
    {
        Guid item = await CreateItemAsync();
        Guid first = await DraftAsync(item, "5", "2.00");
        CommandReply updated = await _host.PostAsync($"{Adjustments}/{first}/update", new { documentDate = "2026-06-15", reasonCode = "CountDifference", warehouseId = Warehouse, postingDate = "2026-06-15", lines = new[] { Line(1, item, "6", "2.00") } }, Key("update"), ifMatch: "\"1\"", cancellationToken: Ct);
        Guid second = await DraftAsync(item, "1", "1.00");
        CommandReply discarded = await _host.PostAsync($"{Adjustments}/{second}/discard", null, Key("discard"), ifMatch: "\"1\"", cancellationToken: Ct);
        CommandReply posted = await _host.PostAsync($"{Adjustments}/{first}/post", null, Key("post"), ifMatch: "\"2\"", cancellationToken: Ct);
        Assert.Equal((200, 200, 200), (updated.Status, discarded.Status, posted.Status));

        long pending = await PendingAsync();

        int published = await _host.Relay.PublishPendingAsync(Ct);

        Assert.Equal((int)pending, published);
        Assert.Equal(0L, await PendingAsync());
        string[] types = ((string)(await _host.ScalarAsync("SELECT string_agg(DISTINCT event_type, ',' ORDER BY event_type) FROM kernel.outbox_event WHERE published_at IS NOT NULL AND attempts = 1", Ct))!).Split(',');
        Assert.Superset(new HashSet<string> { "ITEM_CREATED", "INVENTORY_ADJUSTMENT_CREATED", "INVENTORY_ADJUSTMENT_UPDATED", "INVENTORY_ADJUSTMENT_DISCARDED", "INVENTORY_ADJUSTED" }, types.ToHashSet(StringComparer.Ordinal));
        Assert.Equal(types.Length, (int)(long)(await _host.ScalarAsync("SELECT count(DISTINCT event_type) FROM kernel.outbox_event", Ct))!);
        // Oldest first: the log lines follow occurred_at, then id.
        string expectedOrder = (string)(await _host.ScalarAsync("SELECT string_agg(id::text, ',' ORDER BY occurred_at, id) FROM kernel.outbox_event", Ct))!;
        string loggedOrder = string.Join(",", RelayLines().Select(line => line.Message.Split(' ')[2]));
        Assert.Equal(expectedOrder, loggedOrder, ignoreCase: true);
    }

    [Fact]
    public async Task D_53_two_concurrent_passes_never_hold_the_same_event_at_the_same_time_and_together_publish_each_event_once()
    {
        Guid item = await CreateItemAsync();
        long itemEvents = await PendingAsync();
        for (int draft = 0; draft < 6 - itemEvents; draft++)
        {
            await DraftAsync(item, "1", "1.00");
        }

        Assert.Equal(6L, await PendingAsync());
        TaskCompletionSource<IReadOnlyList<Guid>> firstClaimed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        _host.RelaySeam.AfterClaim = async (ids, cancellationToken) =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                firstClaimed.SetResult(ids);
                await release.Task.WaitAsync(cancellationToken);
            }
        };

        Task<int> firstPass = _host.Relay.PublishPendingAsync(3, Ct);
        IReadOnlyList<Guid> held = await firstClaimed.Task.WaitAsync(TimeSpan.FromSeconds(15), Ct);
        // The second pass runs while the first holds its three events: it takes only the other three and finishes without waiting.
        int secondPass = await _host.Relay.PublishPendingAsync(3, Ct).WaitAsync(TimeSpan.FromSeconds(15), Ct);
        Assert.Equal(3, held.Count);
        Assert.Equal(3, secondPass);
        Assert.Equal(3L, await PendingAsync());
        Assert.Equal(3L, await _host.ScalarAsync("SELECT count(*) FROM kernel.outbox_event WHERE published_at IS NULL AND id = ANY(@ids)", Ct, ("ids", held.ToArray())));
        release.SetResult();
        Assert.Equal(3, await firstPass.WaitAsync(TimeSpan.FromSeconds(15), Ct));

        Assert.Equal(0L, await PendingAsync());
        Assert.Equal(6L, await _host.ScalarAsync("SELECT count(*) FROM kernel.outbox_event WHERE attempts = 1", Ct));
        Assert.Equal(6, RelayLines().Count);
        Assert.Equal(6, RelayLines().Select(line => line.Message.Split(' ')[2]).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(6L, _host.Published);
    }

    [Fact]
    public async Task D_53_a_pass_that_fails_after_its_claim_leaves_the_events_unpublished_with_attempts_unchanged_and_the_next_pass_publishes_them()
    {
        Guid item = await CreateItemAsync();
        await DraftAsync(item, "1", "1.00");
        long pending = await PendingAsync();
        _host.RelaySeam.AfterClaim = (_, _) => throw new InvalidOperationException("Injected failure after the claim (test seam).");

        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(() => _host.Relay.PublishPendingAsync(Ct));

        Assert.Contains("test seam", failure.Message, StringComparison.Ordinal);
        Assert.Equal(pending, await PendingAsync());
        Assert.Equal(0L, await _host.ScalarAsync("SELECT count(*) FROM kernel.outbox_event WHERE attempts <> 0", Ct));
        Assert.NotNull(_host.OldestPendingAge);
        Assert.Empty(RelayLines());
        Assert.Equal(0L, _host.Published);

        _host.RelaySeam.AfterClaim = null;
        Assert.Equal((int)pending, await _host.Relay.PublishPendingAsync(Ct));
        Assert.Equal(0L, await PendingAsync());
        Assert.Equal(pending, await _host.ScalarAsync("SELECT count(*) FROM kernel.outbox_event WHERE attempts = 1", Ct));
        Assert.Equal((int)pending, RelayLines().Count);
    }

    [Fact]
    public async Task R_LED_05_an_event_of_a_command_that_rolled_back_never_exists_for_the_relay()
    {
        CommandReply created = await _host.PostAsync(Counters, new { companyId = Company, initialValue = 1, sample = "1" }, Key("counter"), cancellationToken: Ct);
        Assert.Equal(200, created.Status);
        Guid counter = created.Json!["id"]!.GetValue<Guid>();

        CommandReply failed = await _host.PostAsync($"{Counters}/{counter}/touch", new { increment = 1, failure = "ThrowAfterWrite" }, Key("touch"), ifMatch: "\"1\"", cancellationToken: Ct);
        int published = await _host.Relay.PublishPendingAsync(Ct);

        Assert.Equal(500, failed.Status);
        Assert.Equal(1, published);
        Assert.Equal(0L, await _host.ScalarAsync("SELECT count(*) FROM kernel.outbox_event WHERE event_type = 'FIXTURE_COUNTER_TOUCHED'", Ct));
        Assert.Equal("FIXTURE_COUNTER_CREATED", await _host.ScalarAsync("SELECT event_type FROM kernel.outbox_event WHERE published_at IS NOT NULL", Ct));
        Assert.Single(RelayLines());
    }

    private string Key(string purpose) => $"relay-{purpose}-{++_sequence}";

    private async Task<Guid> CreateItemAsync()
    {
        string number = $"RELAY-{++_sequence}";
        CommandReply reply = await _host.PostAsync("/v1/kernel/items", new { companyId = Company, itemNumber = number, description = $"Item {number}", itemType = "Stocked", baseUnit = "H87", lotTracking = "None", itemPostingGroup = "TRADE_GOODS", taxClass = "Standard", costingMethod = "MovingAverage" }, Key("item"), cancellationToken: Ct);
        Assert.True(reply.Status == 200, reply.Body);
        return reply.Json!["id"]!.GetValue<Guid>();
    }

    private static object Line(int number, Guid item, string quantity, string? enteredUnitCost) =>
        new { lineNumber = number, itemId = item, quantity, warehouseId = (Guid?)null, stockStatus = "Available", enteredUnitCost };

    private async Task<Guid> DraftAsync(Guid item, string quantity, string enteredUnitCost)
    {
        CommandReply reply = await _host.PostAsync(Adjustments, new { companyId = Company, documentDate = "2026-06-15", reasonCode = "CountDifference", warehouseId = Warehouse, postingDate = "2026-06-15", lines = new[] { Line(1, item, quantity, enteredUnitCost) } }, Key("draft"), cancellationToken: Ct);
        Assert.True(reply.Status == 200, reply.Body);
        return reply.Json!["id"]!.GetValue<Guid>();
    }

    private async Task<long> PendingAsync() => (long)(await _host.ScalarAsync("SELECT count(*) FROM kernel.outbox_event WHERE published_at IS NULL", Ct))!;

    private IReadOnlyList<LogLine> RelayLines() => [.. _host.Logs.Lines.Where(line => line.Category == typeof(OutboxRelayPass).FullName && line.Message.StartsWith("Outbox event ", StringComparison.Ordinal))];
}

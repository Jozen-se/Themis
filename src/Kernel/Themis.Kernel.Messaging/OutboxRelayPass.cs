using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Themis.Kernel.Contracts.Persistence;

namespace Themis.Kernel.Messaging;

/// <summary>One relay pass: publish the pending outbox events, up to the batch size, and return how many were published.</summary>
internal interface IOutboxRelay
{
    Task<int> PublishPendingAsync(CancellationToken cancellationToken);
}

/// <summary>An unpublished outbox event as the pass claims it: the D-50 envelope and the delivery counter, never the payload.</summary>
internal sealed record PendingOutboxEvent(Guid Id, string EventType, int SchemaVersion, string AggregateType, Guid AggregateId, Guid? CompanyId, string CommandName, string CorrelationId, DateTimeOffset OccurredAt, int Attempts);

/// <summary>
/// The outbox relay's pass (step 9 brief; D-53): one short transaction on its own connection through the unit of
/// work as themis_kernel (R-DB-01), not a command: no permission, Idempotency-Key, If-Match, audit event or outbox
/// event of its own. It records the age of the oldest unpublished event, claims the oldest unpublished events with
/// <c>FOR UPDATE SKIP LOCKED</c> so that two passes never hold the same event at the same time, logs each one with
/// its envelope (D-50; Build Brief, Observability), sets <c>published_at</c> to the pass's UTC time (R-TIME-01) and
/// <c>attempts</c> to <c>attempts + 1</c>, the only two columns the kernel role may update, and commits. A pass that
/// fails rolls back its updates with it; the next pass handles the events again, so delivery is at least once and
/// consumers must be idempotent (D-53). Nothing leaves the system in step 9.
/// </summary>
internal sealed partial class OutboxRelayPass : IOutboxRelay
{
    private const string OldestPendingSql = "SELECT min(occurred_at) FROM kernel.outbox_event WHERE published_at IS NULL";

    private const string ClaimSql =
        """
        SELECT id, event_type, schema_version, aggregate_type, aggregate_id, company_id, command_name, correlation_id, occurred_at, attempts
        FROM kernel.outbox_event
        WHERE published_at IS NULL
        ORDER BY occurred_at, id
        LIMIT @batch
        FOR UPDATE SKIP LOCKED
        """;

    private const string PublishSql = "UPDATE kernel.outbox_event SET published_at = @now, attempts = attempts + 1 WHERE id = @id";

    private readonly IUnitOfWorkFactory _unitOfWorks;
    private readonly TimeProvider _time;
    private readonly IOptions<OutboxRelayOptions> _options;
    private readonly OutboxRelayTelemetry _telemetry;
    private readonly IOutboxRelaySeam _seam;
    private readonly ILogger<OutboxRelayPass> _logger;

    public OutboxRelayPass(IUnitOfWorkFactory unitOfWorks, TimeProvider time, IOptions<OutboxRelayOptions> options, OutboxRelayTelemetry telemetry, IOutboxRelaySeam seam, ILogger<OutboxRelayPass> logger)
    {
        _unitOfWorks = unitOfWorks;
        _time = time;
        _options = options;
        _telemetry = telemetry;
        _seam = seam;
        _logger = logger;
    }

    public Task<int> PublishPendingAsync(CancellationToken cancellationToken) => PublishPendingAsync(_options.Value.BatchSize, cancellationToken);

    /// <summary>One pass with an explicit batch size; the background loop uses the configured one, the tests their own.</summary>
    public async Task<int> PublishPendingAsync(int batchSize, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(batchSize, 0);
        await using IKernelUnitOfWork unitOfWork = await _unitOfWorks.BeginAsync(cancellationToken);
        DateTimeOffset now = _time.GetUtcNow();

        // The gauge first, before claiming, so that a failing pass still reports how far behind the relay is.
        object? oldest = await unitOfWork.ScalarAsync(DatabaseRole.Kernel, OldestPendingSql, null, cancellationToken);
        _telemetry.OldestPendingAge.Record(AgeInSeconds(oldest, now));

        IReadOnlyList<PendingOutboxEvent> claimed = await unitOfWork.QueryAsync(DatabaseRole.Kernel, ClaimSql, new Dictionary<string, object?> { ["batch"] = batchSize },
            reader => new PendingOutboxEvent(
                reader.GetGuid(0), reader.GetString(1), reader.GetInt32(2), reader.GetString(3), reader.GetGuid(4),
                reader.IsDBNull(5) ? null : reader.GetGuid(5), reader.GetString(6), reader.GetString(7), reader.GetFieldValue<DateTimeOffset>(8), reader.GetInt32(9)),
            cancellationToken);
        await _seam.AfterClaimAsync([.. claimed.Select(pending => pending.Id)], cancellationToken);

        foreach (PendingOutboxEvent pending in claimed)
        {
            // Build Brief, Observability: the event's command name and correlation id, no user, never the payload.
            using IDisposable? scope = _logger.BeginScope(new Dictionary<string, object> { ["CommandName"] = pending.CommandName, ["CorrelationId"] = pending.CorrelationId });
            LogPublished(pending.Id, pending.EventType, pending.SchemaVersion, pending.AggregateType, pending.AggregateId, pending.CompanyId, pending.CommandName, pending.CorrelationId, pending.OccurredAt, pending.Attempts + 1);
            int updated = await unitOfWork.ExecuteAsync(DatabaseRole.Kernel, PublishSql, new Dictionary<string, object?> { ["now"] = now, ["id"] = pending.Id }, cancellationToken);
            if (updated != 1)
            {
                throw new InvalidOperationException($"Outbox event {pending.Id} was claimed but not updated ({updated} rows).");
            }
        }

        await unitOfWork.CommitAsync(cancellationToken);
        if (claimed.Count > 0)
        {
            _telemetry.Published.Add(claimed.Count);
        }

        return claimed.Count;
    }

    /// <summary>Whole seconds between the oldest unpublished event and now; 0 when none is pending or the clock ran backwards.</summary>
    private static long AgeInSeconds(object? oldest, DateTimeOffset now)
    {
        DateTimeOffset? occurredAt = oldest switch
        {
            DateTimeOffset value => value,
            DateTime value => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc), TimeSpan.Zero),
            _ => null,
        };
        return occurredAt is { } at ? Math.Max(0L, (now - at).Ticks / TimeSpan.TicksPerSecond) : 0L;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Outbox event {EventId} {EventType} v{SchemaVersion} of {AggregateType} {AggregateId} (company {CompanyId}) from {CommandName} published; correlation {CorrelationId}, occurred at {OccurredAt}, attempts {Attempts} (D-53).")]
    private partial void LogPublished(Guid eventId, string eventType, int schemaVersion, string aggregateType, Guid aggregateId, Guid? companyId, string commandName, string correlationId, DateTimeOffset occurredAt, int attempts);
}

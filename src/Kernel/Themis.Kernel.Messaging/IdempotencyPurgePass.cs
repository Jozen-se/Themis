using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Themis.Kernel.Contracts.Persistence;

namespace Themis.Kernel.Messaging;

internal interface IIdempotencyPurge
{
    /// <summary>Deletes one batch of expired idempotency records and returns how many went; a full batch means more may wait.</summary>
    Task<int> PurgeExpiredAsync(CancellationToken cancellationToken);
}

/// <summary>
/// The purge of expired idempotency records (RR-42; R-AUD-01 as amended by D-58): one short transaction on its own
/// connection through the unit of work as themis_kernel, which holds DELETE on the table for exactly this (privilege
/// matrix). It is not a command: no permission, Idempotency-Key, audit event or outbox event; its log line and the
/// counter themis.idempotency.purged are the record. The delete is bounded to one batch so it never holds a long lock,
/// and it only removes records whose expiry has passed, so it never conflicts with the dispatcher's own removal of an
/// expired record under the advisory lock (RR-42): whichever runs first deletes, the other deletes nothing.
/// </summary>
internal sealed partial class IdempotencyPurgePass : IIdempotencyPurge
{
    private const string PurgeSql =
        """
        DELETE FROM kernel.idempotency_record
        WHERE ctid IN (SELECT ctid FROM kernel.idempotency_record WHERE expires_at <= @now LIMIT @batch)
        """;

    private readonly IUnitOfWorkFactory _unitOfWorks;
    private readonly TimeProvider _time;
    private readonly IOptions<IdempotencyPurgeOptions> _options;
    private readonly IdempotencyPurgeTelemetry _telemetry;
    private readonly ILogger<IdempotencyPurgePass> _logger;

    public IdempotencyPurgePass(IUnitOfWorkFactory unitOfWorks, TimeProvider time, IOptions<IdempotencyPurgeOptions> options, IdempotencyPurgeTelemetry telemetry, ILogger<IdempotencyPurgePass> logger)
    {
        _unitOfWorks = unitOfWorks;
        _time = time;
        _options = options;
        _telemetry = telemetry;
        _logger = logger;
    }

    public Task<int> PurgeExpiredAsync(CancellationToken cancellationToken) => PurgeExpiredAsync(_options.Value.BatchSize, cancellationToken);

    public async Task<int> PurgeExpiredAsync(int batchSize, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(batchSize, 0);
        await using IKernelUnitOfWork unitOfWork = await _unitOfWorks.BeginAsync(cancellationToken);
        DateTimeOffset now = _time.GetUtcNow();
        int purged = await unitOfWork.ExecuteAsync(DatabaseRole.Kernel, PurgeSql, new Dictionary<string, object?> { ["now"] = now, ["batch"] = batchSize }, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        if (purged > 0)
        {
            _telemetry.Purged.Add(purged);
            LogPurged(purged, now);
        }

        return purged;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Idempotency purge removed {Purged} expired record(s) as of {Now} (RR-42, D-58).")]
    private partial void LogPurged(int purged, DateTimeOffset now);
}

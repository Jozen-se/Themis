using Microsoft.Extensions.Options;
using Themis.Kernel.Messaging;

namespace Themis.Host.Messaging;

/// <summary>
/// The background loop that runs the idempotency purge pass (milestone 2 step 2; RR-42, D-58), shaped like the outbox
/// relay's loop (D-53): after a full batch the next pass runs at once; after a partial or empty batch, or a failed
/// pass, the loop waits <c>Idempotency:Purge:Interval</c>. A failed pass is logged and the next pass runs; cancellation
/// ends the loop between passes, and a pass in flight rolls back with its transaction. With
/// <c>Idempotency:Purge:Enabled</c> false the loop logs that and ends; expired records then go only one at a time, when
/// their key is used again (RR-42).
/// </summary>
internal sealed partial class IdempotencyPurgeHostedService : BackgroundService
{
    private readonly IIdempotencyPurge _purge;
    private readonly IOptions<IdempotencyPurgeOptions> _options;
    private readonly TimeProvider _time;
    private readonly ILogger<IdempotencyPurgeHostedService> _logger;

    public IdempotencyPurgeHostedService(IIdempotencyPurge purge, IOptions<IdempotencyPurgeOptions> options, TimeProvider time, ILogger<IdempotencyPurgeHostedService> logger)
    {
        _purge = purge;
        _options = options;
        _time = time;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        IdempotencyPurgeOptions options = _options.Value;
        if (!options.Enabled)
        {
            LogDisabled();
            return;
        }

        LogStarted(options.Interval, options.BatchSize);
        while (!stoppingToken.IsCancellationRequested)
        {
            int purged;
            try
            {
                purged = await _purge.PurgeExpiredAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // The loop survives any failure of a pass; the next pass retries (D-53 pattern).
                LogPassFailed(exception, options.Interval);
                purged = -1;
            }

            if (purged == options.BatchSize)
            {
                continue;
            }

            try
            {
                await Task.Delay(options.Interval, _time, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        LogStopped();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Idempotency purge disabled by Idempotency:Purge:Enabled; expired records are removed only when their key is used again (RR-42).")]
    private partial void LogDisabled();

    [LoggerMessage(Level = LogLevel.Information, Message = "Idempotency purge started: a pass every {Interval} while less than {BatchSize} expired records are found, at once after a full batch (RR-42, D-58).")]
    private partial void LogStarted(TimeSpan interval, int batchSize);

    [LoggerMessage(Level = LogLevel.Error, Message = "Idempotency purge pass failed; its delete rolled back and the next pass runs after {Interval} (RR-42, D-58).")]
    private partial void LogPassFailed(Exception exception, TimeSpan interval);

    [LoggerMessage(Level = LogLevel.Information, Message = "Idempotency purge stopped.")]
    private partial void LogStopped();
}

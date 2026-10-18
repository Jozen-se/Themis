using Microsoft.Extensions.Options;
using Themis.Kernel.Messaging;

namespace Themis.Host.Messaging;

/// <summary>
/// The background loop that runs the outbox relay's pass (step 9 brief; D-53). After a full batch the next pass
/// runs at once; after a partial or empty batch, or a failed pass, the loop waits <c>Outbox:Relay:PollInterval</c>.
/// A failed pass is logged and retried by the next pass; the relay never uses the command pipeline's whole-command
/// retry (D-41 is about commands). Cancellation ends the loop between passes; a pass in flight rolls back with its
/// transaction. With <c>Outbox:Relay:Enabled</c> false the loop logs that and ends.
/// </summary>
internal sealed partial class OutboxRelayHostedService : BackgroundService
{
    private readonly IOutboxRelay _relay;
    private readonly IOptions<OutboxRelayOptions> _options;
    private readonly TimeProvider _time;
    private readonly ILogger<OutboxRelayHostedService> _logger;

    public OutboxRelayHostedService(IOutboxRelay relay, IOptions<OutboxRelayOptions> options, TimeProvider time, ILogger<OutboxRelayHostedService> logger)
    {
        _relay = relay;
        _options = options;
        _time = time;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        OutboxRelayOptions options = _options.Value;
        if (!options.Enabled)
        {
            LogDisabled();
            return;
        }

        LogStarted(options.PollInterval, options.BatchSize);
        while (!stoppingToken.IsCancellationRequested)
        {
            int published;
            try
            {
                published = await _relay.PublishPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // The loop survives any failure of a pass; the next pass retries (D-53).
                LogPassFailed(exception, options.PollInterval);
                published = -1;
            }

            if (published == options.BatchSize)
            {
                continue;
            }

            try
            {
                await Task.Delay(options.PollInterval, _time, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        LogStopped();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Outbox relay disabled by Outbox:Relay:Enabled; events stay unpublished until a relay runs.")]
    private partial void LogDisabled();

    [LoggerMessage(Level = LogLevel.Information, Message = "Outbox relay started: a pass every {PollInterval} while less than {BatchSize} events are pending, at once after a full batch (D-53).")]
    private partial void LogStarted(TimeSpan pollInterval, int batchSize);

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox relay pass failed; its updates rolled back and the next pass runs after {PollInterval} (D-53).")]
    private partial void LogPassFailed(Exception exception, TimeSpan pollInterval);

    [LoggerMessage(Level = LogLevel.Information, Message = "Outbox relay stopped.")]
    private partial void LogStopped();
}

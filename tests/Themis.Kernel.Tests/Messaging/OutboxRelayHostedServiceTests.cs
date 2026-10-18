using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Themis.Host.Messaging;
using Themis.Kernel.Messaging;

namespace Themis.Kernel.Tests.Messaging;

/// <summary>
/// The host's relay loop (step 9 brief): after a full batch the next pass runs at once, after a partial batch it
/// waits the poll interval, cancellation ends the wait, and a disabled relay runs no pass.
/// </summary>
[Trait("Category", "Unit")]
public sealed class OutboxRelayHostedServiceTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Full_batches_run_back_to_back_a_partial_batch_waits_the_poll_interval_and_stopping_ends_the_wait()
    {
        // With an hour between passes, three calls within seconds prove that the two full batches did not wait.
        ScriptedRelay relay = new([10, 10, 3]);
        using OutboxRelayHostedService service = new(relay, Options.Create(new OutboxRelayOptions { BatchSize = 10, PollInterval = TimeSpan.FromHours(1) }), TimeProvider.System, NullLogger<OutboxRelayHostedService>.Instance);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await relay.ThirdCall.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken).WaitAsync(Timeout, TestContext.Current.CancellationToken);

        Assert.Equal(3, relay.Calls);
        Assert.True(service.ExecuteTask!.IsCompleted);
    }

    [Fact]
    public async Task A_disabled_relay_runs_no_pass_and_the_loop_ends_at_once()
    {
        ScriptedRelay relay = new([1]);
        using OutboxRelayHostedService service = new(relay, Options.Create(new OutboxRelayOptions { Enabled = false }), TimeProvider.System, NullLogger<OutboxRelayHostedService>.Instance);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await service.ExecuteTask!.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, relay.Calls);
    }

    [Fact]
    public async Task A_failing_pass_is_logged_and_the_loop_goes_on_after_the_poll_interval()
    {
        ScriptedRelay relay = new([-1, 10, 10]);
        using OutboxRelayHostedService service = new(relay, Options.Create(new OutboxRelayOptions { BatchSize = 10, PollInterval = TimeSpan.FromMilliseconds(20) }), TimeProvider.System, NullLogger<OutboxRelayHostedService>.Instance);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await relay.ThirdCall.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken).WaitAsync(Timeout, TestContext.Current.CancellationToken);

        Assert.True(relay.Calls >= 3);
    }

    /// <summary>Returns the scripted counts in order (a negative count throws), then 0 forever; signals the third call.</summary>
    private sealed class ScriptedRelay : IOutboxRelay
    {
        private readonly int[] _script;
        private int _calls;

        public ScriptedRelay(int[] script)
        {
            _script = script;
        }

        public int Calls => _calls;

        public TaskCompletionSource ThirdCall { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<int> PublishPendingAsync(CancellationToken cancellationToken)
        {
            int call = Interlocked.Increment(ref _calls);
            if (call == 3)
            {
                ThirdCall.TrySetResult();
            }

            int result = call <= _script.Length ? _script[call - 1] : 0;
            return result < 0 ? Task.FromException<int>(new InvalidOperationException("Scripted pass failure.")) : Task.FromResult(result);
        }
    }
}

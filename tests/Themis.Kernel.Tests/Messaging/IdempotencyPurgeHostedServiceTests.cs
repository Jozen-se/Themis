using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Themis.Host.Messaging;
using Themis.Kernel.Messaging;

namespace Themis.Kernel.Tests.Messaging;

/// <summary>
/// The host's purge loop (milestone 2 step 2; RR-42, D-58), shaped like the relay's: after a full batch the next pass
/// runs at once, after a partial batch it waits the interval, a failing pass is logged and the loop goes on, and a
/// disabled purge runs no pass.
/// </summary>
[Trait("Category", "Unit")]
public sealed class IdempotencyPurgeHostedServiceTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task RR_42_full_batches_run_back_to_back_a_partial_batch_waits_the_interval_and_stopping_ends_the_wait()
    {
        ScriptedPurge purge = new([10, 10, 3]);
        using IdempotencyPurgeHostedService service = new(purge, Options.Create(new IdempotencyPurgeOptions { BatchSize = 10, Interval = TimeSpan.FromHours(1) }), TimeProvider.System, NullLogger<IdempotencyPurgeHostedService>.Instance);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await purge.ThirdCall.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken).WaitAsync(Timeout, TestContext.Current.CancellationToken);

        Assert.Equal(3, purge.Calls);
        Assert.True(service.ExecuteTask!.IsCompleted);
    }

    [Fact]
    public async Task RR_42_a_disabled_purge_runs_no_pass_and_the_loop_ends_at_once()
    {
        ScriptedPurge purge = new([1]);
        using IdempotencyPurgeHostedService service = new(purge, Options.Create(new IdempotencyPurgeOptions { Enabled = false }), TimeProvider.System, NullLogger<IdempotencyPurgeHostedService>.Instance);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await service.ExecuteTask!.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, purge.Calls);
    }

    [Fact]
    public async Task RR_42_a_failing_pass_is_logged_and_the_loop_goes_on_after_the_interval()
    {
        ScriptedPurge purge = new([-1, 10, 10]);
        using IdempotencyPurgeHostedService service = new(purge, Options.Create(new IdempotencyPurgeOptions { BatchSize = 10, Interval = TimeSpan.FromMilliseconds(20) }), TimeProvider.System, NullLogger<IdempotencyPurgeHostedService>.Instance);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await purge.ThirdCall.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken).WaitAsync(Timeout, TestContext.Current.CancellationToken);

        Assert.True(purge.Calls >= 3);
    }

    [Theory]
    [InlineData(0, "00:05:00", "BatchSize")]
    [InlineData(10, "00:00:00", "Interval")]
    [InlineData(10, "-00:00:01", "Interval")]
    public void Invalid_purge_options_name_the_offending_key(int batchSize, string interval, string key)
    {
        IdempotencyPurgeOptions options = new() { BatchSize = batchSize, Interval = TimeSpan.Parse(interval, System.Globalization.CultureInfo.InvariantCulture) };
        Assert.Contains(options.Errors(), error => error.Contains($"Idempotency:Purge:{key}", StringComparison.Ordinal));
        Assert.True(new IdempotencyPurgeOptionsValidator().Validate(null, options).Failed);
    }

    private sealed class ScriptedPurge : IIdempotencyPurge
    {
        private readonly int[] _script;
        private int _calls;

        public ScriptedPurge(int[] script)
        {
            _script = script;
        }

        public int Calls => _calls;

        public TaskCompletionSource ThirdCall { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<int> PurgeExpiredAsync(CancellationToken cancellationToken)
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

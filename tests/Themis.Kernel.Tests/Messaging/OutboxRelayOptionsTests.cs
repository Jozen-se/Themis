using Microsoft.Extensions.Options;
using Themis.Kernel.Messaging;

namespace Themis.Kernel.Tests.Messaging;

/// <summary>The relay's host options (step 9 brief; Build Brief, Settings): the documented defaults, and validation that fails the host start.</summary>
[Trait("Category", "Unit")]
public sealed class OutboxRelayOptionsTests
{
    [Fact]
    public void The_defaults_are_enabled_one_second_and_100_events_per_pass_and_valid()
    {
        OutboxRelayOptions options = new();

        Assert.True(options.Enabled);
        Assert.Equal(TimeSpan.FromSeconds(1), options.PollInterval);
        Assert.Equal(100, options.BatchSize);
        Assert.Empty(options.Errors());
        Assert.True(new OutboxRelayOptionsValidator().Validate(null, options).Succeeded);
    }

    [Theory]
    [InlineData(0, 1, "BatchSize")]
    [InlineData(-5, 1, "BatchSize")]
    [InlineData(100, 0, "PollInterval")]
    [InlineData(100, -1, "PollInterval")]
    public void A_batch_size_or_poll_interval_at_or_below_zero_fails_validation_whether_the_relay_is_enabled_or_not(int batchSize, int pollSeconds, string field)
    {
        foreach (bool enabled in new[] { true, false })
        {
            OutboxRelayOptions options = new() { Enabled = enabled, BatchSize = batchSize, PollInterval = TimeSpan.FromSeconds(pollSeconds) };

            ValidateOptionsResult result = new OutboxRelayOptionsValidator().Validate(null, options);

            Assert.True(result.Failed);
            Assert.Contains(result.Failures!, failure => failure.Contains($"Outbox:Relay:{field}", StringComparison.Ordinal));
        }
    }
}

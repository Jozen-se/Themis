using Microsoft.Extensions.Options;

namespace Themis.Kernel.Messaging;

/// <summary>
/// The relay's operating options (step 9 brief; Build Brief, Settings convention): host configuration under
/// <c>Outbox:Relay</c>, not a configurable default of P-11, since no company adjusts them. The defaults are here:
/// enabled, one pass per second when the last pass found less than a full batch, 100 events per pass. Validated at
/// every host start, enabled or not (<see cref="OutboxRelayOptionsValidator"/>).
/// </summary>
internal sealed class OutboxRelayOptions
{
    public const string SectionName = "Outbox:Relay";

    public bool Enabled { get; set; } = true;

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

    public int BatchSize { get; set; } = 100;

    /// <summary>The problems with these options, empty when they are valid.</summary>
    public IReadOnlyList<string> Errors()
    {
        List<string> errors = [];
        if (PollInterval <= TimeSpan.Zero)
        {
            errors.Add($"{SectionName}:PollInterval must be above zero; it is {PollInterval}.");
        }

        if (BatchSize <= 0)
        {
            errors.Add($"{SectionName}:BatchSize must be above zero; it is {BatchSize}.");
        }

        return errors;
    }
}

/// <summary>Fails the host start on invalid relay options, whether the relay is enabled or not (step 9 brief).</summary>
internal sealed class OutboxRelayOptionsValidator : IValidateOptions<OutboxRelayOptions>
{
    public ValidateOptionsResult Validate(string? name, OutboxRelayOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        IReadOnlyList<string> errors = options.Errors();
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}

using Microsoft.Extensions.Options;

namespace Themis.Kernel.Messaging;

/// <summary>
/// Host configuration of the idempotency purge (milestone 2 step 2; RR-42, D-58), not a settings-table default
/// (Build Brief, Settings): whether the loop runs, how long it waits after a partial or empty batch, and how many
/// expired records one pass deletes. Validated at every host start, enabled or not.
/// </summary>
internal sealed class IdempotencyPurgeOptions
{
    public const string SectionName = "Idempotency:Purge";

    public bool Enabled { get; set; } = true;

    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(5);

    public int BatchSize { get; set; } = 1000;

    public IReadOnlyList<string> Errors()
    {
        List<string> errors = [];
        if (Interval <= TimeSpan.Zero)
        {
            errors.Add($"{SectionName}:Interval must be above zero; it is {Interval}.");
        }

        if (BatchSize <= 0)
        {
            errors.Add($"{SectionName}:BatchSize must be above zero; it is {BatchSize}.");
        }

        return errors;
    }
}

internal sealed class IdempotencyPurgeOptionsValidator : IValidateOptions<IdempotencyPurgeOptions>
{
    public ValidateOptionsResult Validate(string? name, IdempotencyPurgeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        IReadOnlyList<string> errors = options.Errors();
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}

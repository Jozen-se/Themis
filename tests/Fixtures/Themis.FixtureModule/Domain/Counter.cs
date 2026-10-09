using Themis.Kernel.Primitives;

namespace Themis.FixtureModule.Domain;

/// <summary>The fixture entity: a versioned row the commands change (R-API-02 on Version).</summary>
internal sealed record Counter(Guid Id, long Value, long Version, decimal Sample)
{
    public const long FirstVersion = 1;

    public static Result<Counter> Create(Guid id, long initialValue, decimal sample) =>
        initialValue < 0
            ? Error.Validation("initial_value_negative", "A counter starts at zero or above.")
            : new Counter(id, initialValue, FirstVersion, sample);

    /// <summary>Applies the increment on the expected version only; a stale version is rejected (R-API-02).</summary>
    public Result<Counter> Touch(long expectedVersion, long increment)
    {
        if (expectedVersion != Version)
        {
            return Error.PreconditionFailed("version_stale", $"The counter is at version {Version}; the command expected {expectedVersion}.");
        }

        return increment == 0
            ? Error.Rule("R-API-03", "increment_zero", "An increment of zero changes nothing and is rejected.")
            : this with { Value = Value + increment, Version = Version + 1 };
    }
}

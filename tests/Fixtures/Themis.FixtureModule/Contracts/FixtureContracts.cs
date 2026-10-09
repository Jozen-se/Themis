using Themis.Kernel.Contracts.Commands;

namespace Themis.FixtureModule.Contracts;

/// <summary>The fixture module's public contract surface (ARCH-07): its schema, role, commands and routes.</summary>
public static class FixtureContracts
{
    public const string SchemaName = "fixture";
    public const string RoleName = "themis_fixture";
    public const string TypeCode = "FIXTURE_COUNTER";
    public const string EntityType = "FixtureCounter";

    /// <summary>The routes the composition root maps through the kernel pipeline (brief, API conventions).</summary>
    public static IReadOnlyList<ICommandRoute> Routes { get; } =
    [
        new CommandRoute<CreateCounter, CounterSnapshot>("/v1/fixture/counters"),
        new CommandRoute<TouchCounter, CounterSnapshot>("/v1/fixture/counters/{id}/touch"),
    ];
}

/// <summary>
/// Failure a fixture command injects after its business write, so tests can prove what the pipeline leaves behind
/// (R-LED-05 for the pattern, D-41 for the retry, I-14 for the role boundary).
/// </summary>
public enum FixtureFailure
{
    None,

    /// <summary>The handler writes, then returns a rule violation: the write must roll back and the rejection be stored (R-API-03, R-API-01).</summary>
    RejectAfterWrite,

    /// <summary>The handler writes, then throws: nothing is stored, not even the idempotency record.</summary>
    ThrowAfterWrite,

    /// <summary>The handler writes, then raises SQLSTATE 40001 on the first attempt only: the pipeline retries once and the write lands once (D-41).</summary>
    SerializationFailureOnFirstAttempt,

    /// <summary>The handler tries to insert into kernel.audit_event as themis_fixture: PostgreSQL refuses with 42501 (I-14, R-DB-01).</summary>
    WriteKernelTable,
}

/// <summary>Creates a counter row in schema fixture (permission FIXTURE_COUNTER.CREATE).</summary>
[Command("FIXTURE_COUNTER.CREATE")]
public sealed record CreateCounter(long InitialValue, decimal Sample, FixtureFailure Failure = FixtureFailure.None) : ICommand<CounterSnapshot>;

/// <summary>Adds to a counter; the row's version comes from If-Match (R-API-02). Id comes from the route.</summary>
[Command("FIXTURE_COUNTER.TOUCH")]
public sealed record TouchCounter(Guid Id, long ExpectedVersion, long Increment, FixtureFailure Failure = FixtureFailure.None) : ICommand<CounterSnapshot>, IVersionedCommand;

/// <summary>The counter after the command. Sample is a decimal, so the response proves D-37 (strings for decimals, numbers for integers).</summary>
public sealed record CounterSnapshot(Guid Id, long Value, long Version, decimal Sample);

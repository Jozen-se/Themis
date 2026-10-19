using Themis.Kernel.Contracts.Commands;
using Themis.Kernel.Contracts.Security;

namespace Themis.FixtureModule.Contracts;

/// <summary>The fixture module's public contract surface (ARCH-07): its schema, role, commands and routes. The counter's posted flag stays in its table and snapshot as a column of the test schema; no command sets it since step 8.</summary>
public static class FixtureContracts
{
    public const string SchemaName = "fixture";
    public const string RoleName = "themis_fixture";
    public const string TypeCode = "FIXTURE_COUNTER";
    public const string EntityType = "FixtureCounter";

    /// <summary>
    /// The permissions the fixture commands run under (step 4 brief, acceptance 3). Creating a counter shares
    /// ITEM.CREATE with the real command of step 5. Touching a counter runs under the test-only permission
    /// FIXTURE.TOUCH (owner's decision on step 7, point 3): the failure injection keeps proving the pipeline's write
    /// path under the themis_fixture role without any real permission's tests depending on test-only code. The
    /// integration tests' fixture bootstrap seeds the permission, the role <see cref="TouchRoleCode"/> and the clerk's
    /// assignment; no production catalogue knows them. The POST stand-in retired in step 8 with the real
    /// INVENTORY_ADJUSTMENT.POST. The counter carries the company and optional site the scope comes from.
    /// </summary>
    public const string CreatePermission = "ITEM.CREATE";
    public const string TouchPermission = "FIXTURE.TOUCH";

    /// <summary>The test-only role that carries <see cref="TouchPermission"/>, assigned to the clerk by the integration tests.</summary>
    public const string TouchRoleCode = "FIXTURE_TESTER";

    /// <summary>The routes the composition root maps through the kernel pipeline (brief, API conventions).</summary>
    public static IReadOnlyList<ICommandRoute> Routes { get; } =
    [
        new CommandRoute<CreateCounter, CounterSnapshot>("/v1/fixture/counters"),
        new CommandRoute<TouchCounter, CounterSnapshot>("/v1/fixture/counters/{id}/touch"),
        new CommandRoute<TouchPair, TouchPairSnapshot>("/v1/fixture/counters/touch-pair"),
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

/// <summary>Creates a counter row in schema fixture for a company, optionally at a site; the scope is in the body (Build Brief, Authorisation).</summary>
[Command(FixtureContracts.CreatePermission)]
public sealed record CreateCounter(Guid CompanyId, long InitialValue, decimal Sample, Guid? SiteId = null, FixtureFailure Failure = FixtureFailure.None) : ICommand<CounterSnapshot>, IScopedRequest
{
    public RequestScope Scope => SiteId is { } site ? RequestScope.ForSite(CompanyId, site) : RequestScope.ForCompany(CompanyId);
}

/// <summary>Adds to a counter; the row's version comes from If-Match (R-API-02). Id comes from the route; the scope from the row (IScopeResolver).</summary>
[Command(FixtureContracts.TouchPermission)]
public sealed record TouchCounter(Guid Id, long ExpectedVersion, long Increment, FixtureFailure Failure = FixtureFailure.None) : ICommand<CounterSnapshot>, IVersionedCommand;

/// <summary>
/// Adds to two counters in the given order through Entity Framework, waiting at the fixture barrier between the two
/// writes on the first attempt (RR-40 test seam). Runs under FIXTURE.TOUCH; the scope is the first counter's.
/// </summary>
[Command(FixtureContracts.TouchPermission)]
public sealed record TouchPair(Guid FirstId, Guid SecondId, long Increment) : ICommand<TouchPairSnapshot>;

/// <summary>Both counters after a touch pair.</summary>
public sealed record TouchPairSnapshot(CounterSnapshot First, CounterSnapshot Second);

/// <summary>The counter after the command. Sample is a decimal, so the response proves D-37 (strings for decimals, numbers for integers).</summary>
public sealed record CounterSnapshot(Guid Id, Guid CompanyId, Guid? SiteId, long Value, long Version, decimal Sample, bool Posted) : IVersionedResult;

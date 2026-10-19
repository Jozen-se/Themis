using Themis.FixtureModule.Domain;
using Themis.Kernel.Contracts.Persistence;

namespace Themis.FixtureModule.Application;

/// <summary>
/// Second persistence port of the fixture module, implemented over Entity Framework so that a database error reaches
/// the pipeline the way Entity Framework wraps it (RR-40). Every statement runs as themis_fixture (R-DB-01).
/// </summary>
internal interface ITrackedCounterStore
{
    /// <summary>Adds the increment to the counter through a tracked update; null when the counter does not exist.</summary>
    Task<CounterChange?> TouchAsync(IUnitOfWork unitOfWork, Guid id, long increment, CancellationToken cancellationToken);
}

/// <summary>A counter before and after one touch, for the audit event (R-AUD-01).</summary>
internal sealed record CounterChange(Counter Before, Counter After);

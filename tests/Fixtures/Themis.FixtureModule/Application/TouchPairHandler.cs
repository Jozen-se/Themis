using Themis.FixtureModule.Contracts;
using Themis.FixtureModule.Domain;
using Themis.Kernel.Contracts.Commands;
using Themis.Kernel.Contracts.Messaging;
using Themis.Kernel.Contracts.Persistence;
using Themis.Kernel.Contracts.Security;
using Themis.Kernel.Primitives;

namespace Themis.FixtureModule.Application;

/// <summary>
/// Touch pair: adds to two counters in the given order through Entity Framework's SaveChanges, waiting at the
/// fixture barrier between the two writes on the first attempt (RR-40). Two such commands on the same counters in
/// opposite order deadlock; PostgreSQL ends one with SQLSTATE 40P01 wrapped in a DbUpdateException, the pipeline
/// retries it as a whole (D-41) and both succeed. Audit event and FIXTURE_COUNTER_TOUCHED per counter (R-AUD-01, D-44).
/// </summary>
internal sealed class TouchPairHandler : ICommandHandler<TouchPair, TouchPairSnapshot>
{
    private readonly ITrackedCounterStore _store;
    private readonly FixtureBarrier _barrier;

    public TouchPairHandler(ITrackedCounterStore store, FixtureBarrier barrier)
    {
        _store = store;
        _barrier = barrier;
    }

    public async Task<Result<TouchPairSnapshot>> HandleAsync(TouchPair command, ICommandContext context, CancellationToken cancellationToken)
    {
        Counter? first = await TouchAsync(context, command.FirstId, command.Increment, cancellationToken);
        if (first is null)
        {
            return Error.NotFound("counter_not_found", $"Counter {command.FirstId} does not exist.");
        }

        await _barrier.WaitForPartnersAsync(context.IdempotencyKey.Value, cancellationToken);

        Counter? second = await TouchAsync(context, command.SecondId, command.Increment, cancellationToken);
        if (second is null)
        {
            return Error.NotFound("counter_not_found", $"Counter {command.SecondId} does not exist.");
        }

        return new TouchPairSnapshot(CreateCounterHandler.Snapshot(first), CreateCounterHandler.Snapshot(second));
    }

    private async Task<Counter?> TouchAsync(ICommandContext context, Guid id, long increment, CancellationToken cancellationToken)
    {
        CounterChange? change = await _store.TouchAsync(context.UnitOfWork, id, increment, cancellationToken);
        if (change is null)
        {
            return null;
        }

        Counter after = change.After;
        context.Audit.Record(FixtureContracts.EntityType, after.Id, CounterAudit.Changes(change.Before, after));
        context.Outbox.Publish(EventNames.Generated(FixtureContracts.TypeCode, "TOUCH"), EventNames.DefaultSchemaVersion, FixtureContracts.TypeCode, after.Id, after.CompanyId, CreateCounterHandler.Snapshot(after));
        return after;
    }
}

/// <summary>The scope of a touch pair is the first counter's (Build Brief, Authorisation); a missing counter is 404.</summary>
internal sealed class TouchPairScopeResolver : IScopeResolver<TouchPair>
{
    private readonly ICounterStore _store;

    public TouchPairScopeResolver(ICounterStore store)
    {
        _store = store;
    }

    public async Task<Result<RequestScope>> ResolveAsync(TouchPair request, IUnitOfWork unitOfWork, CancellationToken cancellationToken)
    {
        Counter? counter = await _store.FindAsync(unitOfWork, request.FirstId, cancellationToken);
        if (counter is null)
        {
            return Error.NotFound("counter_not_found", $"Counter {request.FirstId} does not exist.");
        }

        return counter.SiteId is { } site ? RequestScope.ForSite(counter.CompanyId, site) : RequestScope.ForCompany(counter.CompanyId);
    }
}

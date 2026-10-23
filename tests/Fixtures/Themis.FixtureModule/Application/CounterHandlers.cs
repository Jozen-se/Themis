using System.Globalization;
using Themis.FixtureModule.Contracts;
using Themis.FixtureModule.Domain;
using Themis.Kernel.Contracts.Commands;
using Themis.Kernel.Contracts.Messaging;
using Themis.Kernel.Contracts.Persistence;
using Themis.Kernel.Contracts.Security;
using Themis.Kernel.Primitives;

namespace Themis.FixtureModule.Application;

/// <summary>Create: inserts the row, records the audit event and publishes FIXTURE_COUNTER_CREATED (R-AUD-01, D-44).</summary>
internal sealed class CreateCounterHandler : ICommandHandler<CreateCounter, CounterSnapshot>
{
    private readonly ICounterStore _store;
    private readonly FixtureFailureInjector _failures;

    public CreateCounterHandler(ICounterStore store, FixtureFailureInjector failures)
    {
        _store = store;
        _failures = failures;
    }

    public async Task<Result<CounterSnapshot>> HandleAsync(CreateCounter command, ICommandContext context, CancellationToken cancellationToken)
    {
        Result<Counter> created = Counter.Create(EntityId.New(), command.CompanyId, command.SiteId, command.InitialValue, command.Sample);
        if (created.IsFailure)
        {
            return Result<CounterSnapshot>.Failure(created.Errors);
        }

        Counter counter = created.Value;
        await _store.InsertAsync(context.UnitOfWork, counter, cancellationToken);
        await _failures.ApplyAsync(new FixtureFailureContext(command.Failure, context.IdempotencyKey.Value, ct => _store.WriteKernelTableAsync(context.UnitOfWork, ct), ct => _store.OverflowSampleAsync(context.UnitOfWork, counter.Id, ct)), cancellationToken);
        if (command.Failure == FixtureFailure.RejectAfterWrite)
        {
            return Error.Rule("R-API-03", "rejected_after_write", "Injected rejection after the business write (fixture).");
        }

        context.Audit.Record(FixtureContracts.EntityType, counter.Id, CounterAudit.Changes(null, counter));
        context.Outbox.Publish(EventNames.Generated(FixtureContracts.TypeCode, "CREATE"), EventNames.DefaultSchemaVersion, FixtureContracts.TypeCode, counter.Id, counter.CompanyId, Snapshot(counter));
        return Snapshot(counter);
    }

    internal static CounterSnapshot Snapshot(Counter counter) => new(counter.Id, counter.CompanyId, counter.SiteId, counter.Value, counter.Version, counter.Sample, counter.Posted);
}

/// <summary>Touch: versioned update (R-API-02), audit event and FIXTURE_COUNTER_TOUCHED (R-AUD-01, D-44).</summary>
internal sealed class TouchCounterHandler : ICommandHandler<TouchCounter, CounterSnapshot>
{
    private readonly ICounterStore _store;
    private readonly FixtureFailureInjector _failures;

    public TouchCounterHandler(ICounterStore store, FixtureFailureInjector failures)
    {
        _store = store;
        _failures = failures;
    }

    public async Task<Result<CounterSnapshot>> HandleAsync(TouchCounter command, ICommandContext context, CancellationToken cancellationToken)
    {
        Counter? current = await _store.FindAsync(context.UnitOfWork, command.Id, cancellationToken);
        if (current is null)
        {
            return Error.NotFound("counter_not_found", $"Counter {command.Id} does not exist.");
        }

        Result<Counter> touched = current.Touch(command.ExpectedVersion, command.Increment);
        if (touched.IsFailure)
        {
            return Result<CounterSnapshot>.Failure(touched.Errors);
        }

        await _store.UpdateAsync(context.UnitOfWork, touched.Value, cancellationToken);
        await _failures.ApplyAsync(new FixtureFailureContext(command.Failure, context.IdempotencyKey.Value, ct => _store.WriteKernelTableAsync(context.UnitOfWork, ct)), cancellationToken);
        if (command.Failure == FixtureFailure.RejectAfterWrite)
        {
            return Error.Rule("R-API-03", "rejected_after_write", "Injected rejection after the business write (fixture).");
        }

        context.Audit.Record(FixtureContracts.EntityType, touched.Value.Id, CounterAudit.Changes(current, touched.Value));
        context.Outbox.Publish(EventNames.Generated(FixtureContracts.TypeCode, "TOUCH"), EventNames.DefaultSchemaVersion, FixtureContracts.TypeCode, touched.Value.Id, touched.Value.CompanyId, CreateCounterHandler.Snapshot(touched.Value));
        return CreateCounterHandler.Snapshot(touched.Value);
    }
}

/// <summary>
/// The scope of a command on an existing counter: the counter's company and, when it has one, its site (Build
/// Brief, Authorisation: scope-checked after the record is loaded, inside the transaction). A missing counter is 404.
/// </summary>
internal sealed class CounterScopeResolver : IScopeResolver<TouchCounter>
{
    private readonly ICounterStore _store;

    public CounterScopeResolver(ICounterStore store)
    {
        _store = store;
    }

    public Task<Result<RequestScope>> ResolveAsync(TouchCounter request, IUnitOfWork unitOfWork, CancellationToken cancellationToken) =>
        ResolveAsync(request.Id, unitOfWork, cancellationToken);

    private async Task<Result<RequestScope>> ResolveAsync(Guid id, IUnitOfWork unitOfWork, CancellationToken cancellationToken)
    {
        Counter? counter = await _store.FindAsync(unitOfWork, id, cancellationToken);
        if (counter is null)
        {
            return Error.NotFound("counter_not_found", $"Counter {id} does not exist.");
        }

        return counter.SiteId is { } site ? RequestScope.ForSite(counter.CompanyId, site) : RequestScope.ForCompany(counter.CompanyId);
    }
}

/// <summary>Validation that runs before the transaction opens; the dispatcher collects every validator's errors.</summary>
internal sealed class CreateCounterValidator : ICommandValidator<CreateCounter>
{
    public IEnumerable<Error> Validate(CreateCounter command)
    {
        if (command.Sample < 0)
        {
            yield return Error.Validation("sample_negative", "Sample is zero or above.");
        }

        if (command.CompanyId == Guid.Empty)
        {
            yield return Error.Validation("company_required", "A counter belongs to a company.");
        }
    }
}

/// <summary>The changed fields of an audit event: before and after as strings (R-AUD-01).</summary>
internal static class CounterAudit
{
    public static IReadOnlyDictionary<string, FieldChange> Changes(Counter? before, Counter after)
    {
        Dictionary<string, FieldChange> changes = new(StringComparer.Ordinal);
        Add(changes, "value", before?.Value, after.Value);
        Add(changes, "version", before?.Version, after.Version);
        Add(changes, "sample", before?.Sample, after.Sample);
        Add(changes, "posted", before?.Posted, after.Posted);
        return changes;
    }

    private static void Add<T>(Dictionary<string, FieldChange> changes, string field, T? before, T after)
        where T : struct, IConvertible
    {
        string? previous = before?.ToString(CultureInfo.InvariantCulture);
        string next = after.ToString(CultureInfo.InvariantCulture);
        if (!string.Equals(previous, next, StringComparison.Ordinal))
        {
            changes[field] = new FieldChange(previous, next);
        }
    }
}

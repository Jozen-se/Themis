using System.Globalization;
using Themis.FixtureModule.Contracts;
using Themis.FixtureModule.Domain;
using Themis.Kernel.Contracts.Commands;
using Themis.Kernel.Contracts.Messaging;
using Themis.Kernel.Contracts.Persistence;
using Themis.Kernel.Primitives;

namespace Themis.FixtureModule.Application;

/// <summary>FIXTURE_COUNTER.CREATE: inserts the row, records the audit event and publishes FIXTURE_COUNTER_CREATED (R-AUD-01, D-44).</summary>
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
        Result<Counter> created = Counter.Create(EntityId.New(), command.InitialValue, command.Sample);
        if (created.IsFailure)
        {
            return Result<CounterSnapshot>.Failure(created.Errors);
        }

        Counter counter = created.Value;
        await _store.InsertAsync(context.UnitOfWork, counter, cancellationToken);
        await _failures.ApplyAsync(new FixtureFailureContext(command.Failure, context.IdempotencyKey.Value, ct => _store.WriteKernelTableAsync(context.UnitOfWork, ct)), cancellationToken);
        if (command.Failure == FixtureFailure.RejectAfterWrite)
        {
            return Error.Rule("R-API-03", "rejected_after_write", "Injected rejection after the business write (fixture).");
        }

        context.Audit.Record(FixtureContracts.EntityType, counter.Id, CounterAudit.Changes(null, counter));
        context.Outbox.Publish(EventNames.Generated(FixtureContracts.TypeCode, "CREATE"), EventNames.DefaultSchemaVersion, counter.Id, Snapshot(counter));
        return Snapshot(counter);
    }

    internal static CounterSnapshot Snapshot(Counter counter) => new(counter.Id, counter.Value, counter.Version, counter.Sample);
}

/// <summary>FIXTURE_COUNTER.TOUCH: versioned update (R-API-02), audit event and FIXTURE_COUNTER_TOUCHED (R-AUD-01, D-44).</summary>
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
        context.Outbox.Publish(EventNames.Generated(FixtureContracts.TypeCode, "TOUCH"), EventNames.DefaultSchemaVersion, touched.Value.Id, CreateCounterHandler.Snapshot(touched.Value));
        return CreateCounterHandler.Snapshot(touched.Value);
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
        return changes;
    }

    private static void Add<T>(Dictionary<string, FieldChange> changes, string field, T? before, T after)
        where T : struct, IFormattable
    {
        string? previous = before?.ToString(null, CultureInfo.InvariantCulture);
        string next = after.ToString(null, CultureInfo.InvariantCulture);
        if (!string.Equals(previous, next, StringComparison.Ordinal))
        {
            changes[field] = new FieldChange(previous, next);
        }
    }
}

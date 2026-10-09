using System.Collections.Concurrent;
using System.Data.Common;

namespace Themis.FixtureModule.Application;

/// <summary>
/// Raises the injected failures after the handler's business write. The serialization failure fires once per
/// idempotency key, so the pipeline's retry (D-41) sees a clean second attempt.
/// </summary>
internal sealed class FixtureFailureInjector
{
    private readonly ConcurrentDictionary<string, byte> _alreadyFailed = new(StringComparer.Ordinal);

    public async Task ApplyAsync(FixtureFailureContext context, CancellationToken cancellationToken)
    {
        switch (context.Failure)
        {
            case Contracts.FixtureFailure.ThrowAfterWrite:
                throw new InvalidOperationException("Injected failure after the business write (fixture).");
            case Contracts.FixtureFailure.SerializationFailureOnFirstAttempt:
                if (_alreadyFailed.TryAdd(context.IdempotencyKey, 0))
                {
                    throw new InjectedSerializationFailure();
                }

                break;
            case Contracts.FixtureFailure.WriteKernelTable:
                await context.WriteKernelTableAsync(cancellationToken);
                break;
            default:
                break;
        }
    }

    /// <summary>The database exception a serialization failure raises, with the SQLSTATE the pipeline retries on (D-41).</summary>
    internal sealed class InjectedSerializationFailure : DbException
    {
        public InjectedSerializationFailure()
            : base("40001: could not serialize access due to concurrent update (injected by the fixture module)")
        {
        }

        public override string SqlState => "40001";
    }
}

internal sealed record FixtureFailureContext(Contracts.FixtureFailure Failure, string IdempotencyKey, Func<CancellationToken, Task> WriteKernelTableAsync);

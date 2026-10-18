using Themis.FixtureModule.Contracts;
using Themis.Kernel.Contracts.Commands;
using Themis.Kernel.Contracts.Persistence;
using Themis.Kernel.Contracts.Posting;

namespace Themis.FixtureModule.Application;

/// <summary>
/// The test implementation of the posting service's hook (step 8 brief, Tests): after the ledger writes and before
/// the document update, it raises the failure the <see cref="PostingFailureSwitch"/> names, so the integration tests
/// prove that a failure after the inventory entries leaves nothing behind (R-LED-05, I-09) and that a serialization
/// failure on the first attempt is retried once and posts exactly once (D-41). Only ThrowAfterWrite and
/// SerializationFailureOnFirstAttempt apply here; the other values of <see cref="FixtureFailure"/> do nothing.
/// The hook never touches the database.
/// </summary>
internal sealed class FixturePostingHook : IPostingHook
{
    private readonly PostingFailureSwitch _switch;
    private readonly FixtureFailureInjector _failures;

    public FixturePostingHook(PostingFailureSwitch failureSwitch, FixtureFailureInjector failures)
    {
        _switch = failureSwitch;
        _failures = failures;
    }

    public async Task AfterLedgerWritesAsync(IUnitOfWork unitOfWork, Guid documentId, ICommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        FixtureFailure failure = _switch.Failure;
        if (failure is FixtureFailure.ThrowAfterWrite or FixtureFailure.SerializationFailureOnFirstAttempt)
        {
            await _failures.ApplyAsync(new FixtureFailureContext(failure, context.IdempotencyKey.Value, _ => Task.CompletedTask), cancellationToken);
        }
    }
}

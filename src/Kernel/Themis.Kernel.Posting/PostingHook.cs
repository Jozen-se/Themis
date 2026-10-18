using Themis.Kernel.Contracts.Commands;
using Themis.Kernel.Contracts.Persistence;

namespace Themis.Kernel.Posting;

/// <summary>
/// A test seam (Build Brief, Test seams): the posting service calls it once after the ledger rows and cache updates
/// are written and before it returns, so the acceptance tests can inject a failure after the inventory entries and
/// before commit (R-LED-05) and a serialization failure on the first attempt (D-41). Internal to the posting
/// capability, never part of a contract; the production registration is <see cref="NoPostingHook"/>, which does
/// nothing, and the integration tests register their own before the kernel's registration runs. Listed in
/// docs/development/test-seams.md.
/// </summary>
internal interface IPostingHook
{
    Task AfterLedgerWritesAsync(IUnitOfWork unitOfWork, Guid documentId, ICommandContext context, CancellationToken cancellationToken);
}

/// <summary>The production hook: nothing happens after the ledger writes.</summary>
internal sealed class NoPostingHook : IPostingHook
{
    public Task AfterLedgerWritesAsync(IUnitOfWork unitOfWork, Guid documentId, ICommandContext context, CancellationToken cancellationToken) => Task.CompletedTask;
}

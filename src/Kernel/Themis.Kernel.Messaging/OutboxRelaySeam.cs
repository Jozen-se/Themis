namespace Themis.Kernel.Messaging;

/// <summary>
/// A test seam (Build Brief, Test seams): the pass calls it once after it has claimed its events and before it logs,
/// updates and commits them, so a test can hold the pass while another runs or make it fail after the claim. Internal
/// to the messaging capability; the production registration is <see cref="NoOutboxRelaySeam"/>, which does nothing,
/// and the integration tests register their own before the kernel's registration runs. Listed in
/// docs/development/test-seams.md.
/// </summary>
internal interface IOutboxRelaySeam
{
    Task AfterClaimAsync(IReadOnlyList<Guid> eventIds, CancellationToken cancellationToken);
}

/// <summary>The production seam: nothing happens between the claim and the commit.</summary>
internal sealed class NoOutboxRelaySeam : IOutboxRelaySeam
{
    public Task AfterClaimAsync(IReadOnlyList<Guid> eventIds, CancellationToken cancellationToken) => Task.CompletedTask;
}

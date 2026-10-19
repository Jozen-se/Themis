using Themis.Kernel.Primitives;

namespace Themis.Kernel.MasterData;

/// <summary>
/// A test seam (Build Brief, Test seams; docs/development/test-seams.md): ITEM.CREATE calls it once after its duplicate
/// pre-check and before it writes, so the RR-45 test can hold the first of two creates with the same item number
/// while the second commits, and prove that the unique index's violation is mapped to the R-MD-05 rejection. Internal
/// to the master data capability; the production registration does nothing.
/// </summary>
internal interface IItemCreateSeam
{
    Task AfterDuplicateCheckAsync(Code itemNumber, CancellationToken cancellationToken);
}

/// <summary>The production seam: nothing happens between the pre-check and the write.</summary>
internal sealed class NoItemCreateSeam : IItemCreateSeam
{
    public Task AfterDuplicateCheckAsync(Code itemNumber, CancellationToken cancellationToken) => Task.CompletedTask;
}

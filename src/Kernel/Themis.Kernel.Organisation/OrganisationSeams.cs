using Themis.Kernel.Primitives;

namespace Themis.Kernel.Organisation;

/// <summary>
/// A test seam (Build Brief, Test seams; docs/development/test-seams.md): every organisation create command calls it
/// once after its duplicate pre-checks and before it writes, so the RR-45 race test can hold the first of two creates
/// with the same code or organisation number while the second commits, and prove that the unique index's violation
/// is mapped to the R-MD-05 rejection (one seam for the pattern, not one per index). Internal to the organisation
/// capability; the production registration does nothing.
/// </summary>
internal interface IOrganisationCreateSeam
{
    Task AfterDuplicateChecksAsync(string entityType, Code code, CancellationToken cancellationToken);
}

/// <summary>The production seam: nothing happens between the pre-checks and the write.</summary>
internal sealed class NoOrganisationCreateSeam : IOrganisationCreateSeam
{
    public Task AfterDuplicateChecksAsync(string entityType, Code code, CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// A test seam (milestone 2 step 3 brief, item 5): COMPANY.UPDATE calls it once after its versioned UPDATE of the
/// company row has returned and before the R-MD-06 trigger checks, so the concurrency test can hold the update,
/// which holds the row lock, while a posting starts and waits on its FOR SHARE of lock class 1b (D-65). Internal to
/// the organisation capability; the production registration does nothing.
/// </summary>
internal interface ICompanyUpdateSeam
{
    Task AfterVersionedUpdateAsync(Guid companyId, CancellationToken cancellationToken);
}

/// <summary>The production seam: nothing happens between the update and the checks.</summary>
internal sealed class NoCompanyUpdateSeam : ICompanyUpdateSeam
{
    public Task AfterVersionedUpdateAsync(Guid companyId, CancellationToken cancellationToken) => Task.CompletedTask;
}

using Themis.Kernel.Contracts.Persistence;
using Themis.Kernel.Contracts.Security;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Pipeline.Security;

/// <summary>
/// The defaults the pipeline registers until the identity capability supplies real ones: nobody is signed in and
/// nothing is permitted, so a misconfigured host refuses every command and query instead of allowing it (R-SEC-01).
/// </summary>
internal sealed class NoCurrentSubject : ICurrentSubject
{
    public AuthenticatedSubject? Current => null;
}

internal sealed class DenyAllAccessControl : IAccessControl
{
    public Task<Result<Principal>> ResolvePrincipalAsync(IUnitOfWork unitOfWork, AuthenticatedSubject subject, CancellationToken cancellationToken) =>
        Task.FromResult(Result<Principal>.Failure(Error.Forbidden(AccessDenied.SubjectUnknown, "No identity capability is registered; every subject is unknown.", AccessDenied.RuleId)));

    public Task<Result> AuthorizeAsync(IUnitOfWork unitOfWork, Principal principal, string permissionCode, RequestScope scope, DateTimeOffset now, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Failure(Error.Forbidden(AccessDenied.PermissionMissing, "No identity capability is registered; nothing is permitted.", AccessDenied.RuleId)));
}

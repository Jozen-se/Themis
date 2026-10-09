using Themis.Kernel.Contracts.Security;

namespace Themis.Kernel.Pipeline.Security;

/// <summary>
/// The defaults the pipeline registers until the identity capability supplies real ones: nobody is signed in and
/// nothing is permitted, so a misconfigured host refuses every command instead of allowing it (R-SEC-01).
/// </summary>
internal sealed class NoCurrentPrincipal : ICurrentPrincipal
{
    public Principal? Current => null;
}

internal sealed class DenyAllPermissionEvaluator : IPermissionEvaluator
{
    public ValueTask<bool> HasPermissionAsync(Principal principal, string permissionCode, CancellationToken cancellationToken) => ValueTask.FromResult(false);
}

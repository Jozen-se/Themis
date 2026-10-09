using Microsoft.Extensions.DependencyInjection;
using Themis.Kernel.Contracts.Persistence;
using Themis.Kernel.Contracts.Security;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Pipeline.Dispatch;

/// <summary>
/// Where a command or query takes its scope from (Build Brief, Authorisation; D-47): its own data when it is an
/// <see cref="IScopedRequest"/>, else the record it names through a registered <see cref="IScopeResolver{TRequest}"/>.
/// A request type with neither is refused: at mapping time, so a host cannot start with an unscoped endpoint, and
/// again at dispatch, so a caller outside HTTP cannot bypass it (403 scope_undeclared, R-SEC-01).
/// </summary>
internal static class ScopeSources
{
    public static bool Declares(Type requestType, IServiceProvider services)
    {
        if (typeof(IScopedRequest).IsAssignableFrom(requestType))
        {
            return true;
        }

        // Asked at mapping time on the root provider, so the registration is checked without resolving a scoped service.
        Type resolverType = typeof(IScopeResolver<>).MakeGenericType(requestType);
        IServiceProviderIsService? registry = services.GetService<IServiceProviderIsService>();
        return registry is not null ? registry.IsService(resolverType) : services.GetService(resolverType) is not null;
    }

    /// <summary>Throws at mapping time for a command or query that names no scope; the host fails to start (fail closed).</summary>
    public static void RequireDeclared(Type requestType, IServiceProvider services)
    {
        if (!Declares(requestType, services))
        {
            throw new InvalidOperationException($"{requestType.FullName} declares no scope: implement IScopedRequest or register IScopeResolver<{requestType.Name}> (R-SEC-01, D-47).");
        }
    }

    public static async Task<Result<RequestScope>> ResolveAsync<TRequest>(TRequest request, IUnitOfWork unitOfWork, IServiceProvider services, CancellationToken cancellationToken)
        where TRequest : notnull
    {
        if (request is IScopedRequest scoped)
        {
            return scoped.Scope;
        }

        IScopeResolver<TRequest>? resolver = services.GetService<IScopeResolver<TRequest>>();
        if (resolver is null)
        {
            return Error.Forbidden(AccessDenied.ScopeUndeclared, $"{typeof(TRequest).Name} declares no scope and is refused (D-47).", AccessDenied.RuleId);
        }

        return await resolver.ResolveAsync(request, unitOfWork, cancellationToken);
    }
}

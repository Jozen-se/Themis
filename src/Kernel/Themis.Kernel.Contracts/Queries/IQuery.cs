using Themis.Kernel.Contracts.Persistence;
using Themis.Kernel.Contracts.Security;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Contracts.Queries;

/// <summary>A read (P-04: GET only, never changes state). The result type is what the API returns.</summary>
#pragma warning disable CA1040 // marker interface: the type parameter carries the contract
public interface IQuery<TResult>
#pragma warning restore CA1040
{
}

/// <summary>Names the permission a query needs, TYPE.READ for example COMPANY.READ (R-SEC-01).</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class QueryAttribute : Attribute
{
    public QueryAttribute(string permissionCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permissionCode);
        PermissionCode = permissionCode;
    }

    public string PermissionCode { get; }
}

public interface IQueryContext
{
    Principal Principal { get; }

    /// <summary>The read's unit of work: one connection, one transaction, rolled back at the end (R-DB-01).</summary>
    IUnitOfWork UnitOfWork { get; }
}

public interface IQueryHandler<in TQuery, TResult>
    where TQuery : IQuery<TResult>
{
    Task<Result<TResult>> HandleAsync(TQuery query, IQueryContext context, CancellationToken cancellationToken);
}

/// <summary>Runs a query through the kernel pipeline: principal, permission, one read-only transaction, problem details on failure (R-SEC-01, R-API-03).</summary>
public interface IQueryDispatcher
{
    Task<Commands.CommandResponse> DispatchAsync<TQuery, TResult>(TQuery query, CorrelationId? correlationId, CancellationToken cancellationToken)
        where TQuery : IQuery<TResult>;
}

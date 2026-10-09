using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Themis.Kernel.Contracts.Persistence;

namespace Themis.Kernel.Persistence.UnitOfWork;

/// <summary>
/// The command interceptor the brief describes: before each statement a data context sends, switch to the context's
/// owning role when the unit of work's current role differs (R-DB-01, D-41).
/// </summary>
internal sealed class RoleSwitchInterceptor : DbCommandInterceptor
{
    private readonly NpgsqlUnitOfWork _unitOfWork;
    private readonly DatabaseRole _role;

    public RoleSwitchInterceptor(NpgsqlUnitOfWork unitOfWork, DatabaseRole role)
    {
        _unitOfWork = unitOfWork;
        _role = role;
    }

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        await _unitOfWork.EnsureRoleAsync(_role, cancellationToken);
        return result;
    }

    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        await _unitOfWork.EnsureRoleAsync(_role, cancellationToken);
        return result;
    }

    public override async ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
    {
        await _unitOfWork.EnsureRoleAsync(_role, cancellationToken);
        return result;
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        _unitOfWork.EnsureRole(_role);
        return result;
    }

    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        _unitOfWork.EnsureRole(_role);
        return result;
    }

    public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        _unitOfWork.EnsureRole(_role);
        return result;
    }
}

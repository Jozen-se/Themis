using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Themis.FixtureModule.Contracts;
using Themis.Kernel.Contracts.Persistence;

namespace Themis.FixtureModule.Infrastructure;

/// <summary>
/// The module data context's command interceptor (RR-62, R-DB-01): before each statement the context sends, switch to
/// themis_fixture through <see cref="IUnitOfWork.EnsureRoleAsync"/> whenever the current role differs, so a statement
/// after a call into the kernel, which hands the connection back as themis_app, still runs as the module. Only the
/// asynchronous paths switch: the fixture module uses no synchronous Entity Framework call, and a synchronous one
/// would run as themis_app and be refused (fail closed).
/// </summary>
internal sealed class FixtureRoleInterceptor : DbCommandInterceptor
{
    public static readonly DatabaseRole Role = DatabaseRole.From(FixtureContracts.RoleName);

    private readonly IUnitOfWork _unitOfWork;

    public FixtureRoleInterceptor(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        await _unitOfWork.EnsureRoleAsync(Role, cancellationToken);
        return result;
    }

    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        await _unitOfWork.EnsureRoleAsync(Role, cancellationToken);
        return result;
    }

    public override async ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
    {
        await _unitOfWork.EnsureRoleAsync(Role, cancellationToken);
        return result;
    }
}

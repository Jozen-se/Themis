using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Themis.FixtureModule.Application;
using Themis.FixtureModule.Domain;
using Themis.Kernel.Contracts.Persistence;

namespace Themis.FixtureModule.Infrastructure;

/// <summary>
/// The two read paths of the RR-62 probe. Raw ADO.NET: the connection from ForRoleAsync, the kernel call, then a
/// statement that does not switch, which runs as themis_app and is refused with 42501. Entity Framework: the fixture
/// context from ForRoleAsync, the kernel call, then a query whose interceptor switches back to themis_fixture first.
/// </summary>
internal sealed class RoleProbeStore : IRoleProbeStore
{
    public async Task<Counter?> ReadAfterKernelCallAsync(IUnitOfWork unitOfWork, Guid id, bool throughEntityFramework, Func<CancellationToken, Task> kernelCall, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(kernelCall);
        if (throughEntityFramework)
        {
            await using FixtureDbContext data = await FixtureDbContext.OnUnitOfWorkAsync(unitOfWork, cancellationToken);
            await kernelCall(cancellationToken);
            FixtureDbContext.CounterRow? row = await data.Counters.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
            return row?.ToCounter();
        }

        UnitOfWorkConnection shared = await unitOfWork.ForRoleAsync(FixtureRoleInterceptor.Role, cancellationToken);
        await kernelCall(cancellationToken);
        await using DbCommand command = shared.Connection.CreateCommand();
        command.Transaction = shared.Transaction;
        command.CommandText = $"SELECT value FROM {SqlCounterStore.Table} WHERE id = @id";
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = "id";
        parameter.Value = id;
        command.Parameters.Add(parameter);
        object? value = await command.ExecuteScalarAsync(cancellationToken);
        throw new InvalidOperationException($"The raw read after the kernel call returned {value}; it must be refused as themis_app (RR-62).");
    }
}

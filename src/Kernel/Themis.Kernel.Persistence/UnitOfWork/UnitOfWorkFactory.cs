using Npgsql;
using Themis.Kernel.Contracts.Persistence;

namespace Themis.Kernel.Persistence.UnitOfWork;

/// <summary>Opens the application's connection (themis_app) from the shared data source and begins the command's transaction.</summary>
internal sealed class UnitOfWorkFactory : IUnitOfWorkFactory
{
    private readonly NpgsqlDataSource _dataSource;

    public UnitOfWorkFactory(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<IKernelUnitOfWork> BeginAsync(CancellationToken cancellationToken) =>
        await NpgsqlUnitOfWork.BeginAsync(_dataSource, cancellationToken);
}

using Themis.FixtureModule.Domain;
using Themis.Kernel.Contracts.Persistence;

namespace Themis.FixtureModule.Application;

/// <summary>Persistence port of the fixture module; the implementation runs every statement as themis_fixture (R-DB-01).</summary>
internal interface ICounterStore
{
    Task<Counter?> FindAsync(IUnitOfWork unitOfWork, Guid id, CancellationToken cancellationToken);

    Task InsertAsync(IUnitOfWork unitOfWork, Counter counter, CancellationToken cancellationToken);

    Task UpdateAsync(IUnitOfWork unitOfWork, Counter counter, CancellationToken cancellationToken);

    /// <summary>Attempts a write to a kernel table as themis_fixture; the database must refuse it (I-14).</summary>
    Task WriteKernelTableAsync(IUnitOfWork unitOfWork, CancellationToken cancellationToken);

    /// <summary>Multiplies the counter's sample past its numeric(20,4) column, so PostgreSQL raises 22003 (RR-83).</summary>
    Task OverflowSampleAsync(IUnitOfWork unitOfWork, Guid id, CancellationToken cancellationToken);
}

using System.Data.Common;
using Themis.Kernel.Contracts.Organisation;
using Themis.Kernel.Contracts.Persistence;
using Themis.Kernel.Organisation;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Tests.Organisation;

/// <summary>RR-44: the period lookup asserts that exactly one period covers a date; the exclusion constraint makes more a defect, never a guess.</summary>
[Trait("Category", "Unit")]
public sealed class FiscalPeriodLookupTests
{
    [Fact]
    public async Task RR_44_the_period_lookup_refuses_more_than_one_covering_period_and_returns_the_single_one()
    {
        OrganisationReader reader = new();
        Guid company = Guid.NewGuid();
        BusinessDate date = new(new DateOnly(2026, 6, 15));
        object[] period = [Guid.NewGuid(), Guid.NewGuid(), 6, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), "Open"];

        Result<FiscalPeriodInfo> single = await reader.ResolveFiscalPeriodAsync(new RowsUnitOfWork([period]), company, date, CancellationToken.None);
        Assert.True(single.IsSuccess);
        Assert.Equal(6, single.Value.PeriodNumber);

        InvalidOperationException defect = await Assert.ThrowsAsync<InvalidOperationException>(() => reader.ResolveFiscalPeriodAsync(new RowsUnitOfWork([period, period]), company, date, CancellationToken.None));
        Assert.Contains("RR-44", defect.Message, StringComparison.Ordinal);

        Result<FiscalPeriodInfo> none = await reader.ResolveFiscalPeriodAsync(new RowsUnitOfWork([]), company, date, CancellationToken.None);
        Assert.Equal("fiscal_period_not_found", none.Primary!.Code);
    }

    /// <summary>A unit of work that answers every query with fixed rows, read through a record-backed data reader.</summary>
    private sealed class RowsUnitOfWork : IUnitOfWork
    {
        private readonly object[][] _rows;

        public RowsUnitOfWork(object[][] rows)
        {
            _rows = rows;
        }

        public DatabaseRole? CurrentRole => null;

        public DbConnection Connection => throw new NotSupportedException();

        public DbTransaction Transaction => throw new NotSupportedException();

        public Task EnsureRoleAsync(DatabaseRole role, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<int> ExecuteAsync(DatabaseRole role, string sql, IReadOnlyDictionary<string, object?>? parameters, CancellationToken cancellationToken) => Task.FromResult(0);

        public Task<object?> ScalarAsync(DatabaseRole role, string sql, IReadOnlyDictionary<string, object?>? parameters, CancellationToken cancellationToken) => Task.FromResult<object?>(null);

        public async Task<IReadOnlyList<T>> QueryAsync<T>(DatabaseRole role, string sql, IReadOnlyDictionary<string, object?>? parameters, Func<DbDataReader, T> map, CancellationToken cancellationToken)
        {
            System.Data.DataTable table = new();
            foreach ((string name, Type type) in new (string, Type)[] { ("id", typeof(Guid)), ("fiscal_year_id", typeof(Guid)), ("period_number", typeof(int)), ("start_date", typeof(DateOnly)), ("end_date", typeof(DateOnly)), ("status", typeof(string)) })
            {
                table.Columns.Add(name, type);
            }

            foreach (object[] row in _rows)
            {
                table.Rows.Add(row);
            }

            List<T> result = [];
            using DbDataReader reader = table.CreateDataReader();
            while (await reader.ReadAsync(cancellationToken))
            {
                result.Add(map(reader));
            }

            return result;
        }
    }
}

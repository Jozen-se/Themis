using Npgsql;
using Themis.Kernel.Contracts.Documents;
using Themis.Kernel.Contracts.Security;
using Themis.Kernel.Documents;
using Themis.Kernel.Organisation;
using Themis.Kernel.Persistence.UnitOfWork;
using Themis.Kernel.Primitives;

namespace Themis.Integration.Tests.Documents;

/// <summary>
/// Numbering on a unit of work (step 7 brief, Tests; R-DOC-07, R-LED-08): consecutive numbers from the locked
/// series row, a failed transaction returns its numbers, two concurrent commands taking both series in opposite
/// order complete without deadlock, a number beyond the padding grows, and a missing series is refused. Step 8 is
/// the first caller; these tests prove the service.
/// </summary>
[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Integration")]
public sealed class NumberSeriesServiceTests : IAsyncLifetime
{
    private static readonly Guid Company = Milestone1OrganisationFixture.CompanyId;
    private static readonly NumberRequest Adjustment = new(DocumentTypes.InventoryAdjustment);
    private static readonly NumberRequest Journal = new(DocumentTypes.JournalEntry, Milestone1OrganisationFixture.FiscalYearId);
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgresFixture _db;
    private readonly NumberSeriesService _service = new();
    private NpgsqlDataSource _dataSource = null!;

    public NumberSeriesServiceTests(PostgresFixture db)
    {
        _db = db;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await _db.TruncateAsync(Ct);
        _dataSource = NpgsqlDataSource.Create(_db.ConnectionStringFor("themis_app"));
    }

    public async ValueTask DisposeAsync() => await _dataSource.DisposeAsync();

    private Task<Result<IReadOnlyList<NumberTaken>>> TakeAsync(NpgsqlUnitOfWork unitOfWork, params NumberRequest[] requests) =>
        _service.TakeAsync(unitOfWork, Company, requests, Now, WellKnownUsers.System, Ct);

    private async Task<object?> ScalarAsync(string sql)
    {
        await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(_db.SuperuserConnectionString, Ct);
        await using NpgsqlCommand command = new(sql, connection);
        return await command.ExecuteScalarAsync(Ct);
    }

    [Fact]
    public async Task R_DOC_07_numbers_are_consecutive_from_the_locked_series_row_and_a_rolled_back_transaction_returns_them()
    {
        await using (NpgsqlUnitOfWork rolledBack = await NpgsqlUnitOfWork.BeginAsync(_dataSource, Ct))
        {
            Result<IReadOnlyList<NumberTaken>> taken = await TakeAsync(rolledBack, Adjustment, Journal);
            Assert.True(taken.IsSuccess);
            Assert.Equal(["IA-000001", "JE2026-000001"], taken.Value.Select(number => number.Number));
            await rolledBack.RollbackAsync(Ct);
        }

        await using (NpgsqlUnitOfWork first = await NpgsqlUnitOfWork.BeginAsync(_dataSource, Ct))
        {
            // The same series twice in one call gives consecutive numbers; the row advances once per call.
            Result<IReadOnlyList<NumberTaken>> taken = await TakeAsync(first, Adjustment, Adjustment, Journal);
            Assert.Equal(["IA-000001", "IA-000002", "JE2026-000001"], taken.Value.Select(number => number.Number));
            Assert.Equal([1L, 2L, 1L], taken.Value.Select(number => number.Sequence));
            Assert.Equal(Milestone1NumberSeriesFixture.InventoryAdjustmentSeriesId, taken.Value[0].SeriesId);
            Assert.Equal(Milestone1NumberSeriesFixture.JournalEntrySeriesId, taken.Value[2].SeriesId);
            // The series ran as themis_kernel (themis_app is refused on kernel.number_series); RR-62: the service hands the connection back as themis_app.
            Assert.Null(first.CurrentRole);
            await first.CommitAsync(Ct);
        }

        await using (NpgsqlUnitOfWork second = await NpgsqlUnitOfWork.BeginAsync(_dataSource, Ct))
        {
            Result<IReadOnlyList<NumberTaken>> taken = await TakeAsync(second, Journal, Adjustment);
            Assert.Equal(["JE2026-000002", "IA-000003"], taken.Value.Select(number => number.Number));
            await second.CommitAsync(Ct);
        }

        Assert.Equal(4L, await ScalarAsync($"SELECT next_number FROM kernel.number_series WHERE id = '{Milestone1NumberSeriesFixture.InventoryAdjustmentSeriesId}'"));
        Assert.Equal(3L, await ScalarAsync($"SELECT version FROM kernel.number_series WHERE id = '{Milestone1NumberSeriesFixture.InventoryAdjustmentSeriesId}'"));
        Assert.Equal(WellKnownUsers.System.Value, await ScalarAsync($"SELECT updated_by FROM kernel.number_series WHERE id = '{Milestone1NumberSeriesFixture.JournalEntrySeriesId}'"));
    }

    [Fact]
    public async Task R_LED_08_two_commands_taking_both_series_in_opposite_order_complete_without_deadlock()
    {
        await using NpgsqlUnitOfWork first = await NpgsqlUnitOfWork.BeginAsync(_dataSource, Ct);
        await using NpgsqlUnitOfWork second = await NpgsqlUnitOfWork.BeginAsync(_dataSource, Ct);

        Result<IReadOnlyList<NumberTaken>> firstTaken = await TakeAsync(first, Adjustment, Journal);
        // The second command asks in the opposite order; it waits on the row locks instead of deadlocking (one statement, primary key order).
        Task<Result<IReadOnlyList<NumberTaken>>> waiting = TakeAsync(second, Journal, Adjustment);
        await Task.Delay(TimeSpan.FromMilliseconds(300), Ct);
        Assert.False(waiting.IsCompleted);

        await first.CommitAsync(Ct);
        Result<IReadOnlyList<NumberTaken>> secondTaken = await waiting.WaitAsync(TimeSpan.FromSeconds(15), Ct);
        await second.CommitAsync(Ct);

        Assert.Equal(["IA-000001", "JE2026-000001"], firstTaken.Value.Select(number => number.Number));
        Assert.Equal(["JE2026-000002", "IA-000002"], secondTaken.Value.Select(number => number.Number));
    }

    [Fact]
    public async Task Padding_is_a_minimum_width_so_the_number_after_IA_999999_is_IA_1000000()
    {
        await ScalarAsync($"UPDATE kernel.number_series SET next_number = 999999 WHERE id = '{Milestone1NumberSeriesFixture.InventoryAdjustmentSeriesId}'");

        await using NpgsqlUnitOfWork unitOfWork = await NpgsqlUnitOfWork.BeginAsync(_dataSource, Ct);
        Result<IReadOnlyList<NumberTaken>> taken = await TakeAsync(unitOfWork, Adjustment, Adjustment);
        await unitOfWork.CommitAsync(Ct);

        Assert.Equal(["IA-999999", "IA-1000000"], taken.Value.Select(number => number.Number));
        Assert.Equal(1000001L, await ScalarAsync($"SELECT next_number FROM kernel.number_series WHERE id = '{Milestone1NumberSeriesFixture.InventoryAdjustmentSeriesId}'"));
    }

    [Fact]
    public async Task A_series_the_company_does_not_have_is_refused_and_nothing_is_taken()
    {
        await using NpgsqlUnitOfWork unitOfWork = await NpgsqlUnitOfWork.BeginAsync(_dataSource, Ct);

        Result<IReadOnlyList<NumberTaken>> otherYear = await TakeAsync(unitOfWork, Adjustment, new NumberRequest(DocumentTypes.JournalEntry, Guid.NewGuid()));
        Result<IReadOnlyList<NumberTaken>> otherCompany = await _service.TakeAsync(unitOfWork, Guid.NewGuid(), [Adjustment], Now, WellKnownUsers.System, Ct);
        Result<IReadOnlyList<NumberTaken>> none = await TakeAsync(unitOfWork);
        await unitOfWork.RollbackAsync(Ct);

        Assert.Equal(NumberSeriesService.SeriesMissing, otherYear.Primary!.Code);
        Assert.Equal("R-DOC-08", otherYear.Primary.RuleId);
        Assert.Equal(NumberSeriesService.SeriesMissing, otherCompany.Primary!.Code);
        Assert.True(none.IsSuccess);
        Assert.Empty(none.Value);
        Assert.Equal(1L, await ScalarAsync($"SELECT next_number FROM kernel.number_series WHERE id = '{Milestone1NumberSeriesFixture.InventoryAdjustmentSeriesId}'"));
    }
}

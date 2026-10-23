using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Themis.Integration.Tests.Pipeline;
using Themis.Kernel.Contracts.Persistence;
using Themis.Kernel.Organisation;

namespace Themis.Integration.Tests.Hardening;

/// <summary>
/// Read-only queries (RR-81): the query dispatcher's unit of work runs SET TRANSACTION READ ONLY right after BEGIN.
/// Role switches, advisory locks and set_config keep working; a write, a row lock or a temporary table is refused with 25006. The write-attempting
/// query seam gets 500 with 25006 in the log, and the counter it tried to change is unchanged. Every existing query
/// passes through the same dispatcher in its own tests.
/// </summary>
[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Integration")]
public sealed class ReadOnlyQueryTests : IAsyncLifetime
{
    private readonly PostgresFixture _db;
    private PipelineTestHost _host = null!;

    public ReadOnlyQueryTests(PostgresFixture db)
    {
        _db = db;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await _db.TruncateAsync(Ct);
        _host = await PipelineTestHost.StartAsync(_db);
    }

    public async ValueTask DisposeAsync()
    {
        await _db.TruncateAsync(CancellationToken.None);
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task RR_81_a_query_that_tries_to_write_gets_500_with_25006_in_the_log_and_changes_nothing()
    {
        Guid company = Milestone1OrganisationFixture.CompanyId;
        CommandReply created = await _host.PostAsync("/v1/fixture/counters", new { companyId = company, initialValue = 5, sample = "1" }, "rr81-create", cancellationToken: Ct);
        Assert.True(created.Status == 200, created.Body);

        CommandReply reply = await _host.GetAsync($"{WriteAttemptQueryRoutes.Query}?companyId={company}", cancellationToken: Ct);

        Assert.Equal(500, reply.Status);
        Assert.Contains(_host.Logs.Lines, line => line.Level == LogLevel.Error && line.Message.Contains("25006", StringComparison.Ordinal));
        Assert.Equal(5L, await _host.ScalarAsync("SELECT value FROM fixture.counter", Ct));
    }

    [Fact]
    public async Task RR_81_the_read_only_unit_of_work_switches_roles_takes_advisory_locks_and_sets_config_but_refuses_writes_row_locks_and_temporary_tables_with_25006()
    {
        IUnitOfWorkFactory factory = _host.Services.GetRequiredService<IUnitOfWorkFactory>();
        await using IKernelUnitOfWork unitOfWork = await factory.BeginReadOnlyAsync(Ct);

        Assert.Equal("on", await unitOfWork.ScalarAsync(DatabaseRole.Kernel, "SELECT current_setting('transaction_read_only')", null, Ct));
        Assert.Equal("themis_kernel", await unitOfWork.ScalarAsync(DatabaseRole.Kernel, "SELECT current_user", null, Ct));
        Assert.True(await unitOfWork.TryAdvisoryLockAsync(818181, Ct));
        Assert.Equal("rr81", await unitOfWork.ScalarAsync(DatabaseRole.Kernel, "SELECT set_config('themis.rr81_probe', 'rr81', true)", null, Ct));
        Assert.Equal("rr81", await unitOfWork.ScalarAsync(DatabaseRole.Kernel, "SELECT current_setting('themis.rr81_probe')", null, Ct));
        await unitOfWork.ResetRoleAsync(Ct);

        foreach (string sql in new[] { "UPDATE kernel.company SET code = code WHERE false", "SELECT id FROM kernel.company FOR UPDATE", "SELECT id FROM kernel.company FOR KEY SHARE", "CREATE TEMPORARY TABLE rr81_probe (x integer)" })
        {
            await unitOfWork.SavepointAsync("probe", Ct);
            PostgresException refused = await Assert.ThrowsAsync<PostgresException>(() => unitOfWork.ExecuteAsync(DatabaseRole.Kernel, sql, null, Ct));
            Assert.Equal((sql, "25006"), (sql, refused.SqlState));
            await unitOfWork.RollbackToSavepointAsync("probe", Ct);
        }

        await unitOfWork.RollbackAsync(Ct);
    }
}

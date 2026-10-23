using Microsoft.Extensions.Logging;
using Themis.Integration.Tests.Pipeline;
using Themis.Kernel.Organisation;

namespace Themis.Integration.Tests.Hardening;

/// <summary>
/// RR-83: only the posting service turns a numeric overflow (SQLSTATE 22003) from its own writes into the 422
/// amount_out_of_range under R-DOC-01 (the RR-46 test in PipelineHardeningTests proves it still does); the dispatcher
/// maps 22003 no longer, so an overflow anywhere else is a 500 and nothing is stored.
/// </summary>
[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Integration")]
public sealed class OverflowMappingTests : IAsyncLifetime
{
    private const string Counters = "/v1/fixture/counters";

    private readonly PostgresFixture _db;
    private PipelineTestHost _host = null!;

    public OverflowMappingTests(PostgresFixture db)
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
    public async Task RR_83_a_numeric_overflow_planted_in_a_fixture_command_is_500_and_nothing_is_stored()
    {
        CommandReply reply = await _host.PostAsync(Counters, new { companyId = Milestone1OrganisationFixture.CompanyId, initialValue = 1, sample = "1.5", failure = "NumericOverflowAfterWrite" }, "rr83-overflow", cancellationToken: Ct);

        Assert.Equal(500, reply.Status);
        Assert.NotEqual("amount_out_of_range", reply.Code);
        Assert.Contains(_host.Logs.Lines, line => line.Level == LogLevel.Error && line.Message.Contains("22003", StringComparison.Ordinal));
        Assert.Equal(0L, await _host.CountAsync("fixture.counter", Ct));
        Assert.Equal(0L, await _host.ScalarAsync("SELECT count(*) FROM kernel.idempotency_record WHERE key = 'rr83-overflow'", Ct));
    }
}

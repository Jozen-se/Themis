using Themis.Integration.Tests.Pipeline;
using Themis.Kernel.Organisation;

namespace Themis.Integration.Tests.Settings;

/// <summary>
/// The first setting and the purge (milestone 2 step 2; R-API-01, D-45 ends, RR-42, D-58): a record's expiry is
/// stamped from idempotency.retention_days when it is written, a later change leaves existing records alone, and the
/// purge removes expired records in bounded batches with its counter and log line as the only record, no audit event.
/// </summary>
[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Integration")]
public sealed class IdempotencyRetentionAndPurgeTests : IAsyncLifetime
{
    private const string Counters = "/v1/fixture/counters";
    private const string Set = "/v1/kernel/settings/set";

    private static readonly Guid Company = Milestone1OrganisationFixture.CompanyId;

    private readonly PostgresFixture _db;
    private PipelineTestHost _host = null!;

    public IdempotencyRetentionAndPurgeTests(PostgresFixture db)
    {
        _db = db;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await _db.TruncateAsync(Ct);
        _host = await PipelineTestHost.StartAsync(_db);
    }

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    private async Task CreateCounterAsync(string key) =>
        Assert.Equal(200, (await _host.PostAsync(Counters, new { companyId = Company, initialValue = 1, sample = "1" }, key, cancellationToken: Ct)).Status);

    [Fact]
    public async Task R_API_01_D_45_ends_the_retention_is_the_setting_a_change_applies_to_records_written_after_it_and_existing_records_keep_their_stamp()
    {
        await CreateCounterAsync("ret-before");
        Assert.Equal(7L, await DaysAsync("ret-before"));

        CommandReply set = await _host.PostAsync(Set, new { key = "idempotency.retention_days", level = "Installation", value = "1" }, "ret-set", ifMatch: "\"0\"", cancellationToken: Ct);
        Assert.Equal(200, set.Status);

        await CreateCounterAsync("ret-after");
        Assert.Equal(1L, await DaysAsync("ret-after"));
        Assert.Equal(7L, await DaysAsync("ret-before"));
    }

    [Fact]
    public async Task RR_42_the_purge_removes_only_expired_records_in_bounded_batches_moves_its_counter_logs_a_line_and_writes_no_audit_event()
    {
        foreach (string key in new[] { "purge-1", "purge-2", "purge-3", "purge-4", "purge-5" })
        {
            await CreateCounterAsync(key);
        }

        await _db.ExecuteAsMigratorAsync("UPDATE kernel.idempotency_record SET expires_at = now() - interval '1 hour' WHERE key IN ('purge-1', 'purge-2', 'purge-3')", Ct);
        long auditBefore = await _host.CountAsync("kernel.audit_event", Ct);

        Assert.Equal(2, await _host.Purge.PurgeExpiredAsync(batchSize: 2, Ct));
        Assert.Equal(2L, _host.Purged);
        Assert.Equal(3L, await _host.CountAsync("kernel.idempotency_record", Ct));

        Assert.Equal(1, await _host.Purge.PurgeExpiredAsync(batchSize: 2, Ct));
        Assert.Equal(0, await _host.Purge.PurgeExpiredAsync(batchSize: 2, Ct));
        Assert.Equal(3L, _host.Purged);
        Assert.Equal(2L, await _host.CountAsync("kernel.idempotency_record", Ct));
        Assert.Equal(0L, await _host.ScalarAsync("SELECT count(*) FROM kernel.idempotency_record WHERE expires_at <= now()", Ct));
        Assert.Equal(auditBefore, await _host.CountAsync("kernel.audit_event", Ct));
        Assert.Contains(_host.Logs.Lines, line => line.Category.EndsWith("IdempotencyPurgePass", StringComparison.Ordinal) && line.Message.Contains("removed 2 expired record(s)", StringComparison.Ordinal) && line.Message.Contains("RR-42", StringComparison.Ordinal));

        // The surviving records still replay (R-API-01).
        CommandReply replay = await _host.PostAsync(Counters, new { companyId = Company, initialValue = 1, sample = "1" }, "purge-4", cancellationToken: Ct);
        Assert.True(replay.Replayed);
    }

    private async Task<long> DaysAsync(string key) =>
        (long)(await _host.ScalarAsync("SELECT round(extract(epoch FROM (expires_at - now())) / 86400)::bigint FROM kernel.idempotency_record WHERE key = @key", Ct, ("key", key)))!;
}

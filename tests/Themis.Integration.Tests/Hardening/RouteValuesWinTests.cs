using Themis.Integration.Tests.Pipeline;
using Themis.Kernel.Organisation;

namespace Themis.Integration.Tests.Hardening;

/// <summary>
/// Route values win (RR-73): the command binder matches body members without regard to letter case, so before the
/// route values and the If-Match version are merged, every body member that names a route value in any case must
/// agree with it (ids compared as Guid values), else 400 route_body_mismatch with no ruleId and nothing stored; and
/// every member that names expectedVersion in any case is dropped, so only If-Match carries the version.
/// </summary>
[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Integration")]
public sealed class RouteValuesWinTests : IAsyncLifetime
{
    private const string Counters = "/v1/fixture/counters";

    private readonly PostgresFixture _db;
    private PipelineTestHost _host = null!;

    public RouteValuesWinTests(PostgresFixture db)
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

    [Theory]
    [InlineData("id")]
    [InlineData("ID")]
    [InlineData("Id")]
    public async Task RR_73_a_body_member_naming_the_route_id_in_any_case_with_another_value_is_400_and_not_stored(string member)
    {
        Guid counter = await CreateCounterAsync("rr73-create-" + member);
        Guid other = await CreateCounterAsync("rr73-other-" + member);
        string key = "rr73-mismatch-" + member;

        CommandReply reply = await _host.PostAsync($"{Counters}/{counter}/touch", $"{{\"{member}\": \"{other}\", \"increment\": 1}}", key, ifMatch: "\"1\"", cancellationToken: Ct);

        Assert.Equal(400, reply.Status);
        Assert.Equal("route_body_mismatch", reply.Code);
        Assert.Null(reply.RuleId);
        Assert.Equal(0L, await _host.ScalarAsync("SELECT count(*) FROM kernel.idempotency_record WHERE key = @key", Ct, ("key", key)));
        Assert.Equal(1L, await _host.ScalarAsync("SELECT version FROM fixture.counter WHERE id = @id", Ct, ("id", other)));
        Assert.Equal(1L, await _host.ScalarAsync("SELECT version FROM fixture.counter WHERE id = @id", Ct, ("id", counter)));
    }

    [Fact]
    public async Task RR_73_the_same_id_in_upper_case_agrees_with_the_route_and_the_command_runs()
    {
        Guid counter = await CreateCounterAsync("rr73-same-create");

        CommandReply reply = await _host.PostAsync($"{Counters}/{counter}/touch", $"{{\"ID\": \"{counter.ToString().ToUpperInvariant()}\", \"increment\": 2}}", "rr73-same", ifMatch: "\"1\"", cancellationToken: Ct);

        Assert.True(reply.Status == 200, reply.Body);
        Assert.Equal(counter, reply.Json!["id"]!.GetValue<Guid>());
        Assert.Equal(7L, await _host.ScalarAsync("SELECT value FROM fixture.counter WHERE id = @id", Ct, ("id", counter)));
    }

    [Fact]
    public async Task RR_73_ExpectedVersion_in_the_body_in_any_case_is_ignored_and_If_Match_decides()
    {
        Guid counter = await CreateCounterAsync("rr73-version-create");

        // The body claims version 1 under two spellings; If-Match says 2, which is stale: the header decides, so 412.
        CommandReply stale = await _host.PostAsync($"{Counters}/{counter}/touch", "{\"ExpectedVersion\": 1, \"EXPECTEDVERSION\": 1, \"increment\": 1}", "rr73-version-stale", ifMatch: "\"2\"", cancellationToken: Ct);
        // The body claims a stale version; If-Match carries the current one: the command runs.
        CommandReply current = await _host.PostAsync($"{Counters}/{counter}/touch", "{\"ExpectedVersion\": 7, \"increment\": 1}", "rr73-version-current", ifMatch: "\"1\"", cancellationToken: Ct);

        Assert.Equal(412, stale.Status);
        Assert.True(current.Status == 200, current.Body);
        Assert.Equal(2L, current.Json!["version"]!.GetValue<long>());
    }

    private async Task<Guid> CreateCounterAsync(string key)
    {
        CommandReply reply = await _host.PostAsync(Counters, new { companyId = Milestone1OrganisationFixture.CompanyId, initialValue = 5, sample = "1.5" }, key, cancellationToken: Ct);
        Assert.True(reply.Status == 200, reply.Body);
        return reply.Json!["id"]!.GetValue<Guid>();
    }
}

using Themis.Integration.Tests.Pipeline;
using Themis.Kernel.Organisation;

namespace Themis.Integration.Tests.Organisation;

/// <summary>
/// CHART_OF_ACCOUNTS.CREATE, UPDATE and READ end to end (milestone 2 step 3 brief, item 1): the header is created
/// with one audit event and one outbox event without a company (R-AUD-01, D-44, D-50), the code is unique in any
/// case (R-MD-05) and never changes (R-MD-06), UPDATE needs the current version (R-API-02), the installation scope
/// is satisfied by a company-wide assignment and by nothing less (D-47, RR-52), and the reads are ordered by code.
/// </summary>
[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Integration")]
public sealed class ChartOfAccountsCommandTests : IAsyncLifetime
{
    private static readonly Guid Bas = Milestone1OrganisationFixture.ChartOfAccountsId;

    private readonly PostgresFixture _db;
    private PipelineTestHost _host = null!;
    private OrganisationTestUsers _users = null!;

    public ChartOfAccountsCommandTests(PostgresFixture db)
    {
        _db = db;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await _db.TruncateAsync(Ct);
        _host = await PipelineTestHost.StartAsync(_db);
        _users = new OrganisationTestUsers(_db);
    }

    public async ValueTask DisposeAsync()
    {
        await _users.DisposeAsync();
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task CHART_OF_ACCOUNTS_CREATE_writes_the_header_with_one_audit_event_and_one_installation_scope_event_D_50_and_a_replay_writes_nothing()
    {
        CommandReply reply = await _host.PostAsync(OrganisationBodies.Charts, new { code = "k3", name = "K3-kontoplan", template = "K3 2026" }, "chart-1", correlationId: "corr-chart-1", cancellationToken: Ct);
        CommandReply replay = await _host.PostAsync(OrganisationBodies.Charts, new { code = "k3", name = "K3-kontoplan", template = "K3 2026" }, "chart-1", cancellationToken: Ct);

        Assert.True(reply.Status == 200, reply.Body);
        Assert.Equal("\"1\"", reply.ETag);
        Guid id = reply.Json!["id"]!.GetValue<Guid>();
        Assert.Equal("K3", reply.Json["code"]!.GetValue<string>());
        Assert.Equal("K3-kontoplan", reply.Json["name"]!.GetValue<string>());
        Assert.Equal("K3 2026", reply.Json["template"]!.GetValue<string>());
        Assert.Equal(1, reply.Json["version"]!.GetValue<long>());
        Assert.Equal(2L, await _host.CountAsync("kernel.chart_of_accounts", Ct));
        Assert.Equal(TestUsers.OrganisationAdministrator, await _host.ScalarAsync("SELECT created_by FROM kernel.chart_of_accounts WHERE id = @id", Ct, ("id", id)));

        // R-AUD-01, D-44: one audit event with every field from nothing, one event; D-50: a chart is an installation-scope aggregate.
        const string audit = "FROM kernel.audit_event WHERE command_name = 'CHART_OF_ACCOUNTS.CREATE'";
        Assert.Equal(1L, await _host.ScalarAsync($"SELECT count(*) {audit}", Ct));
        Assert.Equal(id, await _host.ScalarAsync($"SELECT entity_id {audit} AND entity_type = 'ChartOfAccounts'", Ct));
        Assert.Equal(TestUsers.OrganisationAdministrator, await _host.ScalarAsync($"SELECT user_id {audit}", Ct));
        Assert.Equal("corr-chart-1", await _host.ScalarAsync($"SELECT correlation_id {audit}", Ct));
        Assert.Equal("K3", await _host.ScalarAsync($"SELECT changes->'code'->>'after' {audit}", Ct));
        Assert.Null(await _host.ScalarAsync($"SELECT changes->'code'->>'before' {audit}", Ct));
        Assert.Equal("1", await _host.ScalarAsync($"SELECT changes->'version'->>'after' {audit}", Ct));
        Assert.Equal(1L, await _host.CountAsync("kernel.outbox_event", Ct));
        Assert.Equal(("CHART_OF_ACCOUNTS_CREATED", "CHART_OF_ACCOUNTS", id, null, "CHART_OF_ACCOUNTS.CREATE"), await EnvelopeAsync("CHART_OF_ACCOUNTS_CREATED"));
        Assert.Equal("K3", await _host.ScalarAsync("SELECT payload->>'code' FROM kernel.outbox_event", Ct));

        // R-API-01: the replay returns the stored result and writes nothing.
        Assert.Equal(200, replay.Status);
        Assert.True(replay.Replayed);
        Assert.Equal(reply.Body, replay.Body);
        Assert.Equal(2L, await _host.CountAsync("kernel.chart_of_accounts", Ct));
        Assert.Equal(1L, await _host.CountAsync("kernel.outbox_event", Ct));

        // CHART_OF_ACCOUNTS.READ: one by id, all ordered by code.
        CommandReply read = await _host.GetAsync($"{OrganisationBodies.Charts}/{id}", cancellationToken: Ct);
        CommandReply list = await _host.GetAsync(OrganisationBodies.Charts, cancellationToken: Ct);
        Assert.Equal(200, read.Status);
        Assert.Equal(reply.Body, read.Body);
        Assert.Equal("\"1\"", read.ETag);
        Assert.Equal(200, list.Status);
        Assert.Equal(["BAS", "K3"], list.Json!.AsArray().Select(chart => chart!["code"]!.GetValue<string>()));
    }

    [Fact]
    public async Task R_MD_05_a_duplicate_chart_code_in_any_case_is_422_with_the_ruleId_and_stored_as_the_keys_result()
    {
        CommandReply duplicate = await _host.PostAsync(OrganisationBodies.Charts, new { code = "bas", name = "Another BAS" }, "chart-dup", cancellationToken: Ct);
        CommandReply replay = await _host.PostAsync(OrganisationBodies.Charts, new { code = "bas", name = "Another BAS" }, "chart-dup", cancellationToken: Ct);

        Assert.Equal(422, duplicate.Status);
        Assert.Equal("application/problem+json", duplicate.MediaType);
        Assert.Equal(("R-MD-05", "chart_of_accounts_code_duplicate"), (duplicate.RuleId, duplicate.Code));
        Assert.Equal(1L, await _host.CountAsync("kernel.chart_of_accounts", Ct));
        Assert.Equal(0L, await _host.CountAsync("kernel.outbox_event", Ct));
        Assert.Equal(422, replay.Status);
        Assert.True(replay.Replayed);
        Assert.Equal("chart_of_accounts_code_duplicate", replay.Code);
    }

    [Fact]
    public async Task R_API_02_UPDATE_needs_If_Match_428_a_stale_version_is_412_and_not_stored_and_the_current_one_advances_the_version_with_its_audit_event_and_event()
    {
        var body = new { name = "BAS-kontoplan 2027", template = "BAS 2027" };
        CommandReply missing = await _host.PostAsync(OrganisationBodies.ChartUpdate(Bas), body, "chart-428", cancellationToken: Ct);
        CommandReply stale = await _host.PostAsync(OrganisationBodies.ChartUpdate(Bas), body, "chart-412", ifMatch: "\"7\"", cancellationToken: Ct);
        CommandReply unknown = await _host.PostAsync(OrganisationBodies.ChartUpdate(Guid.NewGuid()), body, "chart-404", ifMatch: "\"1\"", cancellationToken: Ct);
        CommandReply updated = await _host.PostAsync(OrganisationBodies.ChartUpdate(Bas), body, "chart-412", ifMatch: "\"1\"", cancellationToken: Ct);

        Assert.Equal(428, missing.Status);
        Assert.Equal("if_match_required", missing.Code);
        Assert.Equal(412, stale.Status);
        Assert.Equal(("R-API-02", "version_stale"), (stale.RuleId, stale.Code));
        Assert.Equal(404, unknown.Status);
        Assert.Equal("chart_of_accounts_not_found", unknown.Code);
        // The 412 was never the key's result: the same key with the right version runs the command.
        Assert.Equal(200, updated.Status);
        Assert.False(updated.Replayed);
        Assert.Equal("\"2\"", updated.ETag);
        Assert.Equal("BAS-kontoplan 2027", updated.Json!["name"]!.GetValue<string>());
        Assert.Equal("BAS", updated.Json["code"]!.GetValue<string>());
        Assert.Equal(2, updated.Json["version"]!.GetValue<long>());
        Assert.Equal(2L, await _host.ScalarAsync("SELECT version FROM kernel.chart_of_accounts WHERE id = @id", Ct, ("id", Bas)));

        // R-AUD-01: the fields whose value differs and the version, nothing else.
        const string audit = "FROM kernel.audit_event WHERE command_name = 'CHART_OF_ACCOUNTS.UPDATE'";
        Assert.Equal(1L, await _host.ScalarAsync($"SELECT count(*) {audit}", Ct));
        Assert.Equal(("BAS-kontoplan", "BAS-kontoplan 2027"), ((string?)await _host.ScalarAsync($"SELECT changes->'name'->>'before' {audit}", Ct), (string?)await _host.ScalarAsync($"SELECT changes->'name'->>'after' {audit}", Ct)));
        Assert.Equal(("1", "2"), ((string?)await _host.ScalarAsync($"SELECT changes->'version'->>'before' {audit}", Ct), (string?)await _host.ScalarAsync($"SELECT changes->'version'->>'after' {audit}", Ct)));
        Assert.Equal(["name", "template", "version"], ((string)(await _host.ScalarAsync($"SELECT string_agg(key, ',' ORDER BY key) FROM kernel.audit_event, jsonb_object_keys(changes) key WHERE command_name = 'CHART_OF_ACCOUNTS.UPDATE'", Ct))!).Split(','));
        Assert.Equal(("CHART_OF_ACCOUNTS_UPDATED", "CHART_OF_ACCOUNTS", Bas, null, "CHART_OF_ACCOUNTS.UPDATE"), await EnvelopeAsync("CHART_OF_ACCOUNTS_UPDATED"));
    }

    [Fact]
    public async Task R_MD_06_a_differing_code_on_UPDATE_is_422_chart_of_accounts_code_fixed_and_an_equal_code_in_any_case_is_ignored()
    {
        CommandReply changed = await _host.PostAsync(OrganisationBodies.ChartUpdate(Bas), new { code = "BAS2", name = "BAS-kontoplan" }, "chart-code", ifMatch: "\"1\"", cancellationToken: Ct);
        CommandReply same = await _host.PostAsync(OrganisationBodies.ChartUpdate(Bas), new { code = "bas", name = "BAS-kontoplan", template = "BAS 2026" }, "chart-same", ifMatch: "\"1\"", cancellationToken: Ct);

        Assert.Equal(422, changed.Status);
        Assert.Equal(("R-MD-06", "chart_of_accounts_code_fixed"), (changed.RuleId, changed.Code));
        Assert.Equal(1L, await _host.ScalarAsync("SELECT count(*) FROM kernel.idempotency_record WHERE key = 'chart-code'", Ct));
        Assert.Equal(200, same.Status);
        Assert.Equal("BAS", same.Json!["code"]!.GetValue<string>());
        Assert.Equal(2, same.Json["version"]!.GetValue<long>());
    }

    [Fact]
    public async Task A_shape_error_is_400_before_the_transaction_and_not_stored_so_the_corrected_retry_runs()
    {
        CommandReply invalid = await _host.PostAsync(OrganisationBodies.Charts, new { name = " " }, "chart-400", cancellationToken: Ct);
        long storedAfterRejection = await _host.CountAsync("kernel.idempotency_record", Ct);
        CommandReply retry = await _host.PostAsync(OrganisationBodies.Charts, new { code = "K2", name = "K2-kontoplan" }, "chart-400", cancellationToken: Ct);

        Assert.Equal(400, invalid.Status);
        Assert.Null(invalid.RuleId);
        Assert.Equal(["code_required", "name_required"], invalid.Json!["errors"]!.AsArray().Select(error => error!["code"]!.GetValue<string>()).Order());
        Assert.Equal(0L, storedAfterRejection);
        Assert.Equal(200, retry.Status);
        Assert.False(retry.Replayed);
    }

    [Fact]
    public async Task R_SEC_01_RR_52_the_installation_scope_is_satisfied_by_a_company_wide_assignment_not_by_a_site_limited_one_and_the_permission_is_checked_first()
    {
        string siteLimited = await _users.AddAdministratorAsync(Milestone1OrganisationFixture.CompanyId, Milestone1OrganisationFixture.SiteId, Ct);

        CommandReply clerk = await _host.PostAsync(OrganisationBodies.Charts, new { code = "K3", name = "K3" }, "chart-clerk", subject: TestUsers.ClerkSubject, cancellationToken: Ct);
        CommandReply limited = await _host.PostAsync(OrganisationBodies.Charts, new { code = "K3", name = "K3" }, "chart-limited", subject: siteLimited, cancellationToken: Ct);
        CommandReply limitedRead = await _host.GetAsync(OrganisationBodies.Charts, subject: siteLimited, cancellationToken: Ct);
        CommandReply preparerRead = await _host.GetAsync($"{OrganisationBodies.Charts}/{Bas}", subject: TestUsers.PreparerSubject, cancellationToken: Ct);
        CommandReply companyWide = await _host.PostAsync(OrganisationBodies.Charts, new { code = "K3", name = "K3" }, "chart-wide", cancellationToken: Ct);
        CommandReply unknown = await _host.GetAsync($"{OrganisationBodies.Charts}/{Guid.NewGuid()}", cancellationToken: Ct);
        CommandReply anonymous = await _host.GetAsync(OrganisationBodies.Charts, token: "not-a-token", cancellationToken: Ct);

        Assert.Equal(403, clerk.Status);
        Assert.Equal(("R-SEC-01", "permission_missing"), (clerk.RuleId, clerk.Code));
        Assert.Equal(403, limited.Status);
        Assert.Equal(("R-SEC-01", "out_of_scope"), (limited.RuleId, limited.Code));
        Assert.Equal(403, limitedRead.Status);
        Assert.Equal("out_of_scope", limitedRead.Code);
        Assert.Equal(403, preparerRead.Status);
        Assert.Equal("permission_missing", preparerRead.Code);
        Assert.Equal(200, companyWide.Status);
        Assert.Equal(404, unknown.Status);
        Assert.Equal("chart_of_accounts_not_found", unknown.Code);
        Assert.Equal(401, anonymous.Status);
        // A 403 is decided before the transaction: nothing is stored for the refused keys.
        Assert.Equal(1L, await _host.CountAsync("kernel.idempotency_record", Ct));
    }

    private Task<(string Type, string Aggregate, Guid Id, Guid? Company, string Command)> EnvelopeAsync(string eventType) => OrganisationBodies.EnvelopeAsync(_host, eventType, Ct);
}

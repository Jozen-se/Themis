using System.Text.Json.Nodes;
using Themis.Integration.Tests.Pipeline;
using Themis.Kernel.Organisation;

namespace Themis.Integration.Tests.Settings;

/// <summary>
/// SETTING.SET, SETTING.CLEAR and SETTING.READ end to end through the pipeline (milestone 2 step 2 brief; D-58,
/// R-SET-01, R-API-02): the clerk, who holds the settings administrator role, writes installation and company
/// values under If-Match; a 400 for a bad value, a 422 under R-SET-01 for a locked definition, a level not allowed
/// or a Column-backed definition; 412 for a stale version and for the lost concurrent race, 428 without If-Match;
/// one audit event and one SETTING_CHANGED outbox event per write; the preparer gets 403.
/// </summary>
[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Integration")]
public sealed class SettingCommandTests : IAsyncLifetime
{
    private const string Set = "/v1/kernel/settings/set";
    private const string Clear = "/v1/kernel/settings/clear";
    private const string Retention = "idempotency.retention_days";
    private const string MaxLines = "document.max_lines";

    private static readonly Guid Company = Milestone1OrganisationFixture.CompanyId;

    private readonly PostgresFixture _db;
    private PipelineTestHost _host = null!;

    public SettingCommandTests(PostgresFixture db)
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

    private static object Installation(string key, string value) => new { key, level = "Installation", value };

    private static object AtCompany(string key, string value) => new { key, level = "Company", companyId = Company, value };

    [Fact]
    public async Task D_58_RR_101_the_settings_administrator_sets_an_installation_value_under_If_Match_0_with_one_audit_event_and_one_SETTING_CHANGED_event_and_a_replay_writes_nothing()
    {
        CommandReply reply = await _host.PostAsync(Set, Installation(Retention, "30"), "set-1", ifMatch: "\"0\"", correlationId: "corr-set-1", cancellationToken: Ct);

        Assert.Equal(200, reply.Status);
        Assert.Equal("\"1\"", reply.ETag);
        Assert.Equal("30", reply.Json!["value"]!.GetValue<string>());
        Assert.Equal("Set", reply.Json["kind"]!.GetValue<string>());
        Assert.Equal(1, reply.Json["version"]!.GetValue<int>());
        Guid id = reply.Json["id"]!.GetValue<Guid>();

        Assert.Equal(1L, await _host.CountAsync("kernel.setting_value", Ct));
        Assert.Equal("30", await _host.ScalarAsync("SELECT value FROM kernel.setting_value WHERE id = @id", Ct, ("id", id)));
        Assert.Equal(TestUsers.SettingsAdministrator, await _host.ScalarAsync("SELECT recorded_by FROM kernel.setting_value WHERE id = @id", Ct, ("id", id)));

        // R-AUD-01, D-44, D-50: one audit event for the row, one outbox event with the envelope and an empty company for an installation value.
        const string audit = "FROM kernel.audit_event WHERE command_name = 'SETTING.SET'";
        Assert.Equal(1L, await _host.ScalarAsync($"SELECT count(*) {audit}", Ct));
        Assert.Equal(id, await _host.ScalarAsync($"SELECT entity_id {audit} AND entity_type = 'SettingValue'", Ct));
        Assert.Equal("30", await _host.ScalarAsync($"SELECT changes->'value'->>'after' {audit}", Ct));
        Assert.Null(await _host.ScalarAsync($"SELECT changes->'value'->>'before' {audit}", Ct));
        Assert.Equal("1", await _host.ScalarAsync($"SELECT changes->'version'->>'after' {audit}", Ct));
        Assert.Equal(1L, await _host.CountAsync("kernel.outbox_event", Ct));
        Assert.Equal("SETTING_CHANGED", await _host.ScalarAsync("SELECT event_type FROM kernel.outbox_event", Ct));
        Assert.Equal("SETTING", await _host.ScalarAsync("SELECT aggregate_type FROM kernel.outbox_event", Ct));
        Assert.Equal(id, await _host.ScalarAsync("SELECT aggregate_id FROM kernel.outbox_event", Ct));
        Assert.Null(await _host.ScalarAsync("SELECT company_id FROM kernel.outbox_event", Ct));
        Assert.Equal("SETTING.SET", await _host.ScalarAsync("SELECT command_name FROM kernel.outbox_event", Ct));
        Assert.Equal("corr-set-1", await _host.ScalarAsync("SELECT correlation_id FROM kernel.outbox_event", Ct));
        Assert.Equal(Retention, await _host.ScalarAsync("SELECT payload->>'key' FROM kernel.outbox_event", Ct));
        Assert.Equal("30", await _host.ScalarAsync("SELECT payload->>'newValue' FROM kernel.outbox_event", Ct));
        Assert.Null(await _host.ScalarAsync("SELECT payload->>'previousValue' FROM kernel.outbox_event", Ct));

        CommandReply replay = await _host.PostAsync(Set, Installation(Retention, "30"), "set-1", ifMatch: "\"0\"", cancellationToken: Ct);
        Assert.True(replay.Replayed);
        Assert.Equal(1L, await _host.CountAsync("kernel.setting_value", Ct));
    }

    [Fact]
    public async Task R_API_02_the_first_write_to_an_empty_slot_needs_If_Match_0_a_stale_version_is_412_and_not_stored_and_a_missing_header_is_428()
    {
        CommandReply missing = await _host.PostAsync(Set, Installation(Retention, "30"), "set-428", cancellationToken: Ct);
        Assert.Equal(428, missing.Status);
        Assert.Equal("if_match_required", missing.Code);

        CommandReply stale = await _host.PostAsync(Set, Installation(Retention, "30"), "set-412", ifMatch: "\"1\"", cancellationToken: Ct);
        Assert.Equal(412, stale.Status);
        Assert.Equal("version_stale", stale.Code);
        Assert.Equal("R-API-02", stale.RuleId);
        Assert.Equal(0L, await _host.CountAsync("kernel.setting_value", Ct));
        Assert.Equal(0L, await _host.ScalarAsync("SELECT count(*) FROM kernel.idempotency_record WHERE key = 'set-412'", Ct));

        // The same key with the right version runs the command: the 412 was never the key's result.
        CommandReply fresh = await _host.PostAsync(Set, Installation(Retention, "30"), "set-412", ifMatch: "\"0\"", cancellationToken: Ct);
        Assert.Equal(200, fresh.Status);
        Assert.False(fresh.Replayed);

        // The second write to the slot carries the version the first returned.
        CommandReply second = await _host.PostAsync(Set, Installation(Retention, "45"), "set-2", ifMatch: "\"1\"", cancellationToken: Ct);
        Assert.Equal(200, second.Status);
        Assert.Equal("\"2\"", second.ETag);
        Assert.Equal("30", await _host.ScalarAsync("SELECT payload->>'previousValue' FROM kernel.outbox_event WHERE payload->>'newValue' = '45'", Ct));
    }

    [Fact]
    public async Task R_API_02_a_write_that_loses_the_race_on_the_slot_version_gets_412_from_the_unique_index_and_nothing_is_stored_RR_45()
    {
        // The seam pauses the first SET after its version check; a second row for the slot lands meanwhile, so the insert
        // meets ux_setting_value_slot_version and the pipeline maps the unique violation to the same 412 (RR-45).
        TaskCompletionSource paused = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int holds = 0;
        _host.SettingWriteSeam.AfterVersionCheck = async (key, _, cancellationToken) =>
        {
            if (key == MaxLines && Interlocked.Exchange(ref holds, 1) == 0)
            {
                paused.TrySetResult();
                await release.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            }
        };

        Task<CommandReply> loser = _host.PostAsync(Set, Installation(MaxLines, "500"), "race-loser", ifMatch: "\"0\"", cancellationToken: Ct);
        await paused.Task.WaitAsync(TimeSpan.FromSeconds(15), Ct);
        CommandReply winner = await _host.PostAsync(Set, Installation(MaxLines, "600"), "race-winner", ifMatch: "\"0\"", cancellationToken: Ct);
        Assert.Equal(200, winner.Status);
        release.TrySetResult();
        CommandReply lost = await loser;

        Assert.Equal(412, lost.Status);
        Assert.Equal("version_stale", lost.Code);
        Assert.Equal("R-API-02", lost.RuleId);
        Assert.Equal(1L, await _host.CountAsync("kernel.setting_value", Ct));
        Assert.Equal("600", await _host.ScalarAsync("SELECT value FROM kernel.setting_value", Ct));
        Assert.Equal(0L, await _host.ScalarAsync("SELECT count(*) FROM kernel.idempotency_record WHERE key = 'race-loser'", Ct));
        Assert.Contains(_host.Logs.Lines, line => line.Message.Contains("23505", StringComparison.Ordinal) && line.Message.Contains("ux_setting_value_slot_version", StringComparison.Ordinal));
    }

    [Fact]
    public async Task R_SET_01_a_locked_definition_a_level_not_allowed_and_a_Column_backed_definition_are_422_and_stored_as_the_keys_result()
    {
        CommandReply locked = await _host.PostAsync(Set, AtCompany("inventory.negative_stock_allowed", "true"), "set-locked", ifMatch: "\"0\"", cancellationToken: Ct);
        Assert.Equal(422, locked.Status);
        Assert.Equal("R-SET-01", locked.RuleId);
        Assert.Equal("setting_locked", locked.Code);
        Assert.Contains("D-09", locked.Json!["detail"]!.GetValue<string>(), StringComparison.Ordinal);

        CommandReply level = await _host.PostAsync(Set, AtCompany(Retention, "30"), "set-level", ifMatch: "\"0\"", cancellationToken: Ct);
        Assert.Equal(422, level.Status);
        Assert.Equal("R-SET-01", level.RuleId);
        Assert.Equal("setting_level_not_allowed", level.Code);

        CommandReply column = await _host.PostAsync(Set, AtCompany("company.rounding_mode", "HalfEven"), "set-column", ifMatch: "\"0\"", cancellationToken: Ct);
        Assert.Equal(422, column.Status);
        Assert.Equal("R-SET-01", column.RuleId);
        Assert.Equal("setting_not_settable", column.Code);

        CommandReply planned = await _host.PostAsync(Clear, new { key = "sales.credit_check_mode", level = "Company", companyId = Company }, "clear-planned", ifMatch: "\"0\"", cancellationToken: Ct);
        Assert.Equal(422, planned.Status);
        Assert.Equal("setting_not_settable", planned.Code);

        Assert.Equal(0L, await _host.CountAsync("kernel.setting_value", Ct));
        Assert.Equal(4L, await _host.ScalarAsync("SELECT count(*) FROM kernel.idempotency_record WHERE key LIKE 'set-%' OR key LIKE 'clear-%'", Ct));
        CommandReply replay = await _host.PostAsync(Set, AtCompany(Retention, "30"), "set-level", ifMatch: "\"0\"", cancellationToken: Ct);
        Assert.True(replay.Replayed);
        Assert.Equal(422, replay.Status);
    }

    [Fact]
    public async Task D_58_D_63_a_value_of_the_wrong_type_or_outside_its_range_an_unknown_key_and_a_target_that_does_not_fit_the_level_are_400()
    {
        CommandReply above = await _host.PostAsync(Set, Installation(Retention, "91"), "set-91", ifMatch: "\"0\"", cancellationToken: Ct);
        Assert.Equal(400, above.Status);
        Assert.Equal("setting_value_invalid", above.Code);
        Assert.Null(above.RuleId);
        Assert.Equal("value", above.Json!["errors"]![0]!["details"]!["field"]!.GetValue<string>());
        Assert.Equal("90", above.Json["errors"]![0]!["details"]!["max"]!.GetValue<string>());

        CommandReply text = await _host.PostAsync(Set, Installation(Retention, "soon"), "set-text", ifMatch: "\"0\"", cancellationToken: Ct);
        Assert.Equal(400, text.Status);
        Assert.Equal("setting_value_invalid", text.Code);

        CommandReply unknown = await _host.PostAsync(Set, Installation("nothing.here", "1"), "set-unknown", ifMatch: "\"0\"", cancellationToken: Ct);
        Assert.Equal(400, unknown.Status);
        Assert.Equal("setting_unknown", unknown.Code);

        // RR-51: a shape error is pure validation, answered before any statement and never stored.
        CommandReply shape = await _host.PostAsync(Set, new { key = Retention, level = "Company", value = "30" }, "set-shape", ifMatch: "\"0\"", cancellationToken: Ct);
        Assert.Equal(400, shape.Status);
        Assert.Equal("setting_target_invalid", shape.Code);
        Assert.Equal(0L, await _host.ScalarAsync("SELECT count(*) FROM kernel.idempotency_record WHERE key = 'set-shape'", Ct));

        CommandReply validFrom = await _host.PostAsync(Set, new { key = Retention, level = "Installation", value = "30", validFrom = "2026-01-01" }, "set-validfrom", ifMatch: "\"0\"", cancellationToken: Ct);
        Assert.Equal(400, validFrom.Status);
        Assert.Equal("valid_from_not_allowed", validFrom.Code);

        Assert.Equal(0L, await _host.CountAsync("kernel.setting_value", Ct));
    }

    [Fact]
    public async Task D_63_SETTING_CLEAR_inserts_a_Cleared_row_so_the_level_falls_through_and_clearing_an_empty_slot_is_400_setting_value_absent()
    {
        Assert.Equal(200, (await _host.PostAsync(Set, Installation(MaxLines, "50"), "set-ml", ifMatch: "\"0\"", cancellationToken: Ct)).Status);
        Assert.Equal(200, (await _host.PostAsync(Set, AtCompany(MaxLines, "20"), "set-ml-c", ifMatch: "\"0\"", cancellationToken: Ct)).Status);

        CommandReply read = await _host.GetAsync($"/v1/kernel/settings/{MaxLines}?companyId={Company}", cancellationToken: Ct);
        Assert.Equal(200, read.Status);
        Assert.Equal("20", read.Json!["value"]!.GetValue<string>());
        Assert.Equal("Company", read.Json["source"]!.GetValue<string>());

        CommandReply cleared = await _host.PostAsync(Clear, new { key = MaxLines, level = "Company", companyId = Company }, "clear-ml-c", ifMatch: "\"1\"", cancellationToken: Ct);
        Assert.Equal(200, cleared.Status);
        Assert.Equal("Cleared", cleared.Json!["kind"]!.GetValue<string>());
        Assert.Null(cleared.Json["value"]);
        Assert.Equal("\"2\"", cleared.ETag);
        Assert.Equal("20", await _host.ScalarAsync("SELECT payload->>'previousValue' FROM kernel.outbox_event WHERE payload->>'kind' = 'Cleared'", Ct));

        read = await _host.GetAsync($"/v1/kernel/settings/{MaxLines}?companyId={Company}", cancellationToken: Ct);
        Assert.Equal("50", read.Json!["value"]!.GetValue<string>());
        Assert.Equal("Installation", read.Json["source"]!.GetValue<string>());
        JsonArray levels = read.Json["levels"]!.AsArray();
        Assert.Equal(["Installation", "Company"], levels.Select(level => level!["level"]!.GetValue<string>()));
        Assert.Equal([1, 2], levels.Select(level => level!["version"]!.GetValue<int>()));
        Assert.Equal("Cleared", levels[1]!["kind"]!.GetValue<string>());

        CommandReply empty = await _host.PostAsync(Clear, new { key = Retention, level = "Installation" }, "clear-empty", ifMatch: "\"0\"", cancellationToken: Ct);
        Assert.Equal(400, empty.Status);
        Assert.Equal("setting_value_absent", empty.Code);
        Assert.Equal(3L, await _host.CountAsync("kernel.setting_value", Ct));

        // D-63: CLEAR of an unknown key is a 400, like SET of one.
        CommandReply unknown = await _host.PostAsync(Clear, new { key = "nothing.here", level = "Installation" }, "clear-unknown", ifMatch: "\"0\"", cancellationToken: Ct);
        Assert.Equal(400, unknown.Status);
        Assert.Equal("setting_unknown", unknown.Code);
    }

    [Fact]
    public async Task D_63_SETTING_READ_returns_the_default_the_column_value_and_the_definition_shape_an_unknown_key_is_404_and_the_preparer_may_neither_read_nor_write()
    {
        CommandReply retention = await _host.GetAsync($"/v1/kernel/settings/{Retention}", cancellationToken: Ct);
        Assert.Equal(200, retention.Status);
        Assert.Equal("7", retention.Json!["value"]!.GetValue<string>());
        Assert.Equal("Default", retention.Json["source"]!.GetValue<string>());
        Assert.Equal("Integer", retention.Json["valueType"]!.GetValue<string>());
        Assert.Equal(["Installation"], retention.Json["allowedLevels"]!.AsArray().Select(level => level!.GetValue<string>()));
        Assert.Equal(0, retention.Json["levels"]![0]!["version"]!.GetValue<int>());

        // A level the definition does not allow is skipped when reading: the company target shows the installation level alone.
        CommandReply atCompany = await _host.GetAsync($"/v1/kernel/settings/{Retention}?companyId={Company}", cancellationToken: Ct);
        Assert.Equal(200, atCompany.Status);
        Assert.Equal(["Installation"], atCompany.Json!["levels"]!.AsArray().Select(level => level!["level"]!.GetValue<string>()));

        CommandReply column = await _host.GetAsync($"/v1/kernel/settings/company.rounding_mode?companyId={Company}", cancellationToken: Ct);
        Assert.Equal(200, column.Status);
        Assert.Equal("HalfAwayFromZero", column.Json!["value"]!.GetValue<string>());
        Assert.Equal("Column", column.Json["source"]!.GetValue<string>());
        Assert.Equal("Column", column.Json["backing"]!.GetValue<string>());

        CommandReply locked = await _host.GetAsync($"/v1/kernel/settings/inventory.negative_stock_allowed?companyId={Company}", cancellationToken: Ct);
        Assert.Equal("false", locked.Json!["value"]!.GetValue<string>());
        Assert.Contains("D-09", locked.Json["lockedReason"]!.GetValue<string>(), StringComparison.Ordinal);

        CommandReply unknown = await _host.GetAsync("/v1/kernel/settings/nothing.here", cancellationToken: Ct);
        Assert.Equal(404, unknown.Status);

        // R-SEC-01: the preparer holds no settings role.
        CommandReply forbidden = await _host.PostAsync(Set, Installation(Retention, "30"), "set-preparer", ifMatch: "\"0\"", subject: TestUsers.PreparerSubject, cancellationToken: Ct);
        Assert.Equal(403, forbidden.Status);
        Assert.Equal("permission_missing", forbidden.Code);
        CommandReply forbiddenRead = await _host.GetAsync($"/v1/kernel/settings/{Retention}", subject: TestUsers.PreparerSubject, cancellationToken: Ct);
        Assert.Equal(403, forbiddenRead.Status);
    }

    [Fact]
    public async Task RR_98_D_63_SETTING_READ_of_the_Data_definition_security_sod_conflicts_answers_source_Data_with_the_SodConflict_rows_and_no_value_type()
    {
        CommandReply read = await _host.GetAsync("/v1/kernel/settings/security.sod_conflicts", cancellationToken: Ct);

        Assert.True(read.Status == 200, read.Body);
        Assert.Equal("Data", read.Json!["source"]!.GetValue<string>());
        Assert.Equal("Data", read.Json["backing"]!.GetValue<string>());
        Assert.Null(read.Json["valueType"]);
        Assert.Null(read.Json["defaultValue"]);
        Assert.Null(read.Json["value"]);
        string[] pairs = [.. read.Json["conflictPairs"]!.AsArray().Select(pair => $"{pair!["permissionA"]!.GetValue<string>()}|{pair["permissionB"]!.GetValue<string>()}")];
        Assert.Equal(await _host.ScalarAsync("SELECT count(*) FROM kernel.sod_conflict", Ct), (long)pairs.Length);
        Assert.NotEmpty(pairs);
        foreach (string pair in pairs)
        {
            string[] codes = pair.Split('|');
            Assert.Equal(1L, await _host.ScalarAsync("SELECT count(*) FROM kernel.sod_conflict WHERE permission_a = @a AND permission_b = @b", Ct, ("a", codes[0]), ("b", codes[1])));
        }

        // A Planned definition has no value type either, and a definition that is not Data carries no pairs.
        CommandReply planned = await _host.GetAsync($"/v1/kernel/settings/security.four_eyes_posting?companyId={Company}", cancellationToken: Ct);
        Assert.True(planned.Status == 200, planned.Body);
        Assert.Null(planned.Json!["valueType"]);
        Assert.Equal("Default", planned.Json["source"]!.GetValue<string>());
        Assert.Null(planned.Json["conflictPairs"]);
    }

    [Fact]
    public async Task RR_98_D_63_the_OpenAPI_document_describes_the_conflict_pairs_of_SETTING_READ()
    {
        using HttpResponseMessage response = await _host.Client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative), Ct);
        JsonNode schemas = JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!["components"]!["schemas"]!;

        JsonNode view = schemas["SettingReadView"]!["properties"]!;
        Assert.NotNull(view["conflictPairs"]);
        Assert.NotNull(view["valueType"]);
        JsonNode pair = schemas["PermissionCodePair"]!["properties"]!;
        Assert.NotNull(pair["permissionA"]);
        Assert.NotNull(pair["permissionB"]);
    }
}

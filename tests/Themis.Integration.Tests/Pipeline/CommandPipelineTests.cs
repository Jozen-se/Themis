using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Themis.FixtureModule.Api;
using Themis.FixtureModule.Contracts;
using Themis.Kernel.Contracts.Commands;
using Themis.Kernel.Pipeline.Dispatch;
using Themis.Kernel.Primitives;

namespace Themis.Integration.Tests.Pipeline;

/// <summary>
/// The command pipeline end to end on the fixture module's commands: one transaction with two role switches,
/// idempotency with replay, concurrent duplicate and reuse, optimistic concurrency, audit and outbox rows, the
/// stored rejection, the whole-command retry and the role boundary (brief, milestone 1 checklist).
/// </summary>
[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Integration")]
public sealed class CommandPipelineTests : IAsyncLifetime
{
    private const string Counters = "/v1/fixture/counters";

    private readonly PostgresFixture _db;
    private PipelineTestHost _host = null!;

    public CommandPipelineTests(PostgresFixture db)
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

    [Fact]
    public async Task A_command_writes_its_row_audit_event_outbox_event_and_idempotency_record_in_one_transaction()
    {
        CommandReply reply = await _host.PostAsync(Counters, new { initialValue = 5, sample = "12.50" }, "create-1", correlationId: "corr-create-1", cancellationToken: Ct);

        Assert.Equal(200, reply.Status);
        Assert.Equal("application/json", reply.MediaType);
        Assert.Equal(5, reply.Json!["value"]!.GetValue<long>());
        Assert.Equal(1, reply.Json["version"]!.GetValue<long>());
        Guid id = reply.Json["id"]!.GetValue<Guid>();

        Assert.Equal(1L, await _host.CountAsync("fixture.counter", Ct));
        Assert.Equal(1L, await _host.CountAsync("kernel.audit_event", Ct));
        Assert.Equal(1L, await _host.CountAsync("kernel.outbox_event", Ct));
        Assert.Equal(1L, await _host.CountAsync("kernel.idempotency_record", Ct));

        // R-AUD-01: user, time, command, idempotency key, correlation id and the changed fields.
        Assert.Equal("FIXTURE_COUNTER.CREATE", await _host.ScalarAsync("SELECT command_name FROM kernel.audit_event", Ct));
        Assert.Equal("create-1", await _host.ScalarAsync("SELECT idempotency_key FROM kernel.audit_event", Ct));
        Assert.Equal("corr-create-1", await _host.ScalarAsync("SELECT correlation_id FROM kernel.audit_event", Ct));
        Assert.Equal(TestUsers.Alice, await _host.ScalarAsync("SELECT user_id FROM kernel.audit_event", Ct));
        Assert.Equal(id, await _host.ScalarAsync("SELECT entity_id FROM kernel.audit_event", Ct));
        Assert.Equal("FixtureCounter", await _host.ScalarAsync("SELECT entity_type FROM kernel.audit_event", Ct));
        Assert.Equal("5", await _host.ScalarAsync("SELECT changes->'value'->>'after' FROM kernel.audit_event", Ct));
        Assert.Null(await _host.ScalarAsync("SELECT changes->'value'->>'before' FROM kernel.audit_event", Ct));

        // D-44: TYPE plus the command in past tense, schema version 1, aggregate id of the row.
        Assert.Equal("FIXTURE_COUNTER_CREATED", await _host.ScalarAsync("SELECT event_type FROM kernel.outbox_event", Ct));
        Assert.Equal(1, await _host.ScalarAsync("SELECT schema_version FROM kernel.outbox_event", Ct));
        Assert.Equal(id, await _host.ScalarAsync("SELECT aggregate_id FROM kernel.outbox_event", Ct));
        Assert.Null(await _host.ScalarAsync("SELECT published_at FROM kernel.outbox_event", Ct));
        Assert.Equal("12.50", await _host.ScalarAsync("SELECT payload->>'sample' FROM kernel.outbox_event", Ct));
    }

    [Fact]
    public async Task D_37_decimals_are_strings_and_integers_stay_numbers_in_responses_and_both_forms_are_accepted_on_input()
    {
        CommandReply fromString = await _host.PostAsync(Counters, new { initialValue = 1, sample = "0.1250" }, "d37-string", cancellationToken: Ct);
        CommandReply fromNumber = await _host.PostAsync(Counters, """{"initialValue": 1, "sample": 0.125}""", "d37-number", cancellationToken: Ct);

        Assert.Equal(200, fromString.Status);
        Assert.Equal(200, fromNumber.Status);
        Assert.Equal(JsonValueKind.String, fromString.Json!["sample"]!.GetValueKind());
        Assert.Equal("0.1250", fromString.Json["sample"]!.GetValue<string>());
        Assert.Equal("0.125", fromNumber.Json!["sample"]!.GetValue<string>());
        Assert.Equal(JsonValueKind.Number, fromString.Json["version"]!.GetValueKind());
        Assert.Equal(JsonValueKind.Number, fromString.Json["value"]!.GetValueKind());
    }

    [Fact]
    public async Task R_API_01_replaying_the_same_key_returns_the_first_result_and_posts_nothing_twice()
    {
        CommandReply first = await _host.PostAsync(Counters, new { initialValue = 1, sample = "1" }, "replay-1", cancellationToken: Ct);
        CommandReply second = await _host.PostAsync(Counters, new { initialValue = 1, sample = "1" }, "replay-1", cancellationToken: Ct);

        Assert.Equal(200, first.Status);
        Assert.Equal(200, second.Status);
        Assert.False(first.Replayed);
        Assert.True(second.Replayed);
        Assert.Equal(first.Body, second.Body);
        Assert.Equal(1L, await _host.CountAsync("fixture.counter", Ct));
        Assert.Equal(1L, await _host.CountAsync("kernel.outbox_event", Ct));
        Assert.Equal(1L, await _host.CountAsync("kernel.audit_event", Ct));
    }

    [Fact]
    public async Task R_API_01_idempotency_keys_are_scoped_per_user()
    {
        CommandReply alice = await _host.PostAsync(Counters, new { initialValue = 1, sample = "1" }, "shared-key", user: TestUsers.Alice.ToString(), cancellationToken: Ct);
        CommandReply bob = await _host.PostAsync(Counters, new { initialValue = 1, sample = "1" }, "shared-key", user: TestUsers.Bob.ToString(), cancellationToken: Ct);

        Assert.Equal(200, alice.Status);
        Assert.Equal(200, bob.Status);
        Assert.False(bob.Replayed);
        Assert.Equal(2L, await _host.CountAsync("fixture.counter", Ct));
    }

    [Fact]
    public async Task R_API_04_the_same_key_with_a_different_body_is_rejected_and_the_first_result_kept()
    {
        CommandReply first = await _host.PostAsync(Counters, new { initialValue = 1, sample = "1" }, "reuse-1", cancellationToken: Ct);
        CommandReply reused = await _host.PostAsync(Counters, new { initialValue = 2, sample = "1" }, "reuse-1", cancellationToken: Ct);
        CommandReply replay = await _host.PostAsync(Counters, new { initialValue = 1, sample = "1" }, "reuse-1", cancellationToken: Ct);

        Assert.Equal(200, first.Status);
        Assert.Equal(422, reused.Status);
        Assert.Equal("application/problem+json", reused.MediaType);
        Assert.Equal("idempotency_key_reused", reused.Code);
        Assert.Equal("R-API-04", reused.RuleId);
        Assert.True(replay.Replayed);
        Assert.Equal(1L, await _host.CountAsync("fixture.counter", Ct));
    }

    [Fact]
    public async Task R_API_01_a_concurrent_duplicate_gets_409_at_once_while_the_first_request_holds_the_key()
    {
        // The first request is simulated by holding the key's transaction-scoped advisory lock on another connection,
        // exactly what the pipeline does while a command runs. The duplicate must answer without waiting for it.
        long lockKey = AdvisoryLockKey.For(new UserId(TestUsers.Alice), IdempotencyKey.From("concurrent-1"));
        await using NpgsqlConnection holder = await PostgresFixture.OpenAsync(_host.Database.ConnectionStringFor("themis_app"), Ct);
        await using (NpgsqlTransaction running = await holder.BeginTransactionAsync(Ct))
        {
            await using (NpgsqlCommand take = new("SET LOCAL ROLE themis_kernel; SELECT pg_advisory_xact_lock(@key)", holder, running))
            {
                take.Parameters.AddWithValue("key", lockKey);
                await take.ExecuteNonQueryAsync(Ct);
            }

            using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(Ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(15));
            CommandReply duplicate = await _host.PostAsync(Counters, new { initialValue = 1, sample = "1" }, "concurrent-1", cancellationToken: deadline.Token);

            Assert.Equal(409, duplicate.Status);
            Assert.Equal("idempotency_key_in_progress", duplicate.Code);
            Assert.Equal("R-API-01", duplicate.RuleId);
            Assert.Equal(0L, await _host.CountAsync("kernel.idempotency_record", Ct));
            await running.RollbackAsync(Ct);
        }

        // Once the first request completed, the client's retry goes through.
        CommandReply retry = await _host.PostAsync(Counters, new { initialValue = 1, sample = "1" }, "concurrent-1", cancellationToken: Ct);
        Assert.Equal(200, retry.Status);
        Assert.False(retry.Replayed);
    }

    [Fact]
    public async Task R_API_01_a_command_without_an_idempotency_key_is_refused_before_anything_runs()
    {
        CommandReply reply = await _host.PostAsync(Counters, new { initialValue = 1, sample = "1" }, idempotencyKey: null, cancellationToken: Ct);

        Assert.Equal(400, reply.Status);
        Assert.Equal("idempotency_key_required", reply.Code);
        Assert.Equal("R-API-01", reply.RuleId);
        Assert.Equal(0L, await _host.CountAsync("fixture.counter", Ct));
    }

    [Fact]
    public async Task R_SEC_01_a_user_without_the_permission_is_rejected_with_403_and_the_rule_id()
    {
        CommandReply reply = await _host.PostAsync(Counters, new { initialValue = 1, sample = "1" }, "forbidden-1", permissions: "FIXTURE_COUNTER.TOUCH", cancellationToken: Ct);

        Assert.Equal(403, reply.Status);
        Assert.Equal("application/problem+json", reply.MediaType);
        Assert.Equal("permission_missing", reply.Code);
        Assert.Equal("R-SEC-01", reply.RuleId);
        Assert.Equal(0L, await _host.CountAsync("fixture.counter", Ct));
        Assert.Equal(0L, await _host.CountAsync("kernel.idempotency_record", Ct));
    }

    [Fact]
    public async Task R_SEC_01_an_anonymous_request_is_rejected_with_401()
    {
        using HttpRequestMessage request = new(HttpMethod.Post, Counters) { Content = new StringContent("""{"initialValue": 1, "sample": "1"}""", System.Text.Encoding.UTF8, "application/json") };
        request.Headers.Add("Idempotency-Key", "anonymous-1");

        using HttpResponseMessage response = await _host.Client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task R_API_03_a_validation_error_returns_400_with_the_code_and_stores_nothing()
    {
        CommandReply reply = await _host.PostAsync(Counters, new { initialValue = 1, sample = "-1" }, "validation-1", cancellationToken: Ct);

        Assert.Equal(400, reply.Status);
        Assert.Equal("sample_negative", reply.Code);
        Assert.Equal(0L, await _host.CountAsync("kernel.idempotency_record", Ct));
    }

    [Fact]
    public async Task R_API_02_touch_needs_the_current_version_from_If_Match()
    {
        Guid id = await CreateAsync("touch-create");

        CommandReply touched = await _host.PostAsync($"{Counters}/{id}/touch", new { increment = 3 }, "touch-1", ifMatch: "\"1\"", cancellationToken: Ct);
        CommandReply stale = await _host.PostAsync($"{Counters}/{id}/touch", new { increment = 3 }, "touch-2", ifMatch: "\"1\"", cancellationToken: Ct);
        CommandReply missing = await _host.PostAsync($"{Counters}/{id}/touch", new { increment = 3 }, "touch-3", ifMatch: null, cancellationToken: Ct);
        CommandReply weak = await _host.PostAsync($"{Counters}/{id}/touch", new { increment = 1 }, "touch-4", ifMatch: "W/\"2\"", cancellationToken: Ct);

        Assert.Equal(200, touched.Status);
        Assert.Equal(8, touched.Json!["value"]!.GetValue<long>());
        Assert.Equal(2, touched.Json["version"]!.GetValue<long>());
        Assert.Equal(412, stale.Status);
        Assert.Equal("version_stale", stale.Code);
        Assert.Equal("R-API-02", stale.RuleId);
        Assert.Equal(428, missing.Status);
        Assert.Equal("if_match_required", missing.Code);
        Assert.Equal("R-API-02", missing.RuleId);
        Assert.Equal(200, weak.Status);
        Assert.Equal(3, weak.Json!["version"]!.GetValue<long>());
        Assert.Equal("FIXTURE_COUNTER_TOUCHED", await _host.ScalarAsync("SELECT event_type FROM kernel.outbox_event WHERE aggregate_id = @id ORDER BY occurred_at, id OFFSET 1 LIMIT 1", Ct, ("id", id)));
        Assert.Equal("5", await _host.ScalarAsync("SELECT changes->'value'->>'before' FROM kernel.audit_event WHERE command_name = 'FIXTURE_COUNTER.TOUCH' ORDER BY id LIMIT 1", Ct));
    }

    [Fact]
    public async Task R_API_02_a_stale_version_is_not_stored_so_the_retry_with_the_fresh_version_runs()
    {
        Guid id = await CreateAsync("stale-create");

        CommandReply stale = await _host.PostAsync($"{Counters}/{id}/touch", new { increment = 2 }, "stale-1", ifMatch: "\"7\"", cancellationToken: Ct);
        Assert.Equal(412, stale.Status);
        Assert.Equal(0L, await _host.ScalarAsync("SELECT count(*) FROM kernel.idempotency_record WHERE key = 'stale-1'", Ct));

        CommandReply fresh = await _host.PostAsync($"{Counters}/{id}/touch", new { increment = 2 }, "stale-1", ifMatch: "\"1\"", cancellationToken: Ct);
        Assert.Equal(200, fresh.Status);
        Assert.False(fresh.Replayed);
        Assert.Equal(2, fresh.Json!["version"]!.GetValue<long>());
        Assert.Equal(1L, await _host.ScalarAsync("SELECT count(*) FROM kernel.idempotency_record WHERE key = 'stale-1'", Ct));
    }

    [Fact]
    public async Task R_API_02_a_missing_If_Match_is_not_stored_either()
    {
        Guid id = await CreateAsync("missing-if-match-create");

        CommandReply missing = await _host.PostAsync($"{Counters}/{id}/touch", new { increment = 2 }, "missing-if-match-1", ifMatch: null, cancellationToken: Ct);
        CommandReply retry = await _host.PostAsync($"{Counters}/{id}/touch", new { increment = 2 }, "missing-if-match-1", ifMatch: "\"1\"", cancellationToken: Ct);

        Assert.Equal(428, missing.Status);
        Assert.Equal(200, retry.Status);
        Assert.False(retry.Replayed);
    }

    [Fact]
    public async Task R_API_04_the_fingerprint_includes_If_Match_so_a_stored_result_is_not_replayed_for_another_version()
    {
        Guid id = await CreateAsync("fingerprint-create");

        CommandReply first = await _host.PostAsync($"{Counters}/{id}/touch", new { increment = 2 }, "fingerprint-1", ifMatch: "\"1\"", cancellationToken: Ct);
        CommandReply replay = await _host.PostAsync($"{Counters}/{id}/touch", new { increment = 2 }, "fingerprint-1", ifMatch: "\"1\"", cancellationToken: Ct);
        CommandReply otherVersion = await _host.PostAsync($"{Counters}/{id}/touch", new { increment = 2 }, "fingerprint-1", ifMatch: "\"2\"", cancellationToken: Ct);

        Assert.Equal(200, first.Status);
        Assert.True(replay.Replayed);
        Assert.Equal(422, otherVersion.Status);
        Assert.Equal("idempotency_key_reused", otherVersion.Code);
        Assert.Equal("R-API-04", otherVersion.RuleId);
        Assert.Equal(2L, await _host.ScalarAsync("SELECT version FROM fixture.counter WHERE id = @id", Ct, ("id", id)));
    }

    [Fact]
    public async Task R_API_02_every_success_returns_the_new_version_as_ETag_and_a_replay_returns_the_stored_one()
    {
        CommandReply created = await _host.PostAsync(Counters, new { initialValue = 1, sample = "1" }, "etag-create", cancellationToken: Ct);
        Guid id = created.Json!["id"]!.GetValue<Guid>();
        CommandReply touched = await _host.PostAsync($"{Counters}/{id}/touch", new { increment = 1 }, "etag-touch", ifMatch: created.ETag, cancellationToken: Ct);
        CommandReply replayed = await _host.PostAsync($"{Counters}/{id}/touch", new { increment = 1 }, "etag-touch", ifMatch: created.ETag, cancellationToken: Ct);
        CommandReply rejected = await _host.PostAsync($"{Counters}/{id}/touch", new { increment = 0 }, "etag-reject", ifMatch: touched.ETag, cancellationToken: Ct);

        Assert.Equal("\"1\"", created.ETag);
        Assert.Equal("\"2\"", touched.ETag);
        Assert.True(replayed.Replayed);
        Assert.Equal("\"2\"", replayed.ETag);
        Assert.Equal(422, rejected.Status);
        Assert.Null(rejected.ETag);
    }

    [Fact]
    public async Task D_25_the_pipeline_creates_a_correlation_id_when_a_caller_outside_HTTP_supplies_none()
    {
        await using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
        DefaultHttpContext http = new() { RequestServices = scope.ServiceProvider };
        http.Request.Headers[HeaderPrincipal.UserHeader] = TestUsers.Alice.ToString();
        http.Request.Headers[HeaderPermissionEvaluator.PermissionsHeader] = PipelineTestHost.DefaultPermissions;
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = http;
        ICommandDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();

        CommandResponse response = await dispatcher.DispatchAsync<CreateCounter, CounterSnapshot>(new CreateCounter(1, 1m), new CommandEnvelope(IdempotencyKey.From("no-correlation-1")), Ct);

        Assert.Equal(200, response.StatusCode);
        Assert.Equal(1L, response.Version);
        string? stored = (string?)await _host.ScalarAsync("SELECT correlation_id FROM kernel.audit_event WHERE idempotency_key = 'no-correlation-1'", Ct);
        Assert.False(string.IsNullOrWhiteSpace(stored));
        Assert.True(CorrelationId.TryFrom(stored, out _));
    }

    [Fact]
    public async Task R_API_02_the_OpenAPI_document_keeps_expectedVersion_out_and_describes_the_headers_and_decimals()
    {
        using HttpResponseMessage response = await _host.Client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative), Ct);
        string document = await response.Content.ReadAsStringAsync(Ct);
        JsonNode root = JsonNode.Parse(document)!;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("expectedVersion", document, StringComparison.Ordinal);

        JsonNode touch = root["paths"]!["/v1/fixture/counters/{id}/touch"]!["post"]!;
        JsonArray parameters = touch["parameters"]!.AsArray();
        Assert.Contains(parameters, p => p!["name"]!.GetValue<string>() == "Idempotency-Key" && p["in"]!.GetValue<string>() == "header" && p["required"]!.GetValue<bool>());
        Assert.Contains(parameters, p => p!["name"]!.GetValue<string>() == "If-Match" && p["in"]!.GetValue<string>() == "header" && p["required"]!.GetValue<bool>());
        Assert.NotNull(touch["responses"]!["200"]!["headers"]!["ETag"]);

        JsonNode create = root["paths"]!["/v1/fixture/counters"]!["post"]!;
        Assert.DoesNotContain(create["parameters"]!.AsArray(), p => p!["name"]!.GetValue<string>() == "If-Match");

        // D-37: decimals are strings with format decimal; integers stay integers.
        JsonNode snapshot = root["components"]!["schemas"]!["CounterSnapshot"]!["properties"]!;
        Assert.Equal("string", snapshot["sample"]!["type"]!.GetValue<string>());
        Assert.Equal("decimal", snapshot["sample"]!["format"]!.GetValue<string>());
        Assert.Equal("integer", snapshot["version"]!["type"]!.GetValue<string>());
    }

    [Fact]
    public async Task R_API_02_the_body_cannot_override_the_version_or_the_route_id()
    {
        Guid id = await CreateAsync("override-create");

        CommandReply reply = await _host.PostAsync($"{Counters}/{id}/touch", new { increment = 1, expectedVersion = 99, id = Guid.NewGuid() }, "override-1", ifMatch: "\"1\"", cancellationToken: Ct);

        Assert.Equal(200, reply.Status);
        Assert.Equal(id, reply.Json!["id"]!.GetValue<Guid>());
    }

    [Fact]
    public async Task A_touch_of_a_missing_counter_is_404()
    {
        CommandReply reply = await _host.PostAsync($"{Counters}/{Guid.NewGuid()}/touch", new { increment = 1 }, "missing-1", ifMatch: "\"1\"", cancellationToken: Ct);

        Assert.Equal(404, reply.Status);
        Assert.Equal("counter_not_found", reply.Code);
    }

    [Fact]
    public async Task R_API_03_a_rejection_after_the_write_rolls_the_write_back_and_is_stored_as_the_key_result()
    {
        Guid id = await CreateAsync("reject-create");

        CommandReply rejected = await _host.PostAsync($"{Counters}/{id}/touch", new { increment = 1, failure = "RejectAfterWrite" }, "reject-1", ifMatch: "\"1\"", cancellationToken: Ct);
        CommandReply replayed = await _host.PostAsync($"{Counters}/{id}/touch", new { increment = 1, failure = "RejectAfterWrite" }, "reject-1", ifMatch: "\"1\"", cancellationToken: Ct);

        Assert.Equal(422, rejected.Status);
        Assert.Equal("rejected_after_write", rejected.Code);
        Assert.Equal("R-API-03", rejected.RuleId);
        Assert.Equal(422, replayed.Status);
        Assert.True(replayed.Replayed);
        Assert.Equal(1L, await _host.ScalarAsync("SELECT version FROM fixture.counter WHERE id = @id", Ct, ("id", id)));
        Assert.Equal(1L, await _host.ScalarAsync("SELECT count(*) FROM kernel.idempotency_record WHERE key = 'reject-1'", Ct));
        Assert.Equal(0L, await _host.ScalarAsync("SELECT count(*) FROM kernel.audit_event WHERE command_name = 'FIXTURE_COUNTER.TOUCH'", Ct));
        Assert.Equal(0L, await _host.ScalarAsync("SELECT count(*) FROM kernel.outbox_event WHERE event_type = 'FIXTURE_COUNTER_TOUCHED'", Ct));
    }

    [Fact]
    public async Task An_exception_after_the_write_leaves_nothing_behind_not_even_the_idempotency_record()
    {
        Guid id = await CreateAsync("throw-create");

        CommandReply reply = await _host.PostAsync($"{Counters}/{id}/touch", new { increment = 1, failure = "ThrowAfterWrite" }, "throw-1", ifMatch: "\"1\"", cancellationToken: Ct);

        Assert.Equal(500, reply.Status);
        Assert.Equal("application/problem+json", reply.MediaType);
        Assert.Equal(1L, await _host.ScalarAsync("SELECT version FROM fixture.counter WHERE id = @id", Ct, ("id", id)));
        Assert.Equal(0L, await _host.ScalarAsync("SELECT count(*) FROM kernel.idempotency_record WHERE key = 'throw-1'", Ct));

        // Nothing was stored for the key, so the client's retry with the same key runs the command afresh.
        CommandReply again = await _host.PostAsync($"{Counters}/{id}/touch", new { increment = 1 }, "throw-1", ifMatch: "\"1\"", cancellationToken: Ct);
        Assert.Equal(200, again.Status);
        Assert.False(again.Replayed);
        Assert.Equal(2L, await _host.ScalarAsync("SELECT version FROM fixture.counter WHERE id = @id", Ct, ("id", id)));
    }

    [Fact]
    public async Task D_41_a_serialization_failure_on_the_first_attempt_is_retried_once_and_the_change_lands_exactly_once()
    {
        Guid id = await CreateAsync("retry-create");
        long retriesBefore = _host.Retries;

        CommandReply reply = await _host.PostAsync($"{Counters}/{id}/touch", new { increment = 7, failure = "SerializationFailureOnFirstAttempt" }, "retry-1", ifMatch: "\"1\"", cancellationToken: Ct);

        Assert.Equal(200, reply.Status);
        Assert.Equal(2, reply.Json!["version"]!.GetValue<long>());
        Assert.Equal(12L, await _host.ScalarAsync("SELECT value FROM fixture.counter WHERE id = @id", Ct, ("id", id)));
        Assert.Equal(1L, await _host.ScalarAsync("SELECT count(*) FROM kernel.outbox_event WHERE event_type = 'FIXTURE_COUNTER_TOUCHED'", Ct));
        Assert.Equal(1L, await _host.ScalarAsync("SELECT count(*) FROM kernel.audit_event WHERE command_name = 'FIXTURE_COUNTER.TOUCH'", Ct));
        Assert.Equal(1L, _host.Retries - retriesBefore);
        Assert.Contains(_host.Logs.Lines, line => line.Level == LogLevel.Warning && line.Message.Contains("40001", StringComparison.Ordinal));
    }

    [Fact]
    public async Task I_14_a_fixture_role_statement_cannot_touch_a_kernel_table_and_the_command_fails_whole()
    {
        Guid id = await CreateAsync("boundary-create");

        CommandReply reply = await _host.PostAsync($"{Counters}/{id}/touch", new { increment = 1, failure = "WriteKernelTable" }, "boundary-1", ifMatch: "\"1\"", cancellationToken: Ct);

        Assert.Equal(500, reply.Status);
        Assert.Equal(1L, await _host.ScalarAsync("SELECT version FROM fixture.counter WHERE id = @id", Ct, ("id", id)));
        Assert.Equal(0L, await _host.ScalarAsync("SELECT count(*) FROM kernel.audit_event WHERE command_name = 'FIXTURE'", Ct));

        // The same command through the dispatcher shows the refusal itself: SQLSTATE 42501 from PostgreSQL.
        await using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
        DefaultHttpContext http = new() { RequestServices = scope.ServiceProvider };
        http.Request.Headers[HeaderPrincipal.UserHeader] = TestUsers.Alice.ToString();
        http.Request.Headers[HeaderPermissionEvaluator.PermissionsHeader] = PipelineTestHost.DefaultPermissions;
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = http;
        ICommandDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();

        PostgresException refused = await Assert.ThrowsAsync<PostgresException>(() => dispatcher.DispatchAsync<TouchCounter, CounterSnapshot>(
            new TouchCounter(id, 1, 1, FixtureFailure.WriteKernelTable), new CommandEnvelope(IdempotencyKey.From("boundary-2"), CorrelationId.From("boundary")), Ct));
        Assert.Equal("42501", refused.SqlState);
    }

    [Fact]
    public async Task D_25_every_log_line_of_a_command_carries_the_command_name_correlation_id_and_user()
    {
        Guid id = await CreateAsync("log-create");
        await _host.PostAsync($"{Counters}/{id}/touch", new { increment = 1, failure = "RejectAfterWrite" }, "log-1", ifMatch: "\"1\"", correlationId: "corr-log-1", cancellationToken: Ct);

        LogLine rejected = Assert.Single(_host.Logs.Lines, line => line.Message.Contains("rejected with rejected_after_write", StringComparison.Ordinal));
        Assert.Equal("FIXTURE_COUNTER.TOUCH", rejected.Scopes["CommandName"]);
        Assert.Equal("corr-log-1", rejected.Scopes["CorrelationId"]);
        Assert.Equal(TestUsers.Alice.ToString(), rejected.Scopes["UserId"]);
    }

    [Fact]
    public async Task The_response_echoes_the_correlation_id_and_a_rejection_carries_it_in_the_problem()
    {
        CommandReply reply = await _host.PostAsync(Counters, new { initialValue = 1, sample = "-1" }, "corr-1", correlationId: "corr-problem-1", cancellationToken: Ct);

        Assert.Equal("corr-problem-1", reply.CorrelationId);
        Assert.Equal("corr-problem-1", reply.Json!["correlationId"]!.GetValue<string>());
        Assert.Equal(400, reply.Json["status"]!.GetValue<int>());
        Assert.Single(reply.Json["errors"]!.AsArray());
    }

    private async Task<Guid> CreateAsync(string key)
    {
        CommandReply reply = await _host.PostAsync(Counters, new { initialValue = 5, sample = "1.5" }, key, cancellationToken: Ct);
        Assert.Equal(200, reply.Status);
        return reply.Json!["id"]!.GetValue<Guid>();
    }
}

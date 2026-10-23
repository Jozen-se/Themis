using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Themis.FixtureModule;
using Themis.FixtureModule.Application;
using Themis.FixtureModule.Contracts;
using Themis.Kernel.Contracts.Commands;
using Themis.Kernel.Contracts.Documents;
using Themis.Kernel.Contracts.MasterData;
using Themis.Kernel.Contracts.Organisation;
using Themis.Kernel.Contracts.Queries;
using Themis.Kernel.Contracts.Security;
using Themis.Kernel.Contracts.Settings;
using Themis.Kernel.Documents;
using Themis.Kernel.Identity;
using Themis.Kernel.MasterData;
using Themis.Kernel.Messaging;
using Themis.Kernel.Organisation;
using Themis.Kernel.Persistence;
using Themis.Kernel.Pipeline;
using Themis.Kernel.Pipeline.Http;
using Themis.Kernel.Pipeline.Telemetry;
using Themis.Kernel.Posting;
using Themis.Kernel.Settings;

namespace Themis.Integration.Tests.Pipeline;

/// <summary>
/// An in-memory host wired like Themis.Host, with the fixture module and the real identity capability behind the
/// test token issuer of the step 4 brief: persistence as themis_app, the kernel pipeline, bearer authentication
/// with a local signing key, problem details for unhandled exceptions, the correlation id middleware and the
/// fixture module's routes. Requests are signed for the clerk unless a test names another subject. Captures log
/// lines with their scopes (D-25) and the pipeline's retry metric (D-41), per SQLSTATE as well (R-LED-08). Runs
/// against the fixture's database, or against another database on the same server (a clone of a template, step 11).
/// </summary>
internal sealed class PipelineTestHost : IAsyncDisposable
{

    private readonly WebApplication _app;
    private readonly MeterListener _meterListener;

    private PipelineTestHost(WebApplication app, HttpClient client, CapturingLoggerProvider logs, MeterListener meterListener, PostgresFixture db, string superuserConnectionString)
    {
        _app = app;
        Client = client;
        Logs = logs;
        _meterListener = meterListener;
        Database = db;
        SuperuserConnectionString = superuserConnectionString;
    }

    public HttpClient Client { get; }

    public CapturingLoggerProvider Logs { get; }

    public PostgresFixture Database { get; }

    public IServiceProvider Services => _app.Services;

    public long Retries { get; private set; }

    /// <summary>The retries of D-41 per SQLSTATE (40001 serialization failure, 40P01 deadlock); R-LED-08 expects no deadlock at all.</summary>
    public ConcurrentDictionary<string, long> RetriesBySqlState { get; } = new(StringComparer.Ordinal);

    /// <summary>Superuser connection string of the database this host runs against (the fixture's unless a clone was named).</summary>
    public string SuperuserConnectionString { get; }

    /// <summary>Outbox events the relay published through this host (counter themis.outbox.published).</summary>
    public long Published { get; private set; }

    /// <summary>The last recorded age of the oldest unpublished event in seconds; null until a pass ran.</summary>
    public long? OldestPendingAge { get; private set; }

    public TestOutboxRelaySeam RelaySeam => Services.GetRequiredService<TestOutboxRelaySeam>();

    public TestItemCreateSeam ItemCreateSeam => Services.GetRequiredService<TestItemCreateSeam>();

    public PostingFailureSwitch PostingFailures => Services.GetRequiredService<PostingFailureSwitch>();

    public FixtureBarrier Barrier => Services.GetRequiredService<FixtureBarrier>();

    public OutboxRelayPass Relay => Services.GetRequiredService<OutboxRelayPass>();

    /// <summary>Expired idempotency records removed by the purge, from the counter themis.idempotency.purged (milestone 2 step 2).</summary>
    public long Purged { get; private set; }

    public IdempotencyPurgePass Purge => Services.GetRequiredService<IdempotencyPurgePass>();

    public TestSettingDefinitionSeam SettingSeam => Services.GetRequiredService<TestSettingDefinitionSeam>();

    public TestSettingWriteSeam SettingWriteSeam => Services.GetRequiredService<TestSettingWriteSeam>();

    /// <summary>Starts the host against the fixture's database, or against <paramref name="database"/> on the same server when named.</summary>
    public static async Task<PipelineTestHost> StartAsync(PostgresFixture db, string? database = null)
    {
        string applicationConnectionString = WithDatabase(db.ConnectionStringFor("themis_app"), database);
        string superuserConnectionString = WithDatabase(db.SuperuserConnectionString, database);
        CapturingLoggerProvider logs = new();
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(logs);
        builder.Logging.SetMinimumLevel(LogLevel.Information);

        builder.Configuration["Identity:Audience"] = TestTokens.Audience;
        builder.Configuration["Identity:TestIssuer:Issuer"] = TestTokens.Issuer;
        builder.Configuration["Identity:TestIssuer:SigningKey"] = TestTokens.SigningKeyBase64;
        // Step 9: the tests call the relay's pass directly and never wait on the timer; the same for the purge (milestone 2 step 2).
        builder.Configuration["Outbox:Relay:Enabled"] = "false";
        builder.Configuration["Idempotency:Purge:Enabled"] = "false";

        builder.Services.AddKernelPersistence(applicationConnectionString);
        builder.Services.AddKernelPipeline();
        builder.Services.AddKernelIdentity(builder.Configuration, builder.Environment.EnvironmentName);
        builder.Services.AddFixtureModule();
        // The test seams, registered before the kernel's registrations, which keep them (Build Brief, Test seams).
        builder.Services.AddSingleton<PostingFailureSwitch>();
        builder.Services.AddSingleton<IPostingHook, TestPostingHook>();
        builder.Services.AddSingleton<TestOutboxRelaySeam>();
        builder.Services.AddSingleton<IOutboxRelaySeam>(provider => provider.GetRequiredService<TestOutboxRelaySeam>());
        builder.Services.AddSingleton<TestItemCreateSeam>();
        builder.Services.AddSingleton<IItemCreateSeam>(provider => provider.GetRequiredService<TestItemCreateSeam>());
        builder.Services.AddSingleton<TestSettingDefinitionSeam>();
        builder.Services.AddSingleton<ISettingDefinitionSeam>(provider => provider.GetRequiredService<TestSettingDefinitionSeam>());
        builder.Services.AddSingleton<TestSettingWriteSeam>();
        builder.Services.AddSingleton<ISettingWriteSeam>(provider => provider.GetRequiredService<TestSettingWriteSeam>());
        // RR-62: the dispatcher's role probe (docs/development/test-seams.md).
        builder.Services.AddScoped<RoleProbeRecorder>();
        builder.Services.AddScoped<IScopeResolver<RoleProbe>, RoleProbeScopeResolver>();
        builder.Services.AddScoped<ICommandHandler<RoleProbe, RoleProbeResult>, RoleProbeHandler>();
        // RR-81: the write-attempting query (docs/development/test-seams.md).
        builder.Services.AddScoped<IQueryHandler<WriteAttemptQuery, long>, WriteAttemptQueryHandler>();
        builder.Services.AddKernelOrganisation();
        builder.Services.AddKernelSettings();
        builder.Services.AddKernelMasterData();
        builder.Services.AddKernelPosting();
        builder.Services.AddKernelDocuments();
        builder.Services.AddKernelMessaging(builder.Configuration);
        builder.Services.AddKernelOpenApi();
        builder.Services.AddKernelProblemDetails();

        WebApplication app = builder.Build();
        // RR-72: the correlation id surrounds the exception handler, so a 500 and its log line carry it.
        app.UseKernelRequestPipeline();
        app.MapCommandRoutes(FixtureContracts.Routes);
        app.MapCommandRoutes(RoleProbeRoutes.Commands);
        app.MapQuery<WriteAttemptQuery, long>(WriteAttemptQueryRoutes.Query);
        app.MapQuery<GetCompany, CompanyInfo>(OrganisationRoutes.Company);
        app.MapCommandRoutes(MasterDataRoutes.Commands);
        app.MapQuery<GetItem, ItemView>(MasterDataRoutes.Item);
        app.MapQuery<GetItemByNumber, ItemView>(MasterDataRoutes.ItemByNumber);
        app.MapCommandRoutes(DocumentRoutes.Commands);
        app.MapQuery<GetInventoryAdjustment, InventoryAdjustmentView>(DocumentRoutes.InventoryAdjustment);
        app.MapCommandRoutes(SettingRoutes.Commands);
        app.MapQuery<GetSetting, SettingReadView>(SettingRoutes.Setting);
        app.MapOpenApi().AllowAnonymous();
        await app.StartAsync();

        PipelineTestHost host = new(app, app.GetTestClient(), logs, new MeterListener(), db, superuserConnectionString);
        host._meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if ((instrument.Meter.Name == PipelineTelemetry.SourceName && instrument.Name == "themis.command.retries") || instrument.Meter.Name == OutboxRelayTelemetry.MeterName || instrument.Meter.Name == IdempotencyPurgeTelemetry.MeterName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        host._meterListener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            switch (instrument.Name)
            {
                case "themis.command.retries":
                    host.Retries += value;
                    foreach (KeyValuePair<string, object?> tag in tags)
                    {
                        if (tag.Key == "sqlstate" && tag.Value is string sqlState)
                        {
                            host.RetriesBySqlState.AddOrUpdate(sqlState, value, (_, current) => current + value);
                        }
                    }

                    break;
                case OutboxRelayTelemetry.PublishedName:
                    host.Published += value;
                    break;
                case OutboxRelayTelemetry.OldestPendingAgeName:
                    host.OldestPendingAge = value;
                    break;
                case IdempotencyPurgeTelemetry.PurgedName:
                    host.Purged += value;
                    break;
                default:
                    break;
            }
        });
        host._meterListener.Start();
        return host;
    }

    /// <summary>A bearer token for the subject, minted by the test issuer; the clerk by default.</summary>
    public static string TokenFor(string? subject = null) => TestTokens.For(subject ?? TestUsers.ClerkSubject, TimeProvider.System);

    public Task<CommandReply> GetAsync(string path, string? subject = null, string? token = null, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Get, path, null, null, null, token ?? TokenFor(subject ?? DefaultSubject(path)), null, cancellationToken);

    public Task<CommandReply> PostAsync(string path, object? body, string? idempotencyKey, string? ifMatch = null, string? subject = null, string? token = null, string? correlationId = null, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, path, body, idempotencyKey, ifMatch, token ?? TokenFor(subject ?? DefaultSubject(path)), correlationId, cancellationToken);

    /// <summary>
    /// The settings routes default to the settings administrator, the fixture user who holds SETTINGS_ADMINISTRATOR
    /// since the clerk no longer does (RR-101); every other route defaults to the clerk. A test that names a subject gets it.
    /// </summary>
    private static string DefaultSubject(string path) =>
        path.StartsWith("/v1/kernel/settings", StringComparison.Ordinal) ? TestUsers.SettingsAdministratorSubject : TestUsers.ClerkSubject;

    /// <summary>A request with no token at all.</summary>
    public Task<CommandReply> PostAnonymousAsync(string path, object? body, string? idempotencyKey, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, path, body, idempotencyKey, null, null, null, cancellationToken);

    private async Task<CommandReply> SendAsync(HttpMethod method, string path, object? body, string? idempotencyKey, string? ifMatch, string? token, string? correlationId, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(method, path);
        if (body is not null)
        {
            string json = body as string ?? JsonSerializer.Serialize(body);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        AddHeader(request, "Idempotency-Key", idempotencyKey);
        AddHeader(request, "If-Match", ifMatch);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        AddHeader(request, "X-Correlation-Id", correlationId);

        using HttpResponseMessage response = await Client.SendAsync(request, cancellationToken);
        string content = await response.Content.ReadAsStringAsync(cancellationToken);
        return new CommandReply(
            (int)response.StatusCode,
            response.Content.Headers.ContentType?.MediaType,
            content,
            content.Length > 0 ? JsonNode.Parse(content) : null,
            response.Headers.TryGetValues("Idempotency-Replayed", out IEnumerable<string>? replayed) && replayed.Contains("true"),
            response.Headers.TryGetValues("X-Correlation-Id", out IEnumerable<string>? correlation) ? correlation.First() : null,
            response.Headers.ETag?.Tag);
    }

    /// <summary>A superuser view of what the command left behind.</summary>
    public async Task<object?> ScalarAsync(string sql, CancellationToken cancellationToken, params (string Name, object? Value)[] parameters)
    {
        await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(SuperuserConnectionString, cancellationToken);
        await using NpgsqlCommand command = new(sql, connection);
        foreach ((string name, object? value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        object? result = await command.ExecuteScalarAsync(cancellationToken);
        return result is DBNull ? null : result;
    }

    public async Task<long> CountAsync(string table, CancellationToken cancellationToken) =>
        (long)(await ScalarAsync($"SELECT count(*) FROM {table}", cancellationToken))!;

    public async ValueTask DisposeAsync()
    {
        _meterListener.Dispose();
        Client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private static string WithDatabase(string connectionString, string? database) =>
        database is null ? connectionString : new NpgsqlConnectionStringBuilder(connectionString) { Database = database }.ConnectionString;

    private static void AddHeader(HttpRequestMessage request, string name, string? value)
    {
        if (value is not null)
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }
    }
}

internal sealed record CommandReply(int Status, string? MediaType, string Body, JsonNode? Json, bool Replayed, string? CorrelationId, string? ETag)
{
    public string? Code => Json?["code"]?.GetValue<string>();

    public string? RuleId => Json?["ruleId"]?.GetValue<string>();
}

/// <summary>Captures every log line with the key-value pairs of its enclosing scopes, to prove D-25.</summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private readonly List<LogLine> _lines = [];
    private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();

    public IReadOnlyList<LogLine> Lines
    {
        get
        {
            lock (_lines)
            {
                return [.. _lines];
            }
        }
    }

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

    public void Dispose()
    {
    }

    private void Add(string category, LogLevel level, string message)
    {
        Dictionary<string, object?> scopeValues = new(StringComparer.Ordinal);
        _scopes.ForEachScope((scope, state) =>
        {
            if (scope is IEnumerable<KeyValuePair<string, object>> pairs)
            {
                foreach ((string key, object value) in pairs)
                {
                    state[key] = value;
                }
            }
        }, scopeValues);

        lock (_lines)
        {
            _lines.Add(new LogLine(category, level, message, scopeValues));
        }
    }

    private sealed class CapturingLogger : ILogger
    {
        private readonly CapturingLoggerProvider _provider;
        private readonly string _category;

        public CapturingLogger(CapturingLoggerProvider provider, string category)
        {
            _provider = provider;
            _category = category;
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => _provider._scopes.Push(state);

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            _provider.Add(_category, logLevel, exception is null ? formatter(state, exception) : formatter(state, exception) + " " + exception);
    }
}

internal sealed record LogLine(string Category, LogLevel Level, string Message, IReadOnlyDictionary<string, object?> Scopes);

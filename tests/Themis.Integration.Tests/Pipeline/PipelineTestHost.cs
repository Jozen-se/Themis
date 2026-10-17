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
using Themis.FixtureModule.Contracts;
using Themis.Kernel.Contracts.Documents;
using Themis.Kernel.Contracts.MasterData;
using Themis.Kernel.Contracts.Organisation;
using Themis.Kernel.Documents;
using Themis.Kernel.Identity;
using Themis.Kernel.MasterData;
using Themis.Kernel.Organisation;
using Themis.Kernel.Persistence;
using Themis.Kernel.Pipeline;
using Themis.Kernel.Pipeline.Http;
using Themis.Kernel.Pipeline.Telemetry;
using Themis.Kernel.Posting;

namespace Themis.Integration.Tests.Pipeline;

/// <summary>
/// An in-memory host wired like Themis.Host, with the fixture module and the real identity capability behind the
/// test token issuer of the step 4 brief: persistence as themis_app, the kernel pipeline, bearer authentication
/// with a local signing key, problem details for unhandled exceptions, the correlation id middleware and the
/// fixture module's routes. Requests are signed for the clerk unless a test names another subject. Captures log
/// lines with their scopes (D-25) and the pipeline's retry metric (D-41).
/// </summary>
internal sealed class PipelineTestHost : IAsyncDisposable
{

    private readonly WebApplication _app;
    private readonly MeterListener _meterListener;

    private PipelineTestHost(WebApplication app, HttpClient client, CapturingLoggerProvider logs, MeterListener meterListener, PostgresFixture db)
    {
        _app = app;
        Client = client;
        Logs = logs;
        _meterListener = meterListener;
        Database = db;
    }

    public HttpClient Client { get; }

    public CapturingLoggerProvider Logs { get; }

    public PostgresFixture Database { get; }

    public IServiceProvider Services => _app.Services;

    public long Retries { get; private set; }

    public static async Task<PipelineTestHost> StartAsync(PostgresFixture db)
    {
        CapturingLoggerProvider logs = new();
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(logs);
        builder.Logging.SetMinimumLevel(LogLevel.Information);

        builder.Configuration["Identity:Audience"] = TestTokens.Audience;
        builder.Configuration["Identity:TestIssuer:Issuer"] = TestTokens.Issuer;
        builder.Configuration["Identity:TestIssuer:SigningKey"] = TestTokens.SigningKeyBase64;

        builder.Services.AddKernelPersistence(db.ConnectionStringFor("themis_app"));
        builder.Services.AddKernelPipeline();
        builder.Services.AddKernelIdentity(builder.Configuration, builder.Environment.EnvironmentName);
        builder.Services.AddFixtureModule();
        builder.Services.AddKernelOrganisation();
        builder.Services.AddKernelMasterData();
        builder.Services.AddKernelPosting();
        builder.Services.AddKernelDocuments();
        builder.Services.AddKernelOpenApi();
        builder.Services.AddProblemDetails();

        WebApplication app = builder.Build();
        app.UseExceptionHandler();
        app.UseCorrelationId();
        app.UseAuthentication();
        app.MapCommandRoutes(FixtureContracts.Routes);
        app.MapQuery<GetCompany, CompanyInfo>(OrganisationRoutes.Company);
        app.MapCommandRoutes(MasterDataRoutes.Commands);
        app.MapQuery<GetItem, ItemView>(MasterDataRoutes.Item);
        app.MapQuery<GetItemByNumber, ItemView>(MasterDataRoutes.ItemByNumber);
        app.MapCommandRoutes(DocumentRoutes.Commands);
        app.MapQuery<GetInventoryAdjustment, InventoryAdjustmentView>(DocumentRoutes.InventoryAdjustment);
        app.MapOpenApi();
        await app.StartAsync();

        PipelineTestHost host = new(app, app.GetTestClient(), logs, new MeterListener(), db);
        host._meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == PipelineTelemetry.SourceName && instrument.Name == "themis.command.retries")
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        host._meterListener.SetMeasurementEventCallback<long>((_, value, _, _) => host.Retries += value);
        host._meterListener.Start();
        return host;
    }

    /// <summary>A bearer token for the subject, minted by the test issuer; the clerk by default.</summary>
    public static string TokenFor(string? subject = null) => TestTokens.For(subject ?? TestUsers.ClerkSubject, TimeProvider.System);

    public Task<CommandReply> GetAsync(string path, string? subject = null, string? token = null, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Get, path, null, null, null, token ?? TokenFor(subject), null, cancellationToken);

    public Task<CommandReply> PostAsync(string path, object? body, string? idempotencyKey, string? ifMatch = null, string? subject = null, string? token = null, string? correlationId = null, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, path, body, idempotencyKey, ifMatch, token ?? TokenFor(subject), correlationId, cancellationToken);

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
        await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(Database.SuperuserConnectionString, cancellationToken);
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

using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Themis.Host;

namespace Themis.Kernel.Tests;

[Collection(HostTestGroup.Name)]
[Trait("Category", "Unit")]
public sealed class HostSmokeTests : IDisposable
{
    // The host runs as in production configuration with no connection string, so the test needs no database and
    // the readiness check reports the missing configuration.
    private readonly WebApplicationFactory<Program> _factory = new WebApplicationFactory<Program>()
        .WithWebHostBuilder(builder => builder.UseEnvironment("Testing"));

    [Fact]
    public async Task Liveness_endpoint_answers_healthy()
    {
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/health/live", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Readiness_endpoint_reports_unhealthy_without_a_database()
    {
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Response_carries_a_correlation_id_when_none_is_sent()
    {
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/health/live", UriKind.Relative), TestContext.Current.CancellationToken);

        string correlationId = Assert.Single(response.Headers.GetValues("X-Correlation-Id"));
        Assert.False(string.IsNullOrWhiteSpace(correlationId));
    }

    [Fact]
    public async Task Response_echoes_the_correlation_id_an_integration_sends()
    {
        using HttpClient client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-Id", "erp-sync-2026-10-09-0001");

        using HttpResponseMessage response = await client.GetAsync(new Uri("/health/live", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal("erp-sync-2026-10-09-0001", Assert.Single(response.Headers.GetValues("X-Correlation-Id")));
    }

    [Fact]
    public async Task An_unsafe_correlation_id_is_replaced()
    {
        using HttpClient client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-Id", "bad id with spaces");

        using HttpResponseMessage response = await client.GetAsync(new Uri("/health/live", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.NotEqual("bad id with spaces", Assert.Single(response.Headers.GetValues("X-Correlation-Id")));
    }

    [Fact]
    public async Task D_25_a_request_trace_is_visible_in_the_console_exporter_when_it_is_enabled()
    {
        // Milestone 1 acceptance: traces are visible in the console exporter. The host runs with Telemetry:ConsoleExporter on,
        // the request carries a known trace id, and the exporter's console output names that trace with the request's path.
        using WebApplicationFactory<Program> factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Testing").UseSetting("Telemetry:ConsoleExporter", "true"));
        const string traceId = "4bf92f3577b34da6a3ce929d0e0e4736";
        TextWriter original = Console.Out;
        using StringWriter console = new();
        Console.SetOut(console);
        try
        {
            using HttpClient client = factory.CreateClient();
            client.DefaultRequestHeaders.Add("traceparent", $"00-{traceId}-00f067aa0ba902b7-01");

            using HttpResponseMessage response = await client.GetAsync(new Uri("/health/live", UriKind.Relative), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            // The server span ends after the response is on its way and is exported on that thread; wait for it to be printed.
            DateTimeOffset deadline = TimeProvider.System.GetUtcNow().AddSeconds(10);
            while (!console.ToString().Contains("url.path: /health/live", StringComparison.Ordinal) && TimeProvider.System.GetUtcNow() < deadline)
            {
                await Task.Delay(50, TestContext.Current.CancellationToken);
            }
        }
        finally
        {
            Console.SetOut(original);
        }

        string output = console.ToString();
        Assert.Contains($"Activity.TraceId:            {traceId}", output, StringComparison.Ordinal);
        Assert.Contains("url.path: /health/live", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("BatchSize", "0")]
    [InlineData("PollInterval", "00:00:00")]
    [InlineData("PollInterval", "-00:00:01")]
    public void Invalid_relay_options_stop_the_host_at_start_also_with_the_relay_disabled(string option, string value)
    {
        // Step 9 brief: Outbox:Relay:* are validated at every host start, enabled or not.
        using WebApplicationFactory<Program> factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Testing").UseSetting("Outbox:Relay:Enabled", "false").UseSetting($"Outbox:Relay:{option}", value));

        Exception failure = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains($"Outbox:Relay:{option}", failure.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("BatchSize", "0")]
    [InlineData("Interval", "00:00:00")]
    public void RR_42_invalid_purge_options_stop_the_host_at_start_also_with_the_purge_disabled(string option, string value)
    {
        // Milestone 2 step 2 (RR-42): Idempotency:Purge:* are validated at every host start, enabled or not, like the relay's.
        using WebApplicationFactory<Program> factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Testing").UseSetting("Idempotency:Purge:Enabled", "false").UseSetting($"Idempotency:Purge:{option}", value));

        Exception failure = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains($"Idempotency:Purge:{option}", failure.ToString(), StringComparison.Ordinal);
    }

    public void Dispose() => _factory.Dispose();
}

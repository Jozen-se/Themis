using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Themis.Host;

namespace Themis.Kernel.Tests;

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

    public void Dispose() => _factory.Dispose();
}

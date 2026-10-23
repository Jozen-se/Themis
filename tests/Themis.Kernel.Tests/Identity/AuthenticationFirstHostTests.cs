using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Themis.Host;

namespace Themis.Kernel.Tests.Identity;

/// <summary>
/// 401 first on Themis.Host (RR-71, R-SEC-01): without an identity provider the deny scheme stands in, so an anonymous
/// command or query is the shared 401 (header and problem body), never a 500; the health endpoints, OpenAPI and Scalar
/// answer without a token. No database is reached: the connection string points at a closed port.
/// </summary>
[Collection(HostTestGroup.Name)]
[Trait("Category", "Unit")]
public sealed class AuthenticationFirstHostTests
{
    private const string ConnectionString = "Host=localhost;Port=1;Database=themis;Username=themis_app;Password=none";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task RR_71_a_host_without_identity_answers_an_anonymous_command_and_query_401_with_the_header_and_body_not_500()
    {
        using WebApplicationFactory<Program> factory = Host("Testing");
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage command = await client.PostAsync(new Uri("/v1/kernel/items", UriKind.Relative), new StringContent("{not json", Encoding.UTF8, "application/json"), Ct);
        using HttpResponseMessage query = await client.GetAsync(new Uri("/v1/kernel/companies/" + Guid.NewGuid(), UriKind.Relative), Ct);

        foreach (HttpResponseMessage response in new[] { command, query })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal("Bearer", Assert.Single(response.Headers.WwwAuthenticate).ToString());
            JsonNode body = JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!;
            Assert.Equal(("authentication_required", "R-SEC-01"), (body["code"]!.GetValue<string>(), body["ruleId"]!.GetValue<string>()));
            Assert.False(string.IsNullOrWhiteSpace(body["correlationId"]?.GetValue<string>()));
        }
    }

    [Fact]
    public async Task RR_71_the_health_endpoints_answer_without_a_token()
    {
        using WebApplicationFactory<Program> factory = Host("Testing");
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage live = await client.GetAsync(new Uri("/health/live", UriKind.Relative), Ct);
        using HttpResponseMessage ready = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        // Not ready (the database is unreachable), but answered: the readiness check ran instead of a 401.
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
    }

    [Fact]
    public async Task RR_71_OpenAPI_and_Scalar_answer_without_a_token_in_Development()
    {
        using WebApplicationFactory<Program> factory = Host("Development");
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage openApi = await client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative), Ct);
        using HttpResponseMessage scalar = await client.GetAsync(new Uri("/scalar", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.OK, openApi.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, scalar.StatusCode);
        Assert.True(scalar.IsSuccessStatusCode || scalar.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.MovedPermanently, scalar.StatusCode.ToString());
    }

    private static WebApplicationFactory<Program> Host(string environment) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("ConnectionStrings:Themis", ConnectionString);
        });
}

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Themis.Integration.Tests.Pipeline;
using Themis.Kernel.Organisation;

namespace Themis.Integration.Tests.Hardening;

/// <summary>
/// 401 first (RR-71, R-SEC-01): the fallback authorization policy answers an anonymous request before the endpoint
/// reads a header or the body, with the one problem body (authentication_required under R-SEC-01, the correlation id)
/// and <c>WWW-Authenticate: Bearer</c> without error details. A token without a sub claim fails validation and is
/// anonymous too; an unmatched path answers 401 as well. The host without identity and the health endpoints are
/// proven on Themis.Host itself (HostIdentityTests, AuthenticationFirstHostTests).
/// </summary>
[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Integration")]
public sealed class AuthenticationFirstTests : IAsyncLifetime
{
    private readonly PostgresFixture _db;
    private PipelineTestHost _host = null!;

    public AuthenticationFirstTests(PostgresFixture db)
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

    [Fact]
    public async Task RR_71_an_anonymous_command_with_a_malformed_body_and_no_Idempotency_Key_gets_401_with_the_header_and_the_body()
    {
        using HttpRequestMessage request = new(HttpMethod.Post, "/v1/fixture/counters") { Content = new StringContent("{not json", Encoding.UTF8, "application/json") };
        request.Headers.Add("X-Correlation-Id", "rr71-command");

        (HttpResponseMessage response, JsonNode body) = await SendAsync(request);

        AssertUnauthorized(response, body, "rr71-command");
        Assert.Equal(0L, await _host.CountAsync("kernel.idempotency_record", Ct));
    }

    [Fact]
    public async Task RR_71_an_anonymous_query_gets_401_with_the_header_and_the_body()
    {
        using HttpRequestMessage request = new(HttpMethod.Get, $"/v1/kernel/companies/{Milestone1OrganisationFixture.CompanyId}");
        request.Headers.Add("X-Correlation-Id", "rr71-query");

        (HttpResponseMessage response, JsonNode body) = await SendAsync(request);

        AssertUnauthorized(response, body, "rr71-query");
    }

    [Fact]
    public async Task RR_71_a_token_without_a_sub_claim_fails_validation_and_gets_401()
    {
        using HttpRequestMessage request = new(HttpMethod.Post, "/v1/fixture/counters") { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.For(subject: null, TimeProvider.System));
        request.Headers.Add("Idempotency-Key", "rr71-no-sub");
        request.Headers.Add("X-Correlation-Id", "rr71-no-sub");

        (HttpResponseMessage response, JsonNode body) = await SendAsync(request);

        AssertUnauthorized(response, body, "rr71-no-sub");
        Assert.Equal(0L, await _host.CountAsync("kernel.idempotency_record", Ct));
    }

    [Fact]
    public async Task RR_71_an_anonymous_request_to_an_unmatched_path_gets_401()
    {
        using HttpRequestMessage request = new(HttpMethod.Get, "/v1/no/such/route");
        request.Headers.Add("X-Correlation-Id", "rr71-unmatched");

        (HttpResponseMessage response, JsonNode body) = await SendAsync(request);

        AssertUnauthorized(response, body, "rr71-unmatched");
    }

    [Fact]
    public async Task RR_71_a_signed_in_request_still_reaches_the_endpoint_and_its_own_checks()
    {
        // The same malformed command with a valid token: the endpoint runs and answers the missing key, not 401.
        CommandReply reply = await _host.PostAsync("/v1/fixture/counters", "{not json", idempotencyKey: null, cancellationToken: Ct);

        Assert.Equal(400, reply.Status);
        Assert.Equal("idempotency_key_required", reply.Code);
    }

    private async Task<(HttpResponseMessage Response, JsonNode Body)> SendAsync(HttpRequestMessage request)
    {
        HttpResponseMessage response = await _host.Client.SendAsync(request, Ct);
        string content = await response.Content.ReadAsStringAsync(Ct);
        return (response, JsonNode.Parse(content)!);
    }

    private static void AssertUnauthorized(HttpResponseMessage response, JsonNode body, string correlationId)
    {
        using (response)
        {
            Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
            AuthenticationHeaderValue challenge = Assert.Single(response.Headers.WwwAuthenticate);
            // No error details: the scheme alone, no error or error_description parameter.
            Assert.Equal(("Bearer", null), (challenge.Scheme, challenge.Parameter));
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal(("authentication_required", "R-SEC-01", correlationId), (body["code"]!.GetValue<string>(), body["ruleId"]!.GetValue<string>(), body["correlationId"]!.GetValue<string>()));
            Assert.Equal(401, body["status"]!.GetValue<int>());
        }
    }
}

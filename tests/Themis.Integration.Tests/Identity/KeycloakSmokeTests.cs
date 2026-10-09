using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Testcontainers.Keycloak;
using Themis.FixtureModule;
using Themis.FixtureModule.Contracts;
using Themis.Kernel.Contracts.Organisation;
using Themis.Kernel.Identity;
using Themis.Kernel.Organisation;
using Themis.Kernel.Persistence;
using Themis.Kernel.Pipeline;
using Themis.Kernel.Pipeline.Http;

namespace Themis.Integration.Tests.Identity;

/// <summary>
/// The one smoke test through Keycloak (step 4 brief, acceptance 8): the realm export of the compose file is
/// imported into a Keycloak container, the clerk signs in with the password grant, and the host, trusting that
/// realm as its authority (D-12), runs a command with the token. Set THEMIS_TEST_KEYCLOAK to the base URL of a
/// running Keycloak with the realm to skip the container; set THEMIS_TEST_REQUIRE_KEYCLOAK=true (CI) to fail
/// instead of skipping when no container runtime is available.
/// </summary>
[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Integration")]
public sealed class KeycloakSmokeTests
{
    private const string Image = "quay.io/keycloak/keycloak:26.8";

    private readonly PostgresFixture _db;

    public KeycloakSmokeTests(PostgresFixture db)
    {
        _db = db;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_clerk_signs_in_through_Keycloak_and_runs_a_command_with_the_token()
    {
        await using KeycloakSession keycloak = await KeycloakSession.StartAsync(Ct);
        string authority = keycloak.BaseAddress + "realms/themis";

        string token = await PasswordGrantAsync(authority, Milestone1IdentityFixture.ClerkUsername, Milestone1IdentityFixture.ClerkPassword);
        await using WebApplication app = await StartHostAsync(authority);
        using HttpClient client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using HttpRequestMessage request = new(HttpMethod.Post, "/v1/fixture/counters")
        {
            Content = new StringContent($$"""{"companyId": "{{Milestone1OrganisationFixture.CompanyId}}", "initialValue": 1, "sample": "1"}""", System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Idempotency-Key", "keycloak-" + Guid.NewGuid().ToString("N"));
        using HttpResponseMessage response = await client.SendAsync(request, Ct);
        string body = await response.Content.ReadAsStringAsync(Ct);

        Assert.True(response.IsSuccessStatusCode, body);
        Assert.Equal(Milestone1IdentityFixture.ClerkId, JsonNode.Parse(body)!["id"] is not null
            ? (Guid)(await ScalarAsync("SELECT user_id FROM kernel.audit_event WHERE command_name = 'ITEM.CREATE' ORDER BY occurred_at DESC LIMIT 1"))!
            : Guid.Empty);

        using HttpResponseMessage company = await client.GetAsync(new Uri($"/v1/kernel/companies/{Milestone1OrganisationFixture.CompanyId}", UriKind.Relative), Ct);
        Assert.Equal(System.Net.HttpStatusCode.OK, company.StatusCode);
    }

    private async Task<WebApplication> StartHostAsync(string authority)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Configuration["Identity:Authority"] = authority;
        builder.Configuration["Identity:Audience"] = "themis-api";
        builder.Configuration["Identity:RequireHttpsMetadata"] = "false";
        builder.Services.AddKernelPersistence(_db.ConnectionStringFor("themis_app"));
        builder.Services.AddKernelPipeline();
        builder.Services.AddKernelIdentity(builder.Configuration, builder.Environment.EnvironmentName);
        builder.Services.AddFixtureModule();
        builder.Services.AddKernelOrganisation();
        builder.Services.AddProblemDetails();
        WebApplication app = builder.Build();
        app.UseExceptionHandler();
        app.UseCorrelationId();
        app.UseAuthentication();
        app.MapCommandRoutes(FixtureContracts.Routes);
        app.MapQuery<GetCompany, CompanyInfo>(OrganisationRoutes.Company);
        await app.StartAsync(Ct);
        return app;
    }

    private static async Task<string> PasswordGrantAsync(string authority, string username, string password)
    {
        using HttpClient http = new();
        using FormUrlEncodedContent form = new(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = "themis-api",
            ["client_secret"] = "themis-dev-secret",
            ["username"] = username,
            ["password"] = password,
        });
        using HttpResponseMessage response = await http.PostAsync(new Uri(authority + "/protocol/openid-connect/token"), form, Ct);
        string body = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.IsSuccessStatusCode, body);
        return JsonNode.Parse(body)!["access_token"]!.GetValue<string>();
    }

    private async Task<object?> ScalarAsync(string sql)
    {
        await using Npgsql.NpgsqlConnection connection = await PostgresFixture.OpenAsync(_db.SuperuserConnectionString, Ct);
        await using Npgsql.NpgsqlCommand command = new(sql, connection);
        return await command.ExecuteScalarAsync(Ct);
    }

    /// <summary>A running Keycloak with the realm: an external one from THEMIS_TEST_KEYCLOAK, else a container.</summary>
    private sealed class KeycloakSession : IAsyncDisposable
    {
        private KeycloakContainer? _container;

        public string BaseAddress { get; private set; } = string.Empty;

        public static async Task<KeycloakSession> StartAsync(CancellationToken cancellationToken)
        {
            KeycloakSession session = new();
            string? external = Environment.GetEnvironmentVariable("THEMIS_TEST_KEYCLOAK");
            if (!string.IsNullOrWhiteSpace(external))
            {
                session.BaseAddress = external.TrimEnd('/') + "/";
                return session;
            }

            string realm = Path.Combine(AppContext.BaseDirectory, "docker", "keycloak", "themis-realm.json");
            try
            {
                session._container = new KeycloakBuilder(Image).WithRealm(realm).Build();
                await session._container.StartAsync(cancellationToken);
            }
            catch (Exception exception) when (!string.Equals(Environment.GetEnvironmentVariable("THEMIS_TEST_REQUIRE_KEYCLOAK"), "true", StringComparison.OrdinalIgnoreCase))
            {
                Assert.Skip($"No container runtime for Keycloak: {exception.Message}. Set THEMIS_TEST_KEYCLOAK to a running Keycloak with the themis realm, or THEMIS_TEST_REQUIRE_KEYCLOAK=true to fail.");
            }

            session.BaseAddress = session._container!.GetBaseAddress().TrimEnd('/') + "/";
            return session;
        }

        public async ValueTask DisposeAsync()
        {
            if (_container is not null)
            {
                await _container.DisposeAsync();
            }
        }
    }
}

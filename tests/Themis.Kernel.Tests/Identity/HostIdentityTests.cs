using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Themis.Host;
using Themis.Kernel.Contracts.Commands;
using Themis.Kernel.Contracts.Queries;
using Themis.Kernel.Pipeline;
using Themis.Kernel.Pipeline.Http;

namespace Themis.Kernel.Tests.Identity;

/// <summary>The host's identity wiring: the test issuer is refused outside Development and Test (step 4 brief, acceptance 9); an unscoped endpoint cannot be mapped (D-47).</summary>
[Trait("Category", "Unit")]
public sealed class HostIdentityTests
{
    private const string Key = "dGhpcy1pcy1hLXRlc3Qta2V5LW9mLWF0LWxlYXN0LTMyLWJ5dGVzLWxvbmc=";

    // A connection string registers persistence without connecting; a 401 is answered before any connection opens.
    private const string ConnectionString = "Host=localhost;Port=1;Database=themis;Username=themis_app;Password=none";

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Starting_the_host_with_the_test_issuer_configured_outside_Development_and_Test_fails(string environment)
    {
        using WebApplicationFactory<Program> factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("Identity:TestIssuer:Issuer", "https://test-issuer.themis.local");
            builder.UseSetting("Identity:TestIssuer:SigningKey", Key);
        });

        InvalidOperationException refused = Assert.ThrowsAny<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains("Identity:TestIssuer", refused.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public async Task The_test_issuer_is_accepted_in_Development_and_Test_and_an_anonymous_query_is_401(string environment)
    {
        using WebApplicationFactory<Program> factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("ConnectionStrings:Themis", ConnectionString);
            builder.UseSetting("Identity:TestIssuer:Issuer", "https://test-issuer.themis.local");
            builder.UseSetting("Identity:TestIssuer:SigningKey", Key);
        });
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/v1/kernel/companies/" + Guid.NewGuid(), UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Without_an_authority_every_query_is_401_and_the_host_still_serves_health()
    {
        using WebApplicationFactory<Program> factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Themis", ConnectionString);
        });
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage query = await client.GetAsync(new Uri("/v1/kernel/companies/" + Guid.NewGuid(), UriKind.Relative), TestContext.Current.CancellationToken);
        using HttpResponseMessage live = await client.GetAsync(new Uri("/health/live", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, query.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.OK, live.StatusCode);
    }

    [Fact]
    public void D_47_a_command_or_query_without_a_scope_cannot_be_mapped()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Services.AddKernelPipeline();
        using WebApplication app = builder.Build();

        InvalidOperationException command = Assert.Throws<InvalidOperationException>(() => app.MapCommand<UnscopedCommand, string>("/unscoped"));
        InvalidOperationException query = Assert.Throws<InvalidOperationException>(() => app.MapQuery<UnscopedQuery, string>("/unscoped"));
        InvalidOperationException verb = Assert.Throws<InvalidOperationException>(() => app.MapQuery<WrongVerbQuery, string>("/wrong"));

        Assert.Contains("declares no scope", command.Message, StringComparison.Ordinal);
        Assert.Contains("declares no scope", query.Message, StringComparison.Ordinal);
        Assert.Contains("TYPE.READ", verb.Message, StringComparison.Ordinal);
    }

    [Command("UNSCOPED.DO")]
    private sealed record UnscopedCommand : ICommand<string>;

    [Query("UNSCOPED.READ")]
    private sealed record UnscopedQuery : IQuery<string>;

    [Query("WRONG.LIST")]
    private sealed record WrongVerbQuery : IQuery<string>;
}

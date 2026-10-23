using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Scalar.AspNetCore;
using Themis.Host.Messaging;
using Themis.Host.Observability;
using Themis.Kernel.Contracts.Documents;
using Themis.Kernel.Contracts.MasterData;
using Themis.Kernel.Contracts.Organisation;
using Themis.Kernel.Contracts.Settings;
using Themis.Kernel.Documents;
using Themis.Kernel.Identity;
using Themis.Kernel.MasterData;
using Themis.Kernel.Messaging;
using Themis.Kernel.Organisation;
using Themis.Kernel.Persistence;
using Themis.Kernel.Pipeline;
using Themis.Kernel.Pipeline.Http;
using Themis.Kernel.Posting;
using Themis.Kernel.Settings;

namespace Themis.Host;

/// <summary>
/// Composition root. Wires the kernel and the modules into one deployable; holds no business logic and never
/// applies migrations (brief, Repository layout).
/// </summary>
public sealed class Program
{
    private Program()
    {
    }

    public static void Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        builder.AddThemisTelemetry();
        builder.Services.AddSingleton(TimeProvider.System);

        string? connectionString = builder.Configuration.GetConnectionString(HealthCheckExtensions.ConnectionStringName);
        bool databaseConfigured = !string.IsNullOrWhiteSpace(connectionString);
        if (databaseConfigured)
        {
            builder.Services.AddKernelPersistence(connectionString!);
        }

        builder.Services.AddThemisHealthChecks(databaseConfigured);
        builder.Services.AddKernelPipeline();
        // D-12: bearer tokens from the configured OpenID Connect provider; the test issuer only in Development and Test.
        builder.Services.AddKernelIdentity(builder.Configuration, builder.Environment.EnvironmentName);
        builder.Services.AddKernelOrganisation();
        // Milestone 2 step 2 (D-58): the settings catalogue, reader and commands; the pipeline reads the idempotency retention from it.
        builder.Services.AddKernelSettings();
        builder.Services.AddKernelMasterData();
        builder.Services.AddKernelPosting();
        builder.Services.AddKernelDocuments();
        // Step 9 (D-53): the relay's options are validated at every start; the loop itself needs the database.
        builder.Services.AddKernelMessaging(builder.Configuration);
        if (databaseConfigured)
        {
            builder.Services.AddHostedService<OutboxRelayHostedService>();
            // Milestone 2 step 2 (RR-42, D-58): the purge of expired idempotency records, outside the command pipeline.
            builder.Services.AddHostedService<IdempotencyPurgeHostedService>();
        }

        builder.Services.AddKernelOpenApi();
        builder.Services.AddKernelProblemDetails();

        WebApplication app = builder.Build();

        // RR-72: the correlation id surrounds the exception handler, so a 500 and its log line carry it.
        app.UseKernelRequestPipeline();
        if (app.Services.GetService<Microsoft.AspNetCore.Authentication.IAuthenticationSchemeProvider>() is not null)
        {
            app.UseAuthentication();
        }

        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
        app.MapQuery<GetCompany, CompanyInfo>(OrganisationRoutes.Company);
        app.MapCommandRoutes(MasterDataRoutes.Commands);
        app.MapQuery<GetItem, ItemView>(MasterDataRoutes.Item);
        app.MapQuery<GetItemByNumber, ItemView>(MasterDataRoutes.ItemByNumber);
        app.MapCommandRoutes(DocumentRoutes.Commands);
        app.MapQuery<GetInventoryAdjustment, InventoryAdjustmentView>(DocumentRoutes.InventoryAdjustment);
        app.MapCommandRoutes(SettingRoutes.Commands);
        app.MapQuery<GetSetting, SettingReadView>(SettingRoutes.Setting);

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
            app.MapScalarApiReference();
        }

        app.Run();
    }
}

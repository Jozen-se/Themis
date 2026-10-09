using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Scalar.AspNetCore;
using Themis.Host.Observability;
using Themis.Kernel.Contracts.Organisation;
using Themis.Kernel.Organisation;
using Themis.Kernel.Persistence;
using Themis.Kernel.Pipeline;
using Themis.Kernel.Pipeline.Http;

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
        builder.Services.AddKernelOrganisation();
        builder.Services.AddKernelOpenApi();
        builder.Services.AddProblemDetails();

        WebApplication app = builder.Build();

        app.UseExceptionHandler();
        app.UseCorrelationId();

        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
        app.MapQuery<GetCompany, CompanyInfo>(OrganisationRoutes.Company);

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
            app.MapScalarApiReference();
        }

        app.Run();
    }
}

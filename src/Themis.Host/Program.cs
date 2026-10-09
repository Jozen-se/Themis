using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Themis.Host.Observability;

namespace Themis.Host;

/// <summary>
/// Composition root. Wires the kernel and the modules into one deployable; holds no business logic.
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
        builder.Services.AddThemisHealthChecks(builder.Configuration);

        WebApplication app = builder.Build();

        app.UseCorrelationId();

        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

        app.Run();
    }
}

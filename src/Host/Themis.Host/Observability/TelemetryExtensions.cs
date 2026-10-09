using System.Reflection;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Themis.Host.Observability;

/// <summary>
/// Observability baseline (D-25): structured logs, traces and metrics through OpenTelemetry.
/// Export goes to an OTLP endpoint when <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is configured; otherwise
/// telemetry is collected but not exported, which keeps local development free of a collector.
/// </summary>
internal static class TelemetryExtensions
{
    internal const string ServiceName = "themis";

    public static WebApplicationBuilder AddThemisTelemetry(this WebApplicationBuilder builder)
    {
        string? version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        bool exportEnabled = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeScopes = true;
            logging.IncludeFormattedMessage = true;
            logging.ParseStateValues = true;
            if (exportEnabled)
            {
                logging.AddOtlpExporter();
            }
        });

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(ServiceName, serviceVersion: version))
            .WithTracing(tracing =>
            {
                tracing.AddAspNetCoreInstrumentation(options => options.RecordException = true);
                tracing.AddHttpClientInstrumentation();
                if (exportEnabled)
                {
                    tracing.AddOtlpExporter();
                }
            })
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation();
                metrics.AddHttpClientInstrumentation();
                metrics.AddRuntimeInstrumentation();
                if (exportEnabled)
                {
                    metrics.AddOtlpExporter();
                }
            });

        return builder;
    }
}

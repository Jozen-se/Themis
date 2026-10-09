using System.Reflection;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Themis.Kernel.Pipeline.Telemetry;

namespace Themis.Host.Observability;

/// <summary>
/// Observability baseline (D-25): structured logs, traces and metrics through OpenTelemetry.
/// Traces and logs go to the console exporter when <c>Telemetry:ConsoleExporter</c> is true (the development
/// default), and everything goes to an OTLP endpoint when <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is configured.
/// </summary>
internal static class TelemetryExtensions
{
    internal const string ServiceName = "themis";

    public static WebApplicationBuilder AddThemisTelemetry(this WebApplicationBuilder builder)
    {
        string? version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        bool otlpEnabled = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);
        bool consoleEnabled = builder.Configuration.GetValue("Telemetry:ConsoleExporter", builder.Environment.IsDevelopment());

        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeScopes = true;
            logging.IncludeFormattedMessage = true;
            logging.ParseStateValues = true;
            if (otlpEnabled)
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
                tracing.AddSource(PipelineTelemetry.SourceName);
                if (consoleEnabled)
                {
                    tracing.AddConsoleExporter();
                }

                if (otlpEnabled)
                {
                    tracing.AddOtlpExporter();
                }
            })
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation();
                metrics.AddHttpClientInstrumentation();
                metrics.AddRuntimeInstrumentation();
                metrics.AddMeter(PipelineTelemetry.SourceName);
                if (otlpEnabled)
                {
                    metrics.AddOtlpExporter();
                }
            });

        return builder;
    }
}

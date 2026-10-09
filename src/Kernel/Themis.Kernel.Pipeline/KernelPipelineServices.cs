using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Themis.Kernel.Contracts.Commands;
using Themis.Kernel.Contracts.Queries;
using Themis.Kernel.Contracts.Security;
using Themis.Kernel.Pipeline.Dispatch;
using Themis.Kernel.Pipeline.Json;
using Themis.Kernel.Pipeline.OpenApi;
using Themis.Kernel.Pipeline.Security;
using Themis.Kernel.Pipeline.Telemetry;

namespace Themis.Kernel.Pipeline;

/// <summary>Registers the command pipeline. Called by the composition root only; persistence is registered separately.</summary>
internal static class KernelPipelineServices
{
    public static IServiceCollection AddKernelPipeline(this IServiceCollection services, Action<KernelPipelineOptions>? configure = null)
    {
        services.AddOptions<KernelPipelineOptions>();
        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<PipelineTelemetry>();

        // D-37: the host's HTTP JSON options follow the same number contract, so the OpenAPI description declares
        // integers as integers and decimals as strings.
        services.ConfigureHttpJsonOptions(options => ThemisJson.Apply(options.SerializerOptions));
        services.AddScoped<ICommandDispatcher, CommandDispatcher>();
        services.AddScoped<IQueryDispatcher, QueryDispatcher>();

        // R-SEC-01: deny by default until the identity capability registers the real subject and access control.
        services.TryAddScoped<ICurrentSubject, NoCurrentSubject>();
        services.TryAddSingleton<IAccessControl, DenyAllAccessControl>();
        return services;
    }

    /// <summary>The API description with decimals declared as strings (D-37) and the command headers described (R-API-01, R-API-02).</summary>
    public static IServiceCollection AddKernelOpenApi(this IServiceCollection services) =>
        services.AddOpenApi(options =>
        {
            options.AddSchemaTransformer<DecimalSchemaTransformer>();
            options.AddSchemaTransformer<CommandSchemaTransformer>();
            options.AddOperationTransformer<CommandOperationTransformer>();
        });
}

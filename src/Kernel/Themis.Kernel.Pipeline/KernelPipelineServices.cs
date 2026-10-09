using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Themis.Kernel.Contracts.Commands;
using Themis.Kernel.Contracts.Security;
using Themis.Kernel.Pipeline.Dispatch;
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
        services.AddScoped<ICommandDispatcher, CommandDispatcher>();

        // R-SEC-01: deny by default until the identity capability registers the real principal and evaluator.
        services.TryAddScoped<ICurrentPrincipal, NoCurrentPrincipal>();
        services.TryAddSingleton<IPermissionEvaluator, DenyAllPermissionEvaluator>();
        return services;
    }

    /// <summary>The API description with decimals declared as strings (D-37).</summary>
    public static IServiceCollection AddKernelOpenApi(this IServiceCollection services) =>
        services.AddOpenApi(options => options.AddSchemaTransformer<DecimalSchemaTransformer>());
}
